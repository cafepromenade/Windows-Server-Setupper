'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const test = require('node:test');
const { InstallerEngine } = require('../src/installer-engine');
const { redactObject, redactText } = require('../src/redaction');
const { StateStore } = require('../src/state-store');

function temporaryRoot() { return fs.mkdtempSync(path.join(os.tmpdir(), 'exchange-auto-installer-test-')); }

test('redaction removes full quoted assignments, headers, flags, camel and snake keys', () => {
  const source = 'password="two word secret" Authorization: Bearer abc.def -Credential \'three word value\' apiKey=hello client_secret=world';
  const redacted = redactText(source);
  for (const secret of ['two word secret', 'abc.def', 'three word value', 'hello', 'world']) assert.equal(redacted.includes(secret), false);
  const object = redactObject({ apiKey: 'one', api_key: 'two', nested: { authorization: 'three' }, safe: 'kept' });
  assert.deepEqual(object, { apiKey: '[REDACTED]', api_key: '[REDACTED]', nested: { authorization: '[REDACTED]' }, safe: 'kept' });
});

test('StateStore refuses a second runtime and uses monotonic compare-and-swap revisions', () => {
  const root = temporaryRoot();
  const first = new StateStore(root);
  first.load({});
  assert.throws(() => new StateStore(root).load({}), /another Exchange installer runtime/i);
  const before = first.snapshot().revision;
  first.update((state) => { state.phase = 'review'; });
  assert.equal(first.snapshot().revision, before + 1);
  first.releaseLease();
});

test('corrupt primary and backup are preserved and never collapsed to fresh pending state', () => {
  const root = temporaryRoot();
  const directory = path.join(root, 'exchange-auto-installer');
  fs.mkdirSync(directory, { recursive: true });
  fs.writeFileSync(path.join(directory, 'installation-state.json'), '{broken');
  fs.writeFileSync(path.join(directory, 'installation-state.backup.json'), '{also broken');
  const store = new StateStore(root);
  assert.throws(() => store.load({}), /corrupt or unsupported/i);
  assert.equal(fs.readFileSync(path.join(directory, 'installation-state.json'), 'utf8'), '{broken');
  assert.equal(fs.readdirSync(directory).some((name) => name.startsWith('invalid-state-')), true);
  store.releaseLease();
});

test('uncertain stage blocks resume and retry until a fresh one-use reconciliation token is accepted', async () => {
  const root = temporaryRoot();
  const engine = new InstallerEngine({ userDataDir: root, defaults: {}, onState: () => {} });
  engine.update((state) => {
    const stage = state.stages[0];
    stage.status = 'uncertain';
    stage.reconciliationToken = 'fresh-token';
    stage.indeterminateEvidence = { warning: 'prior process may remain' };
    state.phase = 'uncertain';
  });
  await assert.rejects(() => engine.resume(), /indeterminate outcome/i);
  await assert.rejects(() => engine.retryStage('windows-features'), /Reconcile it before any retry/i);
  assert.throws(() => engine.reconcileStage({ stageId: 'windows-features', token: 'stale-token', outcome: 'confirmed-stopped-no-changes' }), /stale/i);
  const reconciled = engine.reconcileStage({ stageId: 'windows-features', token: 'fresh-token', outcome: 'confirmed-stopped-no-changes' });
  assert.equal(reconciled.stages[0].status, 'failed');
  assert.equal(reconciled.stages[0].reconciliationToken, null);
  assert.throws(() => engine.reconcileStage({ stageId: 'windows-features', token: 'fresh-token', outcome: 'confirmed-completed' }), /missing or has already changed/i);
  engine.close();
});
