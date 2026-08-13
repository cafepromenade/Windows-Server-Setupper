import { readdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const guides = path.join(root, "content", "guides");
const destination = path.join(root, "app", "generated-docs.ts");

function parseArticle(filename, source) {
  const lines = source.replaceAll("\r\n", "\n").split("\n");
  const title = lines.find((line) => line.startsWith("# "))?.slice(2).trim();
  const category = lines.find((line) => line.startsWith("Category:"))?.slice("Category:".length).trim();
  const suggested = lines.find((line) => line.startsWith("Suggested:"))?.slice("Suggested:".length).split(",").map((item) => item.trim()).filter(Boolean) ?? [];
  if (!title || !category || suggested.length === 0) {
    throw new Error(`${filename} must declare a title, category, and at least one suggested article`);
  }
  const sections = [];
  let current = null;
  for (const line of lines) {
    if (line.startsWith("## ")) {
      current = { heading: line.slice(3).trim(), paragraphs: [] };
      sections.push(current);
    } else if (current && line.trim() && !line.startsWith("Category:") && !line.startsWith("Suggested:")) {
      current.paragraphs.push(line.trim());
    }
  }
  if (!sections.length || sections.some((section) => section.paragraphs.length === 0)) {
    throw new Error(`${filename} must contain non-empty level-two sections`);
  }
  return {
    id: path.basename(filename, ".md"),
    title,
    category,
    body: sections.flatMap((section) => [section.heading, ...section.paragraphs]).join(" "),
    sections,
    suggested,
  };
}

const filenames = (await readdir(guides)).filter((name) => name.endsWith(".md") && name !== "README.md").sort();
const articles = [];
for (const filename of filenames) {
  articles.push(parseArticle(filename, await readFile(path.join(guides, filename), "utf8")));
}
if (articles.length < 14) throw new Error(`Expected at least 14 bundled articles, found ${articles.length}`);

await writeFile(destination, `// Generated from content/guides by scripts/generate-docs.mjs.\nconst docs = ${JSON.stringify(articles, null, 2)} as const;\nexport default docs;\n`, "utf8");
console.log(`Bundled ${articles.length} documentation articles.`);
