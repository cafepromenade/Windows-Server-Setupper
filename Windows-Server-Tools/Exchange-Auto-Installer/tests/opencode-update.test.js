'use strict';

const assert = require('node:assert/strict');
const { EventEmitter } = require('node:events');
const test = require('node:test');
const { OPENCODE_ARCHIVE_BYTES, OPENCODE_ARCHIVE_SHA256, OPENCODE_ARCHIVE_URL, OPENCODE_VERSION } = require('../src/opencode-manager');
const { createUpdateManager } = require('../src/update-manager');

test('OpenCode official URL, byte count, and digest are pinned as one release-metadata contract', () => {
  assert.equal(OPENCODE_VERSION, '1.18.18');
  assert.equal(OPENCODE_ARCHIVE_URL, 'https://github.com/anomalyco/opencode/releases/download/v1.18.18/opencode-windows-x64.zip');
  assert.equal(OPENCODE_ARCHIVE_BYTES, 60_504_740);
  assert.equal(OPENCODE_ARCHIVE_SHA256, 'c6d265376fdb93164013671b0cf402410184f73c34fc15d82d40a16a745b15f4');
});

test('automatic updater reports checking, failure, download, and ready-to-restart without guessing', async () => {
  class FakeUpdater extends EventEmitter { setFeedURL(value) { this.feed = value; } async checkForUpdates() { this.emit('checking-for-update'); } quitAndInstall() { this.restarted = true; } }
  const updater = new FakeUpdater();
  const app = { isPackaged: true, getVersion: () => '1.0.0' };
  const states = [];
  const manager = createUpdateManager({ app, autoUpdater: updater, onState: (state) => states.push(state), intervalMs: 1_000_000 });
  manager.configure();
  await manager.check();
  assert.equal(manager.getState().status, 'checking');
  updater.emit('error', new Error('offline'));
  assert.equal(manager.getState().status, 'failed');
  updater.emit('update-available', { version: '1.0.1' });
  assert.equal(manager.getState().status, 'downloading');
  updater.emit('update-downloaded', {}, 'notes', '1.0.1');
  assert.equal(manager.getState().status, 'ready');
  manager.restartToInstall();
  assert.equal(updater.restarted, true);
  assert.equal(states.length >= 5, true);
  manager.dispose();
});
