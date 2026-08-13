'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');
const { catalog, convertBuffer, detectType } = require('../src/conversion-manager');
const { validateLocalMediaPath } = require('../src/preflight');
const { PERSONAL_VOCABULARY_MAX_BYTES, validatePersonalVocabulary, validateSettings } = require('../src/settings-store');

test('settings accept the complete bounded baseline and reject unknown or out-of-range fields', () => {
  const valid = validateSettings({ schemaVersion: 1, language: 'bilingual', funnyEnglish: 1, funnyCantonese: 5, showDialogEmojis: false, theme: 'dark', density: 'compact', accent: '#123456', fontFamily: 'Segoe UI', fontScale: 1.25, fontWeight: 600, reducedMotion: true, tabDock: 'right', appDisplayName: 'Mail setup', schoolMode: false, schoolModeName: 'Focus mode', narratorEnabled: false, narratorLanguage: 'yue', narratorVoiceEnglish: 'auto', narratorVoiceCantonese: 'auto', narrationRate: 1, narrationPitch: 1, updateEnabled: true, schedules: [] });
  assert.equal(valid.language, 'bilingual');
  assert.throws(() => validateSettings({ ...valid, surpriseField: true }), /Unknown setting/);
  assert.throws(() => validateSettings({ ...valid, funnyEnglish: 6 }), /supported range/);
});

test('personal vocabulary validates bounds, duplicate keys, unsafe keys, and string replacements', () => {
  assert.deepEqual(validatePersonalVocabulary(Buffer.from('{"schemaVersion":1,"replacements":{"hello":"world"}}')), { schemaVersion: 1, replacements: { hello: 'world' } });
  assert.throws(() => validatePersonalVocabulary(Buffer.from('{"schemaVersion":1,"replacements":{"hello":"one","hello":"two"}}')), /duplicate/i);
  assert.throws(() => validatePersonalVocabulary(Buffer.from('{"schemaVersion":1,"replacements":{"__proto__":"bad"}}')), /unsafe/i);
  assert.throws(() => validatePersonalVocabulary(Buffer.alloc(PERSONAL_VOCABULARY_MAX_BYTES + 1)), /size/i);
});

test('converter registry exposes all categories and refuses every unbundled adapter', () => {
  const registry = catalog();
  assert.equal(registry.categories.length, 8);
  assert.equal(registry.adapters.some((adapter) => adapter.enabled && !adapter.bundled), false);
  for (const adapter of registry.adapters.filter((entry) => !entry.enabled)) assert.throws(() => convertBuffer(adapter.id, Buffer.from('x')), /not bundled|unavailable|is bundled/i);
  assert.equal(detectType(Buffer.from('{"answer":42}')), 'application/json');
  assert.equal(convertBuffer('json-pretty', Buffer.from('{"answer":42}')).toString(), '{\n  "answer": 42\n}\n');
  assert.equal(convertBuffer('binary-base64', Buffer.from('hello')).toString(), 'aGVsbG8=');
  assert.equal(convertBuffer('base64-binary', Buffer.from('aGVsbG8=')).toString(), 'hello');
});

test('UNC, device, relative, and missing media paths are rejected before trusted inspection', () => {
  for (const candidate of ['\\\\server\\share\\Setup.exe', '\\\\?\\C:\\Setup.exe', '\\\\.\\PhysicalDrive0', 'relative\\Setup.exe', 'C:\\definitely-missing\\Setup.exe']) {
    const result = validateLocalMediaPath(candidate);
    assert.equal(result.ok, false, candidate);
  }
});
