'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

function ensureSecureDataRoot(fallbackUserData) {
  if (process.platform !== 'win32') return fallbackUserData;
  const programData = process.env.ProgramData || 'C:\\ProgramData';
  const target = path.join(programData, 'Windows Server Tools');
  rejectRedirectedAncestors(target);
  fs.mkdirSync(target, { recursive: true, mode: 0o700 });
  rejectRedirectedAncestors(target);
  execFileSync(path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'icacls.exe'), [target, '/inheritance:r', '/grant:r', '*S-1-5-18:(OI)(CI)F', '*S-1-5-32-544:(OI)(CI)F'], { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  const probe = path.join(target, `.acl-probe-${process.pid}`);
  fs.writeFileSync(probe, 'protected\n', { flag: 'wx', mode: 0o600 });
  fs.rmSync(probe, { force: true });
  return target;
}

function rejectRedirectedAncestors(target) {
  const parsed = path.parse(path.resolve(target));
  let current = parsed.root;
  for (const segment of path.resolve(target).slice(parsed.root.length).split(path.sep).filter(Boolean)) {
    current = path.join(current, segment);
    if (!fs.existsSync(current)) continue;
    const stat = fs.lstatSync(current);
    if (stat.isSymbolicLink()) throw new Error(`Protected application data path contains a link or junction: ${current}`);
  }
}

module.exports = { ensureSecureDataRoot, rejectRedirectedAncestors };
