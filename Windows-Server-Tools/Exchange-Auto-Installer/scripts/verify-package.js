'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

const root = path.join(__dirname, '..');
const packageDirectory = path.join(root, 'dist', 'squirrel-windows');
const setup = path.join(packageDirectory, 'ExchangeAutoInstaller-1.0.0-x64-Setup.exe');
const releases = path.join(packageDirectory, 'RELEASES');
const fullPackage = path.join(packageDirectory, 'exchange-auto-installer-1.0.0-full.nupkg');
for (const file of [setup, releases, fullPackage]) if (!fs.statSync(file).isFile() || fs.statSync(file).size === 0) throw new Error(`Required package output is missing or empty: ${file}`);
const releaseText = fs.readFileSync(releases, 'utf8');
if (!releaseText.includes(path.basename(fullPackage)) || !releaseText.includes(String(fs.statSync(fullPackage).size))) throw new Error('RELEASES does not reference the exact full package name and byte count.');
const signature = execFileSync('pwsh', ['-NoLogo', '-NoProfile', '-NonInteractive', '-Command', `(Get-AuthenticodeSignature -LiteralPath '${setup.replace(/'/g, "''")}').Status`], { encoding: 'utf8', windowsHide: true }).trim();
if (signature !== 'NotSigned') throw new Error(`Setup executable signature status must be NotSigned, got ${signature}.`);
const ico = fs.readFileSync(path.join(root, 'assets', 'app.ico'));
if (ico.readUInt16LE(2) !== 1 || ico.readUInt16LE(4) < 7) throw new Error('Committed application icon is not a seven-size ICO.');
const commit = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8', windowsHide: true }).trim();
const output = [setup, releases, fullPackage].map((file) => ({ file: path.relative(root, file), bytes: fs.statSync(file).size, sha256: crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex') }));
console.log(JSON.stringify({ commit, unsigned: true, signature, outputs: output }, null, 2));
