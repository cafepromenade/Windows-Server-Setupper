'use strict';

const { contextBridge, ipcRenderer } = require('electron');

const ALLOWED_EVENTS = new Set(['state']);

contextBridge.exposeInMainWorld('exchangeInstaller', Object.freeze({
  getState: () => ipcRenderer.invoke('exchange:get-state'),
  chooseMedia: () => ipcRenderer.invoke('exchange:choose-media'),
  inspectMedia: (candidatePath) => ipcRenderer.invoke('exchange:inspect-media', candidatePath),
  updateProfile: (patch) => ipcRenderer.invoke('exchange:update-profile', patch),
  runPreflight: () => ipcRenderer.invoke('exchange:run-preflight'),
  startInstall: (acknowledgement) => ipcRenderer.invoke('exchange:start-install', acknowledgement),
  retryStage: (stageId) => ipcRenderer.invoke('exchange:retry-stage', stageId),
  resumeInstall: () => ipcRenderer.invoke('exchange:resume-install'),
  requestCancel: () => ipcRenderer.invoke('exchange:request-cancel'),
  exportLogs: () => ipcRenderer.invoke('exchange:export-logs'),
  revealLogs: () => ipcRenderer.invoke('exchange:reveal-logs'),
  getOpenCodeStatus: () => ipcRenderer.invoke('exchange:opencode-status'),
  installOrRepairOpenCode: () => ipcRenderer.invoke('exchange:opencode-install-or-repair'),
  setYoloMode: (request) => ipcRenderer.invoke('exchange:opencode-set-yolo', request),
  runRepairAdvisor: (request) => ipcRenderer.invoke('exchange:opencode-run-repair', request),
  applyRepairPlan: (request) => ipcRenderer.invoke('exchange:opencode-apply-repair', request),
  cancelRepairAdvisor: () => ipcRenderer.invoke('exchange:opencode-cancel-repair'),
  subscribe: (listener) => {
    if (typeof listener !== 'function') throw new TypeError('A state listener is required.');
    const channel = 'exchange:state';
    const eventName = 'state';
    if (!ALLOWED_EVENTS.has(eventName)) throw new Error('The requested event is not allowed.');
    const wrapped = (_event, state) => listener(state);
    ipcRenderer.on(channel, wrapped);
    return () => ipcRenderer.removeListener(channel, wrapped);
  }
}));
