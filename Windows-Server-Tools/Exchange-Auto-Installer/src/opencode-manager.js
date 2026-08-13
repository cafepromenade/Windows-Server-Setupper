'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const https = require('node:https');
const os = require('node:os');
const path = require('node:path');
const { randomUUID } = require('node:crypto');
const { runProcess, terminateProcessTree } = require('./process-runner');
const { redactObject, redactText } = require('./redaction');

const OPENCODE_VERSION = '1.18.18';
const OPENCODE_ARCHIVE_SHA256 = 'c6d265376fdb93164013671b0cf402410184f73c34fc15d82d40a16a745b15f4';
const OPENCODE_ARCHIVE_BYTES = 60_504_740;
const OPENCODE_ARCHIVE_URL = `https://github.com/anomalyco/opencode/releases/download/v${OPENCODE_VERSION}/opencode-windows-x64.zip`;
const MAX_DOWNLOAD_BYTES = 200 * 1024 * 1024;
const YOLO_ACKNOWLEDGEMENT = 'ENABLE BOUNDED YOLO';
const ALLOWED_REPAIR_ACTIONS = Object.freeze({
  reinspect_media: 'Reinspect the already selected Exchange media and compare its digest.',
  refresh_preflight: 'Run the app preflight again against current local state.',
  retry_failed_stage: 'Retry only the current stopped Exchange stage from durable state.',
  resume_installation: 'Resume the reviewed plan from the first incomplete durable stage.',
  export_redacted_logs: 'Offer the native redacted-log export picker.'
});

function createOpenCodeManager({ userDataDir }) {
  const root = path.join(userDataDir, 'exchange-auto-installer', 'opencode');
  const managed = path.join(root, 'managed', OPENCODE_VERSION);
  const executable = path.join(managed, 'opencode.exe');
  const manifestPath = path.join(managed, 'manifest.json');
  const preferencePath = path.join(root, 'preferences.json');
  let activeController = null;
  let activePid = null;
  let activePlan = null;

  async function status() {
    const preference = readPreference(preferencePath);
    const manifest = readJson(manifestPath, 64 * 1024);
    let compatible = false;
    let message = `Managed OpenCode ${OPENCODE_VERSION} is not installed.`;
    if (manifest && manifest.version === OPENCODE_VERSION && fs.existsSync(executable)) {
      const digest = await sha256File(executable);
      compatible = digest === manifest.executableSha256 && manifest.archiveSha256 === OPENCODE_ARCHIVE_SHA256;
      message = compatible ? `Managed OpenCode ${OPENCODE_VERSION} is installed and matches its recorded digest.` : 'The managed OpenCode installation is corrupt or incompatible; use Install or repair.';
    }
    return {
      ok: compatible,
      compatible,
      status: compatible ? 'ready' : 'missing-or-invalid',
      message,
      version: OPENCODE_VERSION,
      source: OPENCODE_ARCHIVE_URL,
      archiveSha256: OPENCODE_ARCHIVE_SHA256,
      yoloMode: preference.yoloMode,
      activeRepair: Boolean(activeController)
    };
  }

  async function installOrRepair(onProgress = () => {}) {
    if (process.platform !== 'win32' || process.arch !== 'x64') throw new Error('The pinned OpenCode package supports Windows x64 only.');
    fs.mkdirSync(root, { recursive: true });
    const staging = path.join(root, `staging-${randomUUID()}`);
    const archive = path.join(staging, 'opencode-windows-x64.zip');
    const extracted = path.join(staging, 'extracted');
    try {
      fs.mkdirSync(extracted, { recursive: true });
      onProgress({ phase: 'download', message: `Downloading pinned OpenCode ${OPENCODE_VERSION} from the official release.` });
      await downloadPinnedArchive(OPENCODE_ARCHIVE_URL, archive, MAX_DOWNLOAD_BYTES);
      const archiveSha256 = await sha256File(archive);
      if (archiveSha256 !== OPENCODE_ARCHIVE_SHA256) throw new Error('The OpenCode archive digest did not match the pinned official release digest.');
      onProgress({ phase: 'extract', message: 'Digest matched; extracting into private application data.' });
      const tar = path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'tar.exe');
      const extraction = await runProcess({
        file: tar,
        args: ['-xf', archive, '-C', extracted],
        cwd: staging,
        env: minimalEnvironment(),
        timeoutMs: 120_000,
        privatePaths: [root]
      });
      if (extraction.exitCode !== 0 || extraction.timedOut) throw new Error('The verified OpenCode archive could not be extracted.');
      const stagedExecutable = path.join(extracted, 'opencode.exe');
      if (!fs.existsSync(stagedExecutable)) throw new Error('The verified archive did not contain opencode.exe.');
      const versionResult = await runProcess({ file: stagedExecutable, args: ['--version'], cwd: extracted, env: minimalEnvironment(), timeoutMs: 30_000, privatePaths: [root] });
      if (versionResult.exitCode !== 0 || !versionResult.stdoutTail.includes(OPENCODE_VERSION)) throw new Error(`The extracted OpenCode executable did not report version ${OPENCODE_VERSION}.`);
      const executableSha256 = await sha256File(stagedExecutable);
      fs.writeFileSync(path.join(extracted, 'manifest.json'), `${JSON.stringify({ version: OPENCODE_VERSION, archiveSha256, executableSha256, source: OPENCODE_ARCHIVE_URL }, null, 2)}\n`, { mode: 0o600 });
      fs.mkdirSync(path.dirname(managed), { recursive: true });
      if (fs.existsSync(managed)) fs.rmSync(managed, { recursive: true, force: true });
      fs.renameSync(extracted, managed);
      onProgress({ phase: 'complete', message: `Managed OpenCode ${OPENCODE_VERSION} is ready.` });
      return status();
    } finally {
      try { if (fs.existsSync(staging)) fs.rmSync(staging, { recursive: true, force: true }); } catch { /* A later repair can remove a stale private staging folder. */ }
    }
  }

  function getYoloMode() {
    return readPreference(preferencePath).yoloMode;
  }

  function setYoloMode(request) {
    const enabled = request && request.enabled === true;
    if (enabled && request.acknowledgement !== YOLO_ACKNOWLEDGEMENT) throw new Error(`Type ${YOLO_ACKNOWLEDGEMENT} to enable the bounded mode.`);
    fs.mkdirSync(path.dirname(preferencePath), { recursive: true });
    fs.writeFileSync(preferencePath, `${JSON.stringify({ schemaVersion: 1, yoloMode: enabled, updatedAt: new Date().toISOString() }, null, 2)}\n`, { mode: 0o600 });
    return { enabled, scope: Object.keys(ALLOWED_REPAIR_ACTIONS) };
  }

  async function runRepairAdvisor(request, { installerState, onProgress = () => {} }) {
    if (activeController) throw new Error('An OpenCode repair adviser run is already active.');
    const current = await status();
    if (!current.compatible) throw new Error('Install or repair the pinned OpenCode build before requesting advice.');
    const runId = randomUUID();
    const workspace = path.join(root, 'runs', runId);
    const isolatedHome = path.join(workspace, 'isolated-home');
    fs.mkdirSync(workspace, { recursive: true });
    fs.mkdirSync(isolatedHome, { recursive: true });
    const diagnostic = buildDiagnosticBundle(installerState, request);
    fs.writeFileSync(path.join(workspace, 'diagnostics.json'), `${JSON.stringify(diagnostic, null, 2)}\n`, { mode: 0o600 });
    fs.writeFileSync(path.join(workspace, 'opencode.json'), `${JSON.stringify(restrictedConfig(), null, 2)}\n`, { mode: 0o600 });
    const prompt = fixedRepairPrompt();
    activeController = new AbortController();
    onProgress({ phase: 'running', message: 'OpenCode is analyzing the bounded redacted diagnostic bundle.' });
    try {
      const result = await runProcess({
        file: executable,
        args: ['run', prompt, '--format', 'json', '--agent', 'plan', '--dir', workspace],
        cwd: workspace,
        env: {
          ...minimalEnvironment(),
          HOME: isolatedHome,
          USERPROFILE: isolatedHome,
          APPDATA: path.join(isolatedHome, 'AppData', 'Roaming'),
          LOCALAPPDATA: path.join(isolatedHome, 'AppData', 'Local'),
          XDG_CONFIG_HOME: path.join(isolatedHome, '.config'),
          XDG_DATA_HOME: path.join(isolatedHome, '.local', 'share'),
          XDG_CACHE_HOME: path.join(isolatedHome, '.cache'),
          OPENCODE_CONFIG: path.join(workspace, 'opencode.json'),
          OPENCODE_DISABLE_PROJECT_CONFIG: '1',
          OPENCODE_DISABLE_GLOBAL_CONFIG: '1',
          OPENCODE_DISABLE_PLUGINS: '1'
        },
        timeoutMs: 10 * 60 * 1000,
        signal: activeController.signal,
        privatePaths: [root, workspace, process.env.USERPROFILE],
        onLine: (_source, line) => onProgress({ phase: 'stream', message: redactText(line, [root, workspace, process.env.USERPROFILE]) })
      });
      if (result.exitCode !== 0 || result.timedOut || result.aborted) throw new Error(result.aborted ? 'The repair adviser was stopped.' : result.timedOut ? 'The repair adviser exceeded its ten-minute limit.' : `OpenCode stopped with exit code ${result.exitCode ?? 'unknown'}.`);
      const actionIds = parseActionIds(result.stdoutTail);
      activePlan = { id: randomUUID(), runId, actionIds, createdAt: new Date().toISOString() };
      const response = { planId: activePlan.id, actionIds, actions: actionIds.map((id) => ({ id, description: ALLOWED_REPAIR_ACTIONS[id] })), yoloMode: getYoloMode(), autoApprove: getYoloMode() };
      onProgress({ phase: 'complete', message: actionIds.length ? `OpenCode proposed ${actionIds.length} allowlisted repair action(s).` : 'OpenCode proposed no allowlisted repair action.' });
      return response;
    } finally {
      activeController = null;
      activePid = null;
      try { fs.rmSync(workspace, { recursive: true, force: true }); } catch { /* Redacted run data remains private if cleanup is refused. */ }
    }
  }

  function approveRepairActions(request) {
    if (!activePlan || request.planId !== activePlan.id) throw new Error('The repair plan is missing or no longer current.');
    const requested = Array.isArray(request.actionIds) ? request.actionIds : [];
    if (requested.length > 10 || requested.some((id) => !activePlan.actionIds.includes(id) || !ALLOWED_REPAIR_ACTIONS[id])) throw new Error('The requested repair action is not part of the current allowlisted plan.');
    return { planId: activePlan.id, actionIds: [...new Set(requested)] };
  }

  async function cancelActiveRun() {
    if (!activeController) return { cancelled: false, message: 'No repair adviser run is active.' };
    activeController.abort();
    if (activePid) await terminateProcessTree(activePid);
    return { cancelled: true, message: 'Emergency stop requested.' };
  }

  return { status, installOrRepair, getYoloMode, setYoloMode, runRepairAdvisor, approveRepairActions, cancelActiveRun };
}

function restrictedConfig() {
  return {
    '$schema': 'https://opencode.ai/config.json',
    permission: {
      '*': 'deny', read: 'allow', glob: 'allow', grep: 'allow', list: 'allow', edit: 'deny', bash: 'deny', task: 'deny', external_directory: 'deny', webfetch: 'deny', websearch: 'deny', question: 'deny', skill: 'deny', lsp: 'deny'
    }
  };
}

function fixedRepairPrompt() {
  return `Read diagnostics.json only. Diagnose the stopped Microsoft Exchange installation without using shell, edits, external directories, web access, subagents, credentials, environment data, or private paths. Return one JSON object only: {"actionIds":[...]}. actionIds may contain only: ${Object.keys(ALLOWED_REPAIR_ACTIONS).join(', ')}. Choose the smallest applicable actions. Do not output commands, scripts, URLs, secrets, paths, prose, markdown, or new action names.`;
}

function buildDiagnosticBundle(installerState, request) {
  const state = redactObject(installerState || {}, [process.env.USERPROFILE, process.env.APPDATA, process.env.LOCALAPPDATA]);
  return {
    schemaVersion: 1,
    objective: 'Select the smallest allowlisted recovery actions for the current stopped Exchange installation.',
    failedStageId: /^[a-z0-9-]{1,80}$/i.test(String(request?.failedStageId || '')) ? request.failedStageId : null,
    phase: state.phase,
    preflight: {
      status: state.preflight?.status,
      checks: Object.entries(state.preflight?.checks || {}).slice(0, 20).map(([id, check]) => ({ id, status: check?.status }))
    },
    stages: Array.isArray(state.stages) ? state.stages.slice(0, 20).map((stage) => ({ id: stage.id, status: stage.status, attempts: stage.attempts, exitCode: stage.exitCode, lastError: stage.lastError, reconciliation: stage.reconciliation })) : [],
    lastError: state.lastError ? { stageId: state.lastError.stageId, uncertain: Boolean(state.lastError.uncertain), category: classifyDiagnosticError(state.lastError.message) } : null,
    rebootRequired: Boolean(state.rebootRequired)
  };
}

function classifyDiagnosticError(message) {
  const text = String(message || '').toLowerCase();
  if (text.includes('restart') || text.includes('reboot')) return 'restart-required';
  if (text.includes('media') || text.includes('signature') || text.includes('digest')) return 'media-verification';
  if (text.includes('timeout')) return 'timeout';
  if (text.includes('cancel')) return 'cancelled';
  if (text.includes('exit code')) return 'process-exit';
  return 'unspecified';
}

function parseActionIds(output) {
  const texts = [];
  for (const line of String(output || '').split(/\r?\n/)) {
    try {
      const event = JSON.parse(line);
      if (typeof event.text === 'string') texts.push(event.text);
      if (typeof event.part?.text === 'string') texts.push(event.part.text);
    } catch { /* Non-JSON progress is ignored. */ }
  }
  texts.push(String(output || ''));
  for (const text of texts.reverse()) {
    const match = text.match(/\{[\s\S]*\}/);
    if (!match || match[0].length > 16_384) continue;
    try {
      const parsed = JSON.parse(match[0]);
      if (!Array.isArray(parsed.actionIds)) continue;
      const actionIds = [...new Set(parsed.actionIds.filter((id) => typeof id === 'string' && ALLOWED_REPAIR_ACTIONS[id]))];
      if (actionIds.length <= 10) return actionIds;
    } catch { /* Try the next bounded text candidate. */ }
  }
  throw new Error('OpenCode did not return a valid allowlisted repair plan.');
}

async function downloadPinnedArchive(url, destination, maxBytes, redirects = 0) {
  if (redirects > 5) throw new Error('The official OpenCode download exceeded the redirect limit.');
  const parsed = new URL(url);
  const allowedHosts = new Set(['github.com', 'objects.githubusercontent.com', 'release-assets.githubusercontent.com']);
  if (parsed.protocol !== 'https:' || !allowedHosts.has(parsed.hostname)) throw new Error('The OpenCode download redirected to an unapproved host.');
  await new Promise((resolve, reject) => {
    const request = https.get(parsed, { headers: { 'User-Agent': 'Exchange-Auto-Installer/1.0' }, timeout: 30_000 }, (response) => {
      if ([301, 302, 303, 307, 308].includes(response.statusCode) && response.headers.location) {
        response.resume();
        downloadPinnedArchive(new URL(response.headers.location, parsed).href, destination, maxBytes, redirects + 1).then(resolve, reject);
        return;
      }
      if (response.statusCode !== 200) { response.resume(); reject(new Error(`The official OpenCode download returned HTTP ${response.statusCode}.`)); return; }
      const declared = Number(response.headers['content-length'] || 0);
      if (declared && declared !== OPENCODE_ARCHIVE_BYTES) { response.resume(); reject(new Error('The OpenCode archive size does not match the pinned official release metadata.')); return; }
      if (declared && declared > maxBytes) { response.resume(); reject(new Error('The OpenCode archive exceeds the download bound.')); return; }
      let received = 0;
      const stream = fs.createWriteStream(destination, { flags: 'wx', mode: 0o600 });
      response.on('data', (chunk) => { received += chunk.length; if (received > maxBytes) request.destroy(new Error('The OpenCode archive exceeded the download bound.')); });
      response.pipe(stream);
      stream.once('finish', () => stream.close(() => received === OPENCODE_ARCHIVE_BYTES ? resolve() : reject(new Error('The OpenCode archive byte count does not match the pinned official release metadata.'))));
      stream.once('error', reject);
    });
    request.once('timeout', () => request.destroy(new Error('The official OpenCode download timed out.')));
    request.once('error', reject);
  });
}

function readPreference(filePath) {
  const parsed = readJson(filePath, 16 * 1024);
  return { yoloMode: parsed?.schemaVersion === 1 && parsed.yoloMode === true };
}

function readJson(filePath, maxBytes) {
  try { const stat = fs.statSync(filePath); if (!stat.isFile() || stat.size > maxBytes) return null; return JSON.parse(fs.readFileSync(filePath, 'utf8')); } catch { return null; }
}

function sha256File(filePath) {
  return new Promise((resolve, reject) => { const hash = crypto.createHash('sha256'); const stream = fs.createReadStream(filePath); stream.on('data', (chunk) => hash.update(chunk)); stream.once('error', reject); stream.once('end', () => resolve(hash.digest('hex'))); });
}

function minimalEnvironment() {
  const systemRoot = process.env.SystemRoot || 'C:\\Windows';
  return { SystemRoot: systemRoot, WINDIR: systemRoot, COMSPEC: process.env.COMSPEC || path.join(systemRoot, 'System32', 'cmd.exe'), TEMP: process.env.TEMP || os.tmpdir(), TMP: process.env.TMP || os.tmpdir(), PATH: `${path.join(systemRoot, 'System32')};${path.join(systemRoot, 'System32', 'WindowsPowerShell', 'v1.0')}` };
}

module.exports = { ALLOWED_REPAIR_ACTIONS, OPENCODE_ARCHIVE_BYTES, OPENCODE_ARCHIVE_SHA256, OPENCODE_ARCHIVE_URL, OPENCODE_VERSION, YOLO_ACKNOWLEDGEMENT, createOpenCodeManager };
