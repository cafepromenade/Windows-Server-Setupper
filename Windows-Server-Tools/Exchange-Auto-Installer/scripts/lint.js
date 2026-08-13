'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

const root = path.join(__dirname, '..');
const files = walk(root).filter((file) => file.endsWith('.js') && !file.includes(`${path.sep}node_modules${path.sep}`) && !file.includes(`${path.sep}dist${path.sep}`));
for (const file of files) execFileSync(process.execPath, ['--check', file], { stdio: 'pipe' });
const html = fs.readFileSync(path.join(root, 'renderer', 'index.html'), 'utf8');
const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map((match) => match[1]);
const duplicates = ids.filter((id, index) => ids.indexOf(id) !== index);
if (duplicates.length) throw new Error(`Duplicate HTML identifiers: ${[...new Set(duplicates)].join(', ')}`);
if (/<script(?![^>]*\bsrc=)/i.test(html)) throw new Error('Inline renderer scripts are prohibited by the Content Security Policy.');
console.log(`Linted ${files.length} JavaScript files and ${ids.length} unique HTML identifiers.`);

function walk(directory) { return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => entry.isDirectory() ? walk(path.join(directory, entry.name)) : [path.join(directory, entry.name)]); }
