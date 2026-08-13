'use strict';

const fs = require('node:fs');
const path = require('node:path');

const projectRoot = path.resolve(__dirname, '..');
const outputPath = path.resolve(projectRoot, 'dist');

if (path.dirname(outputPath) !== projectRoot || path.basename(outputPath) !== 'dist') {
  throw new Error(`Refusing to clean unexpected output path: ${outputPath}`);
}

fs.rmSync(outputPath, { recursive: true, force: true });
process.stdout.write(`Cleared generated package output: ${outputPath}\n`);
