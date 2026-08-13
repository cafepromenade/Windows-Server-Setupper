'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

const root = path.join(__dirname, '..');
const requirements = Object.freeze([
  ['renderer/index.html', 'id="settings-search"'], ['renderer/index.html', 'id="regex-dialog"'], ['renderer/index.html', 'id="command-palette"'],
  ['renderer/index.html', 'id="update-banner"'], ['renderer/index.html', 'id="uncertain-reconciliation"'], ['renderer/index.html', 'id="converter-catalog"'],
  ['renderer/index.html', 'id="ollama-status"'], ['renderer/index.html', 'id="docs-article"'], ['renderer/universal-features.js', 'event.ctrlKey && event.shiftKey'],
  ['src/settings-store.js', 'validatePersonalVocabulary'], ['src/installer-engine.js', 'reconciliationToken'], ['src/media-hydrator.js', '--verify-only'],
  ['src/update-manager.js', 'restartToInstall'], ['package.json', 'assets/app.ico'], ['docs/universal-feature-inventory.md', '| U54.']
]);

function check(load) {
  for (const [file, token] of requirements) {
    const source = load(file);
    if (!source.includes(token)) throw new Error(`${file} is missing exact universal contract token ${token}`);
  }
}

test('universal contract check is green at exact declared boundaries', () => {
  assert.doesNotThrow(() => check((file) => fs.readFileSync(path.join(root, file), 'utf8')));
});

test('negative regression turns red when each asserted registration disappears entirely', () => {
  for (const [removedFile, removedToken] of requirements) {
    assert.throws(() => check((file) => {
      const source = fs.readFileSync(path.join(root, file), 'utf8');
      return file === removedFile ? source.split(removedToken).join('INTENTIONALLY_REMOVED_FOR_NEGATIVE_REGRESSION') : source;
    }), new RegExp(removedFile.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
  }
});
