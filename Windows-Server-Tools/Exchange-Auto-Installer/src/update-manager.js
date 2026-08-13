'use strict';

const DEFAULT_FEED_URL = 'https://github.com/cafepromenade/Windows-Server-Setupper/releases/latest/download';
const CHECK_INTERVAL_MS = 6 * 60 * 60 * 1000;

function createUpdateManager({ app, autoUpdater, onState = () => {}, feedUrl = DEFAULT_FEED_URL, intervalMs = CHECK_INTERVAL_MS }) {
  let timer = null;
  let state = { status: app.isPackaged ? 'idle' : 'development', currentVersion: app.getVersion(), updateVersion: null, releaseNotesUrl: null, error: null };
  const emit = (patch) => { state = { ...state, ...patch, checkedAt: new Date().toISOString() }; onState({ ...state }); return { ...state }; };

  autoUpdater.on('checking-for-update', () => emit({ status: 'checking', error: null }));
  autoUpdater.on('update-available', (info) => emit({ status: 'downloading', updateVersion: info.version || null, error: null }));
  autoUpdater.on('update-not-available', () => emit({ status: 'current', updateVersion: null, error: null }));
  autoUpdater.on('update-downloaded', (_event, releaseNotes, releaseName) => emit({ status: 'ready', updateVersion: releaseName || null, releaseNotes: normalizeNotes(releaseNotes), releaseNotesUrl: 'https://github.com/cafepromenade/Windows-Server-Setupper/releases/latest', error: null }));
  autoUpdater.on('error', (error) => emit({ status: 'failed', error: safeMessage(error) }));

  function configure() {
    if (!app.isPackaged) return emit({ status: 'development' });
    autoUpdater.setFeedURL({ url: feedUrl });
    if (!timer) timer = setInterval(() => { check().catch(() => {}); }, intervalMs);
    if (typeof timer.unref === 'function') timer.unref();
    return emit({ status: 'idle' });
  }

  async function check() {
    if (!app.isPackaged) return emit({ status: 'development' });
    emit({ status: 'checking', error: null });
    await autoUpdater.checkForUpdates();
    return { ...state };
  }

  function restartToInstall() {
    if (state.status !== 'ready') throw new Error('No downloaded update is ready to install.');
    autoUpdater.quitAndInstall(false, true);
    return { restarting: true };
  }

  function dispose() { if (timer) clearInterval(timer); timer = null; }
  return { check, configure, dispose, getState: () => ({ ...state }), restartToInstall };
}

function normalizeNotes(notes) { if (Array.isArray(notes)) return notes.map((entry) => String(entry.note || '')).join('\n').slice(0, 20_000); return String(notes || '').slice(0, 20_000); }
function safeMessage(error) { return String(error?.message || error || 'Update check failed.').replace(/[\r\n\0]/g, ' ').slice(0, 1_000); }

module.exports = { CHECK_INTERVAL_MS, DEFAULT_FEED_URL, createUpdateManager };
