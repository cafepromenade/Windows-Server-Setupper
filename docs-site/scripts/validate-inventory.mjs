import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const requiredFeatureIds = [
  "material-3",
  "responsive-accessibility",
  "tabs",
  "regex-builder",
  "language-modes",
  "dialog-emoji",
  "school-mode",
  "personal-vocabulary",
  "narrator",
  "scheduled-settings",
  "notifications",
  "appearance-editor",
  "custom-logo",
  "file-converter",
  "ollama-manager",
  "authenticator",
  "toy-locks",
  "support-tickets",
  "command-palette",
  "offline-docs",
  "changelog",
  "destructive-confirmation",
  "local-history",
  "release-download",
  "hosting"
];

export const requiredProofFields = [
  "name",
  "status",
  "implementation",
  "article",
  "localized",
  "test",
  "interaction",
  "capture"
];

export function validateInventory(inventory) {
  const errors = [];
  if (inventory.version !== "site-contract-v1") errors.push("inventory version must be site-contract-v1");
  if (!Array.isArray(inventory.features)) errors.push("features must be an array");
  const features = Array.isArray(inventory.features) ? inventory.features : [];
  const ids = features.map((feature) => feature.id);
  if (new Set(ids).size !== ids.length) errors.push("feature identifiers must be unique");
  for (const id of requiredFeatureIds) {
    const feature = features.find((candidate) => candidate.id === id);
    if (!feature) {
      errors.push(`missing canonical feature: ${id}`);
      continue;
    }
    for (const field of requiredProofFields) {
      if (typeof feature[field] !== "string" || !feature[field].trim()) {
        errors.push(`${id} is missing proof field: ${field}`);
      }
    }
  }
  for (const id of ids) {
    if (!requiredFeatureIds.includes(id)) errors.push(`unknown feature identifier: ${id}`);
  }
  return errors;
}

async function main() {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
  const inventory = JSON.parse(await readFile(path.join(root, "content", "completeness-inventory.json"), "utf8"));
  const errors = validateInventory(inventory);
  if (errors.length) {
    console.error(errors.join("\n"));
    process.exitCode = 1;
  } else {
    console.log(`Inventory verified: ${inventory.features.length} canonical feature rows with ${requiredProofFields.length} proof fields each.`);
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
