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
    this.lockPath = path.join(this.directory, 'installation-state.lock');
    this.lockFd = null;
  }

  load(defaults) {
    fs.mkdirSync(this.directory, { recursive: true });
    this.acquireLease();
    const primary = this.readCandidate(this.statePath);
    const backup = this.readCandidate(this.backupPath);
    const anyExisting = primary.status !== 'missing' || backup.status !== 'missing';
    const loaded = primary.status === 'valid' ? primary.value : backup.status === 'valid' ? backup.value : null;
    if (!loaded && anyExisting) {
      this.preserveInvalidState(primary, backup);
      throw new Error('Installation state is corrupt or unsupported. Evidence was preserved; use an explicit repair or reset action before continuing.');
    }
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
      if (!stat.isFile() || stat.size > 2_000_000) return { status: 'invalid', reason: 'not a bounded regular file' };
      const parsed = JSON.parse(fs.readFileSync(candidatePath, 'utf8'));
      if (!parsed || parsed.schemaVersion !== SCHEMA_VERSION || !Array.isArray(parsed.stages) || !Number.isSafeInteger(parsed.revision)) return { status: 'invalid', reason: 'schema or revision invalid' };
      return { status: 'valid', value: parsed };
    } catch (error) {
      return { status: fs.existsSync(candidatePath) ? 'invalid' : 'missing', reason: error.message };
    }
  }

  update(mutator) {
    if (!this.current) throw new Error('Installation state is not loaded.');
    const disk = this.readCandidate(this.statePath);
    if (disk.status === 'valid' && disk.value.revision !== this.current.revision) throw new Error('Installation state changed in another runtime; reload before updating.');
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

  acquireLease() {
    try {
      this.lockFd = fs.openSync(this.lockPath, 'wx', 0o600);
      fs.writeFileSync(this.lockFd, `${JSON.stringify({ pid: process.pid, startedAt: new Date().toISOString() })}\n`);
    } catch {
      throw new Error('Another Exchange installer runtime owns the protected installation state. Close it before continuing.');
    }
  }

  releaseLease() {
    try { if (this.lockFd !== null) fs.closeSync(this.lockFd); } catch { /* Process shutdown keeps the original state authoritative. */ }
    this.lockFd = null;
    try { fs.rmSync(this.lockPath, { force: true }); } catch { /* A stale lock is safer than concurrent privileged mutation. */ }
  }

  preserveInvalidState(primary, backup) {
    const quarantine = path.join(this.directory, `invalid-state-${new Date().toISOString().replace(/[:.]/g, '-')}`);
    fs.mkdirSync(quarantine, { recursive: false, mode: 0o700 });
    if (primary.status !== 'missing') fs.copyFileSync(this.statePath, path.join(quarantine, 'installation-state.json'));
    if (backup.status !== 'missing') fs.copyFileSync(this.backupPath, path.join(quarantine, 'installation-state.backup.json'));
    fs.writeFileSync(path.join(quarantine, 'reason.json'), `${JSON.stringify({ primary, backup }, (_key, value) => _key === 'value' ? '[OMITTED]' : value, 2)}\n`, { mode: 0o600 });
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
