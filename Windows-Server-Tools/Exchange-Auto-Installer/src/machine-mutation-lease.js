'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { spawn } = require('node:child_process');

const LEASE_RELATIVE_PATH = path.join('Coordination', 'server-mutation.lease');

async function acquireMachineMutationLease(machineRoot) {
  const leasePath = path.join(machineRoot, LEASE_RELATIVE_PATH);
  fs.mkdirSync(path.dirname(leasePath), { recursive: true, mode: 0o700 });
  if (process.platform !== 'win32') return { path: leasePath, release: async () => {} };
  const powershell = path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');
  const script = [
    "$ErrorActionPreference='Stop'",
    '$path=$env:EXCHANGE_MACHINE_MUTATION_LEASE',
    '$stream=[System.IO.File]::Open($path,[System.IO.FileMode]::OpenOrCreate,[System.IO.FileAccess]::ReadWrite,[System.IO.FileShare]::None)',
    "[Console]::Out.WriteLine('READY')",
    '[Console]::Out.Flush()',
    '[Console]::In.ReadToEnd() | Out-Null',
    '$stream.Dispose()'
  ].join('; ');
  const child = spawn(powershell, ['-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'RemoteSigned', '-Command', script], {
    env: { SystemRoot: process.env.SystemRoot, WINDIR: process.env.WINDIR, COMSPEC: process.env.COMSPEC, PATH: process.env.PATH, EXCHANGE_MACHINE_MUTATION_LEASE: leasePath },
    windowsHide: true,
    shell: false,
    stdio: ['pipe', 'pipe', 'pipe']
  });
  const ready = await waitForReady(child, 5_000);
  if (!ready) {
    try { child.kill(); } catch { /* Refused acquisition already owns no lease. */ }
    throw new Error('Another Windows Server Tools runtime owns the protected machine-wide server mutation lease. No Exchange stage was started or queued.');
  }
  let released = false;
  return {
    path: leasePath,
    release: async () => {
      if (released) return;
      released = true;
      child.stdin.end();
      await new Promise((resolve) => { child.once('exit', resolve); setTimeout(() => { child.kill(); resolve(); }, 5_000).unref(); });
    }
  };
}

function waitForReady(child, timeoutMs) {
  return new Promise((resolve) => {
    let settled = false;
    let output = '';
    const finish = (value) => { if (settled) return; settled = true; clearTimeout(timeout); resolve(value); };
    child.stdout.on('data', (chunk) => { output += chunk.toString('utf8'); if (output.split(/\r?\n/).includes('READY')) finish(true); });
    child.once('error', () => finish(false));
    child.once('exit', () => finish(false));
    const timeout = setTimeout(() => finish(false), timeoutMs);
  });
}

module.exports = { LEASE_RELATIVE_PATH, acquireMachineMutationLease };
