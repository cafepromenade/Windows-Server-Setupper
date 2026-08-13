import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import test from "node:test";

const root = new URL("../", import.meta.url);

async function render() {
  const workerUrl = new URL("../dist/server/index.js", import.meta.url);
  workerUrl.searchParams.set("test", `${process.pid}-${Date.now()}`);
  const { default: worker } = await import(workerUrl.href);
  return worker.fetch(
    new Request("http://localhost/", { headers: { accept: "text/html" } }),
    { ASSETS: { fetch: async () => new Response("Not found", { status: 404 }) } },
    { waitUntil() {}, passThroughOnException() {} },
  );
}

test("server-renders the finished product landing page", async () => {
  const response = await render();
  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-type") ?? "", /^text\/html\b/i);
  const html = await response.text();
  assert.match(html, /<title>Windows Server Setupper<\/title>/i);
  assert.match(html, /Server setup that remembers what finished\./);
  assert.match(html, /recovery-2026\.08\.13-50b75f17/);
  assert.match(html, /53c030076d2ddef4955ee0c45cf1beabf066a0f64be25512026cc38af1b89839/);
  assert.match(html, /Previous verified release/);
  assert.match(html, /Final release download: pending publication/);
  assert.match(html, /\.\/brand\/windows-server-setupper-logo\.png/);
  assert.doesNotMatch(html, /codex-preview|Your site is taking shape|react-loading-skeleton/);
});

test("starter artifacts are gone and finished metadata is present", async () => {
  const [page, layout, packageJson, publicFiles] = await Promise.all([
    readFile(new URL("app/page.tsx", root), "utf8"),
    readFile(new URL("app/layout.tsx", root), "utf8"),
    readFile(new URL("package.json", root), "utf8"),
    readdir(new URL("public/", root)),
  ]);
  assert.match(page, /<SiteShell \/>/);
  assert.match(layout, /Windows Server Setupper/);
  assert.doesNotMatch(layout, /next\/font\/google|codex-preview|Starter Project/);
  assert.doesNotMatch(packageJson, /react-loading-skeleton|site-creator-vinext-starter/);
  assert.ok(publicFiles.includes("sw.js"));
  assert.deepEqual(await readdir(new URL("app/_sites-preview/", root)), []);
});

test("approved shipped mark preserves its verified bytes", async () => {
  const image = new Uint8Array(await readFile(new URL("public/brand/windows-server-setupper-logo.png", root)));
  assert.deepEqual(Array.from(image.slice(0, 8)), [137, 80, 78, 71, 13, 10, 26, 10]);
  const digest = await crypto.subtle.digest("SHA-256", image);
  const hex = Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0")).join("");
  assert.equal(hex, "8e6333f433bc875a5829bfe7ad13e89630f7cbfbd7725a38be998593f769d03c");
});
