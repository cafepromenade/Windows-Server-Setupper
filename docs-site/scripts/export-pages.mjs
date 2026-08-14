import { cp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const output = path.join(root, "pages-dist");
const baseIndex = process.argv.indexOf("--base-path");
const basePath = baseIndex >= 0 ? process.argv[baseIndex + 1] : "/Windows-Server-Setupper";

if (!/^\/[A-Za-z0-9._-]+$/.test(basePath)) {
  throw new Error(`Base path must be one project segment such as /Windows-Server-Setupper; received ${basePath}`);
}

const serverEntry = path.join(root, "dist", "server", "index.js");
const moduleUrl = pathToFileURL(serverEntry);
moduleUrl.searchParams.set("pages-export", `${Date.now()}`);
const { default: worker } = await import(moduleUrl.href);
const response = await worker.fetch(
  new Request("http://localhost/", { headers: { accept: "text/html" } }),
  { ASSETS: { fetch: async () => new Response("Not found", { status: 404 }) } },
  { waitUntil() {}, passThroughOnException() {} },
);
if (!response.ok) throw new Error(`Server render failed with ${response.status}`);

let html = await response.text();
html = html
  .replaceAll('href="/_next/', `href="${basePath}/_next/`)
  .replaceAll('src="/_next/', `src="${basePath}/_next/`)
  .replaceAll('href="/brand/', `href="${basePath}/brand/`)
  .replaceAll('src="/brand/', `src="${basePath}/brand/`)
  .replace('{"pathname":"/","searchParams":[]}', `{"pathname":"${basePath}/","searchParams":[]}`)
  .replace("<head>", `<head><base href="${basePath}/"/>`);

await rm(output, { recursive: true, force: true });
await mkdir(output, { recursive: true });
await cp(path.join(root, "dist", "client"), output, { recursive: true });
await writeFile(path.join(output, "index.html"), html, "utf8");
await writeFile(path.join(output, "404.html"), html, "utf8");
await writeFile(path.join(output, ".nojekyll"), "", "utf8");
await writeFile(
  path.join(output, "pages-build.json"),
  `${JSON.stringify({ schemaVersion: 1, basePath, source: "dist/server/index.js", output: "pages-dist" }, null, 2)}\n`,
  "utf8",
);

const rendered = await readFile(path.join(output, "index.html"), "utf8");
for (const expected of [
  `<base href="${basePath}/"/>`,
  `${basePath}/_next/static/`,
  "Server setup that remembers what finished.",
  "Windows Server Setupper documentation · Windows build 6.1",
]) {
  if (!rendered.includes(expected)) throw new Error(`Static export is missing ${expected}`);
}
console.log(`GitHub Pages export ready at ${output} with base path ${basePath}/.`);
