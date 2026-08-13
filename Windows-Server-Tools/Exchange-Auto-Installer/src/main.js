'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { app, BrowserWindow, dialog, ipcMain, shell } = require('electron');
const { InstallerEngine } = require('./installer-engine');
const { detectLocalDefaults } = require('./preflight');

if (require('electron-squirrel-startup')) app.quit();

let mainWindow = null;
let engine = null;
let openCodeManager = null;

function sendState(state) {
  if (mainWindow && !mainWindow.isDestroyed()) mainWindow.webContents.send('exchange:state', state);
}

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1340,
    height: 900,
    minWidth: 880,
    minHeight: 640,
    show: false,
    title: 'Exchange Auto Installer',
    autoHideMenuBar: true,
    backgroundColor: '#f8f9ff',
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      sandbox: true,
      nodeIntegration: false,
      webSecurity: true,
      allowRunningInsecureContent: false,
      spellcheck: false
    }
  });
  mainWindow.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  mainWindow.webContents.on('will-navigate', (event, url) => {
    if (url !== mainWindow.webContents.getURL()) event.preventDefault();
  });
  mainWindow.once('ready-to-show', () => mainWindow.show());
  mainWindow.on('closed', () => { mainWindow = null; });
  mainWindow.loadFile(path.join(__dirname, '..', 'renderer', 'index.html'));
}

function registerIpc() {
  const handle = (channel, handler) => ipcMain.handle(channel, async (_event, ...args) => {
    try { return await handler(...args); }
    catch (error) { throw new Error(safeError(error)); }
  });

  handle('exchange:get-state', () => engine.getPublicState());
  handle('exchange:choose-media', async () => {
    const selection = await dialog.showOpenDialog(mainWindow, {
      title: 'Choose Exchange Setup.exe or mounted media folder',
      properties: ['openFile', 'openDirectory'],
      filters: [{ name: 'Exchange Setup', extensions: ['exe'] }]
    });
    if (selection.canceled || !selection.filePaths[0]) return null;
    await engine.inspectMedia(selection.filePaths[0]);
    return selection.filePaths[0];
  });
  handle('exchange:inspect-media', (candidatePath) => engine.inspectMedia(assertPath(candidatePath)));
  handle('exchange:update-profile', (patch) => engine.updateProfile(assertPlainObject(patch)));
  handle('exchange:run-preflight', () => engine.preflight());
  handle('exchange:start-install', (acknowledgement) => {
    const accepted = assertPlainObject(acknowledgement).accepted === true;
    if (!accepted) throw new Error('Review the plan and accept the applicable Exchange license terms first.');
    return engine.start({ licenseAccepted: true });
  });
  handle('exchange:retry-stage', (stageId) => engine.retryStage(assertIdentifier(stageId)));
  handle('exchange:resume-install', () => engine.resume());
  handle('exchange:request-cancel', () => engine.requestCancel());
  handle('exchange:reveal-logs', async () => {
    const logPath = engine.getState().logPath;
    if (!logPath) throw new Error('No log folder is available yet.');
    const result = await shell.openPath(path.dirname(logPath));
    if (result) throw new Error(result);
    return { opened: true };
  });
  handle('exchange:export-logs', async () => {
    const source = engine.getState().logPath;
    if (!source || !fs.existsSync(source)) throw new Error('There are no redacted logs to export.');
    const destination = await dialog.showSaveDialog(mainWindow, {
      title: 'Export redacted installation log',
      defaultPath: `Exchange-installation-${new Date().toISOString().slice(0, 10)}.jsonl`,
      filters: [{ name: 'JSON Lines', extensions: ['jsonl'] }]
    });
    if (destination.canceled || !destination.filePath) return null;
    fs.copyFileSync(source, destination.filePath);
    return { path: destination.filePath };
  });

  handle('exchange:opencode-status', () => requireOpenCode().status());
  handle('exchange:opencode-install-or-repair', () => requireOpenCode().installOrRepair());
  handle('exchange:opencode-set-yolo', (request) => requireOpenCode().setYoloMode(assertPlainObject(request)));
  handle('exchange:opencode-run-repair', async (request) => {
    const manager = requireOpenCode();
    const result = await manager.runRepairAdvisor(assertPlainObject(request), {
      installerState: engine.getPublicState(),
      onProgress: (progress) => sendState({ ...engine.getPublicState(), openCodeProgress: progress })
    });
    if (result.autoApprove) result.applied = await applyRepairActions(manager.approveRepairActions({ planId: result.planId, actionIds: result.actionIds }));
    return result;
  });
  handle('exchange:opencode-apply-repair', async (request) => {
    const approved = requireOpenCode().approveRepairActions(assertPlainObject(request));
    return applyRepairActions(approved);
  });
  handle('exchange:opencode-cancel-repair', () => requireOpenCode().cancelActiveRun());
}

function requireOpenCode() {
  if (!openCodeManager) throw new Error('OpenCode integration is unavailable in this build.');
  return openCodeManager;
}

async function applyRepairActions(plan) {
  const results = [];
  for (const actionId of plan.actionIds) {
    if (actionId === 'reinspect_media') {
      const mediaPath = engine.getState().media?.path;
      if (!mediaPath) throw new Error('No selected Exchange media is available to reinspect.');
      results.push({ actionId, result: await engine.inspectMedia(mediaPath) });
    } else if (actionId === 'refresh_preflight') {
      results.push({ actionId, result: await engine.preflight() });
    } else if (actionId === 'retry_failed_stage') {
      const failed = engine.getState().stages.find((stage) => ['failed', 'uncertain', 'cancelled'].includes(stage.status));
      if (!failed) throw new Error('No stopped Exchange stage is available to retry.');
      results.push({ actionId, result: await engine.retryStage(failed.id) });
    } else if (actionId === 'resume_installation') {
      results.push({ actionId, result: await engine.resume() });
    } else if (actionId === 'export_redacted_logs') {
      results.push({ actionId, result: { requiresNativePicker: true } });
    } else {
      throw new Error('The repair action is not allowlisted.');
    }
  }
  return { planId: plan.planId, results };
}

function assertPath(value) {
  const text = String(value || '').trim();
  if (!text || text.length > 1_024 || /[\r\n\0]/.test(text) || !path.isAbsolute(text)) throw new Error('Select an absolute local media path.');
  return text;
}

function assertIdentifier(value) {
  const text = String(value || '');
  if (!/^[a-z0-9-]{1,80}$/i.test(text)) throw new Error('The identifier is not allowed.');
  return text;
}

function assertPlainObject(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value) || Object.getPrototypeOf(value) !== Object.prototype) throw new Error('The request is not structurally valid.');
  return value;
}

function safeError(error) {
  return String(error && error.message ? error.message : error || 'The operation failed.').replace(/[\r\n\0]/g, ' ').slice(0, 2_000);
}

app.whenReady().then(async () => {
  const defaults = await detectLocalDefaults();
  engine = new InstallerEngine({ userDataDir: app.getPath('userData'), defaults, onState: sendState });
  try {
    const { createOpenCodeManager } = require('./opencode-manager');
    openCodeManager = createOpenCodeManager({ userDataDir: app.getPath('userData') });
  } catch {
    openCodeManager = null;
  }
  registerIpc();
  createWindow();
});

app.on('window-all-closed', () => app.quit());
app.on('activate', () => { if (BrowserWindow.getAllWindows().length === 0) createWindow(); });
