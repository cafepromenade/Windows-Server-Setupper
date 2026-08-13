'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { runProcess } = require('./process-runner');

const EXCHANGE_ISO_PATH = 'Windows-Server-Tools/Exchange-Install-API/exchange.iso';
const EXCHANGE_ISO_BYTES = 6_402_453_504;
const EXCHANGE_ISO_SHA256 = 'cd2b13f2c297187776af4cff3541b4be3c677cf907cca69d85ab0e2b70377bd1';

function createMediaHydrator({ repositoryRoot }) {
  const helper = path.join(repositoryRoot, '.desktop-material', 'cheap-lfs', 'hydrate.mjs');
  if (!fs.existsSync(helper)) throw new Error('The repository Cheap LFS helper is unavailable.');

  async function verifyMetadata() {
    const result = await runProcess({ file: process.execPath, args: [helper, '--verify-only'], cwd: repositoryRoot, env: minimalEnvironment(), timeoutMs: 30_000, privatePaths: [repositoryRoot] });
    if (result.exitCode !== 0 || result.timedOut || result.aborted) throw new Error(result.stderrTail || 'Cheap LFS metadata verification did not complete.');
    const payload = parseLastJson(result.stdoutTail);
    if (payload.status !== 'verified' || payload.downloadedBytes !== 0 || !Array.isArray(payload.releaseAssets) || payload.releaseAssets.length !== 13 || !Array.isArray(payload.checked)) throw new Error('Cheap LFS verification returned incomplete or unexpected evidence.');
    const entry = payload.checked.find((item) => item.path === EXCHANGE_ISO_PATH);
    if (!entry || entry.sizeInBytes !== EXCHANGE_ISO_BYTES || entry.sha256 !== EXCHANGE_ISO_SHA256) throw new Error('Cheap LFS Exchange ISO metadata does not match the pinned size and digest.');
    return { status: 'verified', downloadedBytes: 0, checked: entry, releaseAssetCount: payload.releaseAssets.length, repository: payload.repository, mode: payload.mode };
  }

  async function hydrate() {
    await verifyMetadata();
    const result = await runProcess({ file: process.execPath, args: [helper, '--path', EXCHANGE_ISO_PATH], cwd: repositoryRoot, env: minimalEnvironment(), timeoutMs: 8 * 60 * 60 * 1000, privatePaths: [repositoryRoot] });
    if (result.exitCode !== 0 || result.timedOut || result.aborted) throw new Error(result.stderrTail || 'Cheap LFS Exchange ISO hydration did not complete.');
    const isoPath = path.join(repositoryRoot, EXCHANGE_ISO_PATH);
    const stat = fs.statSync(isoPath);
    if (!stat.isFile() || stat.size !== EXCHANGE_ISO_BYTES) throw new Error('Hydrated Exchange ISO size does not match the pinned object.');
    const digest = await sha256File(isoPath);
    if (digest !== EXCHANGE_ISO_SHA256) throw new Error('Hydrated Exchange ISO digest does not match the pinned object.');
    return { status: 'complete', path: isoPath, sizeInBytes: stat.size, sha256: digest };
  }

  return { verifyMetadata, hydrate };
}

function parseLastJson(text) {
  for (const line of String(text || '').split(/\r?\n/).reverse()) { try { return JSON.parse(line); } catch { /* Search bounded process tail for its final JSON record. */ } }
  throw new Error('Cheap LFS did not return structured evidence.');
}
function minimalEnvironment() { return { SystemRoot: process.env.SystemRoot, WINDIR: process.env.WINDIR, COMSPEC: process.env.COMSPEC, TEMP: process.env.TEMP, TMP: process.env.TMP, PATH: process.env.PATH, GH_CONFIG_DIR: process.env.GH_CONFIG_DIR }; }
function sha256File(filePath) { return new Promise((resolve, reject) => { const hash = crypto.createHash('sha256'); const stream = fs.createReadStream(filePath); stream.on('data', (chunk) => hash.update(chunk)); stream.once('error', reject); stream.once('end', () => resolve(hash.digest('hex'))); }); }

module.exports = { EXCHANGE_ISO_BYTES, EXCHANGE_ISO_PATH, EXCHANGE_ISO_SHA256, createMediaHydrator };
