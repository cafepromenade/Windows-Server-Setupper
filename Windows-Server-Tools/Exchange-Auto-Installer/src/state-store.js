'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { SCHEMA_VERSION, STAGES, makeInitialState } = require('./constants');

class StateStore {
  constructor(userDataDir) {
    this.directory = path.join(userDataDir, 'exchange-auto-installer');
    this.statePath = path.join(this.directory, 'installation-state.json');
    this.backupPath = path.join(this.directory, 'installation-state.backup.json');
    this.logPath = path.join(this.directory, 'installation.log');
    this.current = null;
  }

  load(defaults) {
    fs.mkdirSync(this.directory, { recursive: true });
    const loaded = this.readCandidate(this.statePath) || this.readCandidate(this.backupPath);
    this.current = loaded || makeInitialState(defaults);
    this.current.logPath = this.logPath;
    this.current.stages = mergeStages(this.current.stages);
    this.current.revision = Number.isSafeInteger(this.current.revision) ? this.current.revision : 0;
    this.current.updatedAt = new Date().toISOString();
    this.save();
    return this.snapshot();
  }

  readCandidate(candidatePath) {
    try {
      const stat = fs.statSync(candidatePath);
      if (!stat.isFile() || stat.size > 2_000_000) return null;
      const parsed = JSON.parse(fs.readFileSync(candidatePath, 'utf8'));
      if (!parsed || parsed.schemaVersion !== SCHEMA_VERSION || !Array.isArray(parsed.stages)) return null;
      return parsed;
    } catch {
      return null;
    }
  }

  update(mutator) {
    if (!this.current) throw new Error('Installation state is not loaded.');
    mutator(this.current);
    this.current.revision += 1;
    this.current.updatedAt = new Date().toISOString();
    this.save();
    return this.snapshot();
  }

  save() {
    const temporaryPath = `${this.statePath}.${process.pid}.tmp`;
    const serialized = `${JSON.stringify(this.current, null, 2)}\n`;
    fs.mkdirSync(this.directory, { recursive: true });
    fs.writeFileSync(temporaryPath, serialized, { encoding: 'utf8', flag: 'w', mode: 0o600 });
    let previousMoved = false;
    try {
      if (fs.existsSync(this.backupPath)) fs.rmSync(this.backupPath, { force: true });
      if (fs.existsSync(this.statePath)) {
        fs.renameSync(this.statePath, this.backupPath);
        previousMoved = true;
      }
      fs.renameSync(temporaryPath, this.statePath);
    } catch (error) {
      try {
        if (previousMoved && !fs.existsSync(this.statePath) && fs.existsSync(this.backupPath)) {
          fs.renameSync(this.backupPath, this.statePath);
        }
      } catch {
        // The original write error remains the most useful result for the caller.
      }
      throw error;
    } finally {
      try { if (fs.existsSync(temporaryPath)) fs.rmSync(temporaryPath, { force: true }); } catch { /* Best-effort temporary cleanup. */ }
    }
  }

  appendLog(record) {
    fs.mkdirSync(this.directory, { recursive: true });
    fs.appendFileSync(this.logPath, `${JSON.stringify(record)}\n`, { encoding: 'utf8', mode: 0o600 });
  }

  snapshot() {
    return JSON.parse(JSON.stringify(this.current));
  }
}

function mergeStages(savedStages) {
  const saved = new Map((savedStages || []).map((stage) => [stage.id, stage]));
  return STAGES.map((definition) => ({
    id: definition.id,
    title: definition.title,
    description: definition.description,
    status: 'pending',
    attempts: 0,
    startedAt: null,
    finishedAt: null,
    exitCode: null,
    lastError: null,
    reconciliation: null,
    ...(saved.get(definition.id) || {})
  }));
}

module.exports = { StateStore };
