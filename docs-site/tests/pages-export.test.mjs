import assert from "node:assert/strict";
import { access, readFile } from "node:fs/promises";
import test from "node:test";

const root = new URL("../", import.meta.url);

test("GitHub Pages export uses the repository base path and contains no guessed final release", async () => {
  const [html, contract] = await Promise.all([
    readFile(new URL("pages-dist/index.html", root), "utf8"),
    readFile(new URL("pages-dist/pages-build.json", root), "utf8").then(JSON.parse),
  ]);
  assert.equal(contract.basePath, "/Windows-Server-Setupper");
  assert.equal(contract.output, "pages-dist");
  assert.match(html, /<base href="\/Windows-Server-Setupper\/"\/>/);
  assert.match(html, /\/Windows-Server-Setupper\/_next\/static\//);
  assert.match(html, /Final release download: pending publication/);
  assert.match(html, /Previous verified release/);
  assert.doesNotMatch(html, /final-[0-9]|latest\/download/);
  await access(new URL("pages-dist/.nojekyll", root));
  await access(new URL("pages-dist/404.html", root));
  await access(new URL("pages-dist/brand/windows-server-setupper-logo.png", root));
});
