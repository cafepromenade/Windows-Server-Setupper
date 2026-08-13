import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import test from "node:test";
import { requiredFeatureIds, requiredProofFields, validateInventory } from "../scripts/validate-inventory.mjs";

const root = new URL("../", import.meta.url);
const inventory = JSON.parse(await readFile(new URL("content/completeness-inventory.json", root), "utf8"));
const shell = await readFile(new URL("app/site-shell.tsx", root), "utf8");
const css = await readFile(new URL("app/globals.css", root), "utf8");

test("the hand-written feature inventory is complete", () => {
  assert.deepEqual(validateInventory(inventory), []);
  assert.deepEqual(inventory.features.map((feature) => feature.id), requiredFeatureIds);
});

test("negative regression turns red when any canonical row disappears", () => {
  for (const id of requiredFeatureIds) {
    const broken = structuredClone(inventory);
    broken.features = broken.features.filter((feature) => feature.id !== id);
    assert.ok(validateInventory(broken).some((error) => error === `missing canonical feature: ${id}`), id);
  }
});

test("negative regression turns red when any proof disappears", () => {
  for (const feature of inventory.features) {
    for (const field of requiredProofFields) {
      const broken = structuredClone(inventory);
      broken.features.find((candidate) => candidate.id === feature.id)[field] = "";
      assert.ok(validateInventory(broken).some((error) => error === `${feature.id} is missing proof field: ${field}`), `${feature.id}.${field}`);
    }
  }
});

test("every inventory article exists in the bundle source", async () => {
  const articleNames = new Set((await readdir(new URL("content/guides/", root))).filter((name) => name.endsWith(".md")));
  for (const feature of inventory.features) {
    const name = feature.article.split("/").at(-1);
    assert.ok(articleNames.has(name), `${feature.id}: ${name}`);
  }
});

test("every named search surface uses the anchored builder component", () => {
  const ids = [
    "tab-strip-search",
    "documentation-search",
    "settings-search",
    "voice-search",
    "current-strip-search",
    "group-tabs-search",
    "groups-search",
    "master-tabs-search",
    "completeness-search",
    "tab-context-search",
    "appearance-search",
    "command-search"
  ];
  for (const id of ids) assert.match(shell, new RegExp(`id=\\"${id}\\"`), id);
  assert.match(shell, /function SearchField\(/);
  assert.match(shell, /aria-label=\{`Open regular expression builder/);
  assert.match(css, /\.regex-builder\s*\{[^}]*position:\s*absolute/s);
});

test("private inputs remain local and destructive reset is absent", () => {
  assert.doesNotMatch(shell, /localStorage\.clear\s*\(/);
  assert.doesNotMatch(shell, /sessionStorage/);
  assert.match(shell, /VOCABULARY_KEY/);
  assert.match(shell, /LOGO_KEY/);
  assert.match(shell, /crypto\.subtle\.digest/);
  assert.match(shell, /http:\/\/127\.0\.0\.1:11434\/api\/version/);
  assert.doesNotMatch(shell, /https?:\/\/(?!127\.0\.0\.1:11434|github\.com\/cafepromenade\/Windows-Server-Setupper)/);
});

test("responsive, focus, overlay and reduced-motion contracts are explicit", () => {
  for (const token of ["@media (max-width: 980px)", "@media (max-width: 760px)", "@media (max-width: 420px)", "prefers-reduced-motion", ":focus-visible", ".overlay-card", ".dialog-scrim"]) {
    assert.ok(css.includes(token), token);
  }
  assert.match(shell, /className="skip-link"/);
  assert.match(shell, /aria-orientation=/);
  assert.match(shell, /Ctrl<\/kbd>\+<kbd>Shift<\/kbd>\+<kbd>F/);
});
