'use strict';

const fs = require('node:fs');
const path = require('node:path');

const inventoryPath = path.join(__dirname, '..', 'docs', 'universal-feature-inventory.md');
const inventory = fs.readFileSync(inventoryPath, 'utf8');
const required = ['P01.', ...Array.from({ length: 54 }, (_value, index) => `U${String(index + 1).padStart(2, '0')}.`)];
const missing = required.filter((id) => !inventory.includes(`| ${id}`));
if (missing.length) throw new Error(`Universal inventory is missing hand-written rows: ${missing.join(', ')}`);
for (const heading of ['Implementation evidence', 'Documentation article', 'Localized copy', 'Persistence', 'Focused tests', 'Packaged-artifact proof', 'Built interaction proof', 'Real capture', 'Release status']) if (!inventory.includes(heading)) throw new Error(`Universal inventory is missing evidence column ${heading}.`);
console.log(`Verified ${required.length} hand-written inventory rows and every required evidence column.`);
