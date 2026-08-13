'use strict';

const fs = require('node:fs');
const path = require('node:path');
const {
  PROFILE_KEYS,
  REBOOT_EXIT_CODES,
  STAGES,
  SUCCESS_EXIT_CODES,
  TRANSIENT_EXIT_CODES,
  WINDOWS_FEATURES
} = require('./constants');
const { inspectExchangeMedia, minimalWindowsEnvironment, powershellPath, runPreflight } = require('./preflight');
const { runProcess } = require('./process-runner');
const { redactObject, redactText } = require('./redaction');
const { StateStore } = require('./state-store');

class InstallerEngine {
  constructor({ userDataDir, defaults, onState }) {
    this.store = new StateStore(userDataDir);
    this.state = this.store.load(defaults);
    this.onState = onState || (() => {});
    this.activeAbort = null;
    this.running = false;
    this.reconcileInterruptedStage();
  }

  getState() {
    return this.store.snapshot();
  }

  async inspectMedia(candidatePath) {
    const media = await inspectExchangeMedia(candidatePath);
    this.update((state) => {
      state.media = media;
      state.preflight.status = 'not-run';
    });
    this.log('media-inspected', media.ok ? 'Exchange media passed signature inspection.' : media.reason, { sha256: media.sha256, size: media.size });
    return media;
  }

  updateProfile(patch) {
    const requested = patch && typeof patch === 'object' ? patch : {};
    const next = {};
    for (const [key, value] of Object.entries(requested)) {
      if (!PROFILE_KEYS.has(key)) continue;
      next[key] = normalizeProfileValue(key, value);
    }
    this.update((state) => {
      state.profile = { ...state.profile, ...next };
      state.preflight.status = 'not-run';
    });
    this.log('profile-updated', 'The reviewed non-secret installation profile changed.', Object.keys(next));
    return this.getState().profile;
  }

  async preflight() {
    if (this.running) throw new Error('An installation stage is already running.');
    this.update((state) => { state.preflight.status = 'running'; state.lastError = null; });
    const result = await runPreflight(this.getState());
    this.update((state) => {
      state.detected = result.detected;
      if (!state.profile.targetDomain && result.detected.domain) state.profile.targetDomain = result.detected.domain;
      state.preflight = { status: result.status, checkedAt: result.checkedAt, checks: mapRendererChecks(result.checks) };
    });
    this.log('preflight-finished', `Preflight ${result.status}.`, result.checks);
    return this.getState().preflight;
  }

  plan() {
    const state = this.getState();
    return STAGES.filter((stage) => stageEnabled(stage.id, state.profile)).map((stage) => ({
      id: stage.id,
      title: stage.title,
      detail: previewStage(stage, state)
    }));
  }

  async start(acknowledgement) {
    if (!acknowledgement || acknowledgement.licenseAccepted !== true) throw new Error('License acknowledgement is required immediately before installation.');
    const state = this.getState();
    if (state.preflight.status !== 'passed') throw new Error('Preflight must pass immediately before installation.');
    if (!state.media || !state.media.ok) throw new Error('Verified Microsoft Exchange media is required.');
    this.update((next) => {
      next.phase = 'installing';
      next.cancelRequested = false;
      next.lastError = null;
      for (const stage of next.stages) {
        if (!stageEnabled(stage.id, next.profile) && stage.status === 'pending') stage.status = 'skipped';
      }
    });
    return this.runRemaining();
  }

  async resume() {
    if (this.running) throw new Error('An installation stage is already running.');
    const state = this.getState();
    if (!state.media || !state.media.ok) throw new Error('Inspect the Exchange media again before resuming.');
    if (state.preflight.status !== 'passed') throw new Error('Run preflight again before resuming.');
    this.update((next) => { next.phase = 'installing'; next.cancelRequested = false; next.lastError = null; });
    return this.runRemaining();
  }

  async retryStage(stageId) {
    if (!STAGES.some((stage) => stage.id === stageId)) throw new Error('The requested stage is not in the installation plan.');
    this.update((state) => {
      const stage = state.stages.find((entry) => entry.id === stageId);
      if (!['failed', 'uncertain', 'cancelled'].includes(stage.status)) throw new Error('Only a stopped stage can be retried.');
      stage.status = 'pending';
      stage.lastError = null;
      stage.reconciliation = null;
      state.phase = 'installing';
      state.lastError = null;
      state.cancelRequested = false;
    });
    return this.runRemaining(stageId);
  }

  requestCancel() {
    if (!this.running) return { accepted: false, message: 'No installation stage is running.' };
    this.update((state) => { state.cancelRequested = true; });
    this.log('cancel-requested', 'Cancellation will occur at the next safe stage boundary.');
    return { accepted: true, message: 'Cancellation is waiting for a safe stage boundary.' };
  }

  async runRemaining(firstStageId = null) {
    if (this.running) throw new Error('An installation stage is already running.');
    this.running = true;
    try {
      const definitions = STAGES.filter((stage) => stageEnabled(stage.id, this.getState().profile));
      let reachedFirst = !firstStageId;
      for (const definition of definitions) {
        if (firstStageId && definition.id === firstStageId) reachedFirst = true;
        if (!reachedFirst) continue;
        const stageState = this.getState().stages.find((entry) => entry.id === definition.id);
        if (['completed', 'skipped'].includes(stageState.status)) continue;
        if (this.getState().cancelRequested) {
          this.update((state) => { state.phase = 'cancelled'; state.currentStageId = null; });
          this.log('installation-cancelled', 'Installation stopped at a safe stage boundary.');
          return this.getState();
        }
        const outcome = await this.runStage(definition);
        if (!outcome.continue) return this.getState();
      }
      this.update((state) => { state.phase = 'completed'; state.currentStageId = null; state.lastError = null; });
      this.log('installation-completed', 'Every selected Exchange installation stage completed.');
      return this.getState();
    } finally {
      this.running = false;
      this.activeAbort = null;
    }
  }

  async runStage(definition) {
    const maximumAttempts = Math.min(4, Math.max(1, Number(this.getState().profile.maxTransientRetries || 0) + 1));
    for (;;) {
      this.update((state) => {
        const stage = state.stages.find((entry) => entry.id === definition.id);
        stage.status = 'running';
        stage.attempts += 1;
        stage.startedAt = new Date().toISOString();
        stage.finishedAt = null;
        stage.lastError = null;
        stage.reconciliation = null;
        state.currentStageId = definition.id;
        state.phase = 'installing';
      });
      this.log('stage-started', definition.title, { stageId: definition.id });
      this.activeAbort = new AbortController();
      const result = await this.executeStage(definition, this.activeAbort.signal);
      const attempts = this.getState().stages.find((entry) => entry.id === definition.id).attempts;

      if (result.ok) {
        this.update((state) => {
          const stage = state.stages.find((entry) => entry.id === definition.id);
          stage.status = 'completed';
          stage.finishedAt = new Date().toISOString();
          stage.exitCode = result.exitCode;
          stage.reconciliation = result.reconciliation || 'Verified by the stage-specific completion probe.';
          state.rebootRequired ||= Boolean(result.rebootRequired);
          state.currentStageId = null;
        });
        this.log('stage-completed', definition.title, { stageId: definition.id, exitCode: result.exitCode, rebootRequired: result.rebootRequired });
        return { continue: true };
      }

      if (result.transient && attempts < maximumAttempts) {
        this.log('stage-retrying', `${definition.title} returned a transient result; retrying within the configured bound.`, { stageId: definition.id, attempt: attempts });
        await delay(Math.min(60_000, 5_000 * attempts));
        continue;
      }

      this.update((state) => {
        const stage = state.stages.find((entry) => entry.id === definition.id);
        stage.status = result.uncertain ? 'uncertain' : result.cancelled ? 'cancelled' : 'failed';
        stage.finishedAt = new Date().toISOString();
        stage.exitCode = result.exitCode;
        stage.lastError = result.message;
        stage.reconciliation = result.reconciliation || null;
        state.phase = result.uncertain ? 'uncertain' : 'failed';
        state.lastError = { stageId: definition.id, message: result.message, uncertain: Boolean(result.uncertain) };
        state.currentStageId = null;
      });
      this.log('stage-stopped', result.message, { stageId: definition.id, exitCode: result.exitCode, uncertain: result.uncertain });
      return { continue: false };
    }
  }

  async executeStage(definition, signal) {
    const state = this.getState();
    if (definition.kind === 'postflight') return this.runPostflight(signal);
    if (definition.kind === 'powershell') return this.runWindowsFeatures(definition, signal);

    const media = await inspectExchangeMedia(state.media.path);
    if (!media.ok || media.sha256 !== state.media.sha256) {
      return { ok: false, uncertain: false, exitCode: null, message: 'Exchange media changed or could not be re-verified before execution.' };
    }
    const args = buildExchangeArguments(definition, state.profile);
    const result = await runProcess({
      file: media.path,
      args,
      cwd: media.directory,
      env: minimalWindowsEnvironment(),
      timeoutMs: definition.timeoutMs,
      signal,
      privatePaths: [media.path, media.directory],
      onLine: (source, line) => this.log('process-output', line, { stageId: definition.id, source })
    });
    return classifyResult(result, definition.id);
  }

  async runWindowsFeatures(definition, signal) {
    const featureLiteral = WINDOWS_FEATURES.map((name) => `'${name.replace(/'/g, "''")}'`).join(',');
    const script = `$features=@(${featureLiteral}); $result=Install-WindowsFeature -Name $features -IncludeManagementTools -ErrorAction Stop; $result | Select-Object Success,RestartNeeded,ExitCode | ConvertTo-Json -Compress`;
    const result = await runProcess({
      file: powershellPath(),
      args: ['-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'RemoteSigned', '-Command', script],
      env: minimalWindowsEnvironment(),
      timeoutMs: definition.timeoutMs,
      signal,
      onLine: (source, line) => this.log('process-output', line, { stageId: definition.id, source })
    });
    return classifyResult(result, definition.id);
  }

  async runPostflight(signal) {
    const script = "$setup=Get-ItemProperty 'HKLM:\\SOFTWARE\\Microsoft\\ExchangeServer\\v15\\Setup' -ErrorAction Stop; $service=Get-Service MSExchangeServiceHost -ErrorAction Stop; [pscustomobject]@{InstallPath=$setup.MsiInstallPath; ServiceStatus=[string]$service.Status} | ConvertTo-Json -Compress";
    const result = await runProcess({
      file: powershellPath(),
      args: ['-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'RemoteSigned', '-Command', script],
      env: minimalWindowsEnvironment(),
      timeoutMs: 600_000,
      signal,
      onLine: (source, line) => this.log('process-output', line, { stageId: 'postflight', source })
    });
    const classified = classifyResult(result, 'postflight');
    if (classified.ok) classified.reconciliation = 'The Exchange setup registry key and Exchange service were found locally.';
    return classified;
  }

  reconcileInterruptedStage() {
    const interrupted = this.state.stages.find((stage) => stage.status === 'running');
    if (!interrupted) return;
    this.update((state) => {
      const stage = state.stages.find((entry) => entry.id === interrupted.id);
      stage.status = 'uncertain';
      stage.finishedAt = new Date().toISOString();
      stage.lastError = 'The app stopped while this stage was running. Its outcome must be reconciled before retrying.';
      stage.reconciliation = 'Durable state proves the stage started but does not prove whether Setup completed.';
      state.phase = 'uncertain';
      state.currentStageId = null;
      state.lastError = { stageId: stage.id, message: stage.lastError, uncertain: true };
    });
  }

  update(mutator) {
    this.state = this.store.update(mutator);
    this.onState(this.getPublicState());
    return this.state;
  }

  getPublicState() {
    const state = this.getState();
    state.profile.mediaPath = state.media?.path || '';
    state.plan = this.plan();
    state.status = state.phase;
    state.install = { status: state.phase, stages: state.stages, message: state.lastError?.message || null };
    state.canInstall = state.preflight.status === 'passed';
    state.canResume = ['failed', 'uncertain', 'cancelled'].includes(state.phase);
    state.install.canResume = state.canResume;
    state.install.durableStateAvailable = state.stages.some((stage) => stage.status !== 'pending');
    state.logs = readLogTail(state.logPath);
    return state;
  }

  log(event, message, details = null) {
    const record = redactObject({ timestamp: new Date().toISOString(), event, message, details }, this.state.media ? [this.state.media.path, this.state.media.directory] : []);
    try { this.store.appendLog(record); } catch { /* Installation state remains authoritative when optional log append fails. */ }
    this.onState(this.getPublicState());
  }
}

function buildExchangeArguments(definition, profile) {
  const args = [...(definition.setupArguments || [])];
  args.push(profile.disableTelemetry || profile.diagnosticData === 'OFF' ? '/IAcceptExchangeServerLicenseTerms_DiagnosticDataOFF' : '/IAcceptExchangeServerLicenseTerms_DiagnosticDataON');
  if (definition.id === 'prepare-ad') args.push(`/OrganizationName:${profile.organizationName}`);
  if (definition.id === 'install-mailbox' && profile.installWindowsComponents) args.push('/InstallWindowsComponents');
  if (definition.id === 'install-mailbox' && profile.installPath) args.push(`/TargetDir:${profile.installPath}`);
  if (definition.id === 'install-mailbox' && profile.databaseName) args.push(`/MdbName:${profile.databaseName}`);
  if (definition.id === 'install-mailbox' && profile.databasePath) args.push(`/DbFilePath:${profile.databasePath}`);
  if (definition.id === 'install-mailbox' && profile.logPath) args.push(`/LogFolderPath:${profile.logPath}`);
  return args;
}

function classifyResult(result, stageId) {
  const exitCode = Number.isInteger(result.exitCode) ? result.exitCode : null;
  if (SUCCESS_EXIT_CODES.has(exitCode) && !result.timedOut && !result.aborted) {
    return { ok: true, exitCode, rebootRequired: REBOOT_EXIT_CODES.has(exitCode), reconciliation: `Process exited with documented success code ${exitCode}.` };
  }
  const uncertain = Boolean(result.timedOut || result.aborted || result.spawnError || exitCode === null);
  return {
    ok: false,
    exitCode,
    uncertain,
    cancelled: Boolean(result.aborted),
    transient: TRANSIENT_EXIT_CODES.has(exitCode),
    message: redactText(result.spawnError || result.stderrTail || result.stdoutTail || `${stageId} stopped with exit code ${exitCode ?? 'unknown'}.`),
    reconciliation: uncertain ? 'The process did not provide a conclusive exit result; inspect Exchange setup logs and run preflight before retrying.' : null
  };
}

function mapRendererChecks(checks) {
  const map = Object.fromEntries(checks.map((entry) => [entry.id, { status: entry.ok ? 'passed' : 'failed', message: entry.detail }]));
  return {
    elevation: map.elevation,
    media: map.media,
    digest: map.media && { status: map.media.status, message: map.media.status === 'passed' ? 'Authenticode signature and SHA-256 digest were recorded.' : map.media.message },
    prerequisites: map.windows,
    activeDirectory: map.domain,
    restart: map.reboot,
    organization: map.organization,
    targetDomain: map['target-domain']
  };
}

function normalizeProfileValue(key, value) {
  if (['installPrerequisites', 'prepareSchema', 'prepareActiveDirectory', 'prepareDomains', 'disableTelemetry', 'resumeAfterRestart', 'installWindowsComponents'].includes(key)) return Boolean(value);
  if (key === 'maxTransientRetries') return Math.min(3, Math.max(0, Number(value) || 0));
  const text = String(value ?? '').trim();
  if (text.length > 512 || /[\r\n\0]/.test(text)) throw new Error(`${key} is not structurally valid.`);
  if (key === 'organizationName' && !/^[a-z0-9][a-z0-9 ._-]{0,63}$/i.test(text)) throw new Error('The organization name is not valid.');
  if (key === 'role' && !['Mailbox', 'ManagementTools', 'EdgeTransport'].includes(text)) throw new Error('The selected role is not supported by this release.');
  if (['installPath', 'databasePath', 'logPath'].includes(key) && !path.win32.isAbsolute(text)) throw new Error(`${key} must be an absolute Windows path.`);
  return text;
}

function stageEnabled(stageId, profile) {
  if (stageId === 'windows-features') return profile.installPrerequisites !== false;
  if (stageId === 'prepare-schema') return profile.prepareSchema !== false;
  if (stageId === 'prepare-ad') return profile.prepareActiveDirectory !== false;
  if (stageId === 'prepare-domains') return profile.prepareDomains !== false;
  return true;
}

function previewStage(stage, state) {
  if (stage.kind === 'powershell') return `Install ${WINDOWS_FEATURES.length} fixed Windows Server feature identifiers.`;
  if (stage.kind === 'postflight') return 'Read the Exchange setup registry key and Microsoft Exchange Service Host status.';
  return `Setup.exe ${buildExchangeArguments(stage, state.profile).join(' ')}`;
}

function readLogTail(logPath) {
  try {
    const stat = fs.statSync(logPath);
    if (!stat.isFile()) return [];
    const length = Math.min(64 * 1024, stat.size);
    const fd = fs.openSync(logPath, 'r');
    const buffer = Buffer.alloc(length);
    fs.readSync(fd, buffer, 0, length, stat.size - length);
    fs.closeSync(fd);
    return buffer.toString('utf8').split(/\r?\n/).filter(Boolean).slice(-200).map((line) => {
      try { return JSON.parse(line); } catch { return { message: redactText(line) }; }
    });
  } catch { return []; }
}

function delay(ms) { return new Promise((resolve) => setTimeout(resolve, ms)); }

module.exports = { InstallerEngine };
