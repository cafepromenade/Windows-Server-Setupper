'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { runProcess } = require('./process-runner');

const TRUSTED_MICROSOFT_SUBJECT = /CN=Microsoft (Corporation|Windows)/i;

async function detectLocalDefaults() {
  const defaults = {
    computerName: os.hostname(),
    platform: process.platform,
    release: os.release(),
    domain: '',
    domainRole: null,
    isAdministrator: false,
    pendingReboot: null
  };
  if (process.platform !== 'win32') return defaults;

  const script = [
    "$cs = Get-CimInstance Win32_ComputerSystem",
    "$pending = (Test-Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Component Based Servicing\\RebootPending') -or (Test-Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\WindowsUpdate\\Auto Update\\RebootRequired')",
    "$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)",
    "[pscustomobject]@{ Domain=$cs.Domain; DomainRole=[int]$cs.DomainRole; IsAdministrator=$admin; PendingReboot=$pending } | ConvertTo-Json -Compress"
  ].join('; ');
  const result = await runProcess({
    file: powershellPath(),
    args: ['-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'RemoteSigned', '-Command', script],
    timeoutMs: 30_000,
    env: minimalWindowsEnvironment()
  });
  if (result.exitCode === 0) {
    try {
      const parsed = JSON.parse(result.stdoutTail.trim().split(/\r?\n/).at(-1));
      defaults.domain = String(parsed.Domain || '').toLowerCase();
      defaults.domainRole = Number(parsed.DomainRole);
      defaults.isAdministrator = Boolean(parsed.IsAdministrator);
      defaults.pendingReboot = Boolean(parsed.PendingReboot);
    } catch {
      // The caller renders the missing evidence instead of guessing.
    }
  }
  return defaults;
}

async function inspectExchangeMedia(candidatePath) {
  const resolved = path.resolve(String(candidatePath || ''));
  const stat = safeStat(resolved);
  let setupPath = resolved;
  if (stat && stat.isDirectory()) setupPath = path.join(resolved, 'Setup.exe');
  const setupStat = safeStat(setupPath);
  if (!setupStat || !setupStat.isFile() || setupStat.size < 1_000_000) {
    return { ok: false, path: setupPath, reason: 'Setup.exe was not found or is unexpectedly small.' };
  }

  const digest = await sha256File(setupPath);
  const signature = await inspectSignature(setupPath);
  return {
    ok: signature.valid,
    path: setupPath,
    directory: path.dirname(setupPath),
    size: setupStat.size,
    sha256: digest,
    signature,
    reason: signature.valid ? null : signature.reason
  };
}

async function runPreflight(state) {
  const checks = [];
  const detected = await detectLocalDefaults();
  checks.push(check('windows', process.platform === 'win32', 'Microsoft Windows', process.platform === 'win32' ? os.release() : 'This installer runs only on Windows.'));
  checks.push(check('elevation', detected.isAdministrator, 'Administrator privileges', detected.isAdministrator ? 'Running elevated.' : 'Restart the app as administrator.'));
  checks.push(check('domain', Boolean(detected.domain && detected.domainRole >= 1), 'Active Directory membership', detected.domain ? `${detected.domain} (role ${detected.domainRole})` : 'No proven Active Directory domain was found.'));
  checks.push(check('reboot', detected.pendingReboot === false, 'Pending restart', detected.pendingReboot === false ? 'No pending restart was detected.' : 'Restart Windows before installing Exchange.'));
  checks.push(check('media', Boolean(state.media && state.media.ok), 'Exchange installation media', state.media && state.media.ok ? `${state.media.path} (${state.media.sha256})` : 'Choose valid Microsoft-signed Exchange media.'));
  checks.push(check('organization', /^[a-z0-9][a-z0-9 ._-]{0,63}$/i.test(state.profile.organizationName), 'Organization name', state.profile.organizationName || 'Enter an organization name.'));
  checks.push(check('role', state.profile.role === 'Mailbox', 'Exchange role', state.profile.role === 'Mailbox' ? 'Mailbox role selected.' : 'This release automates the Mailbox role only.'));
  checks.push(check('target-domain', state.profile.targetDomain === detected.domain && Boolean(detected.domain), 'Target domain', state.profile.targetDomain || 'No domain selected.'));

  return {
    status: checks.every((entry) => entry.ok) ? 'passed' : 'failed',
    checkedAt: new Date().toISOString(),
    detected,
    checks
  };
}

function check(id, ok, title, detail) {
  return { id, ok: Boolean(ok), title, detail: String(detail) };
}

async function inspectSignature(filePath) {
  if (process.platform !== 'win32') return { valid: false, status: 'Unsupported', subject: '', reason: 'Authenticode verification requires Windows.' };
  const escaped = filePath.replace(/'/g, "''");
  const script = `$sig=Get-AuthenticodeSignature -LiteralPath '${escaped}'; [pscustomobject]@{Status=[string]$sig.Status; Subject=[string]$sig.SignerCertificate.Subject} | ConvertTo-Json -Compress`;
  const result = await runProcess({
    file: powershellPath(),
    args: ['-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'RemoteSigned', '-Command', script],
    timeoutMs: 30_000,
    env: minimalWindowsEnvironment(),
    privatePaths: [filePath]
  });
  try {
    const parsed = JSON.parse(result.stdoutTail.trim().split(/\r?\n/).at(-1));
    const valid = result.exitCode === 0 && parsed.Status === 'Valid' && TRUSTED_MICROSOFT_SUBJECT.test(parsed.Subject || '');
    return { valid, status: parsed.Status, subject: parsed.Subject, reason: valid ? null : `Authenticode status ${parsed.Status || 'unknown'} was not a trusted Microsoft signature.` };
  } catch {
    return { valid: false, status: 'Unknown', subject: '', reason: 'The media signature could not be verified.' };
  }
}

function sha256File(filePath) {
  return new Promise((resolve, reject) => {
    const hash = crypto.createHash('sha256');
    const stream = fs.createReadStream(filePath);
    stream.on('data', (chunk) => hash.update(chunk));
    stream.once('error', reject);
    stream.once('end', () => resolve(hash.digest('hex')));
  });
}

function safeStat(candidate) {
  try { return fs.statSync(candidate); } catch { return null; }
}

function powershellPath() {
  return path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');
}

function minimalWindowsEnvironment() {
  return {
    SystemRoot: process.env.SystemRoot || 'C:\\Windows',
    WINDIR: process.env.WINDIR || process.env.SystemRoot || 'C:\\Windows',
    COMSPEC: process.env.COMSPEC || 'C:\\Windows\\System32\\cmd.exe',
    TEMP: process.env.TEMP,
    TMP: process.env.TMP,
    PATH: `${process.env.SystemRoot || 'C:\\Windows'}\\System32;${process.env.SystemRoot || 'C:\\Windows'}\\System32\\WindowsPowerShell\\v1.0`
  };
}

module.exports = { detectLocalDefaults, inspectExchangeMedia, minimalWindowsEnvironment, powershellPath, runPreflight };
