'use strict';

const { execFileSync } = require('node:child_process');
const { OPENCODE_ARCHIVE_BYTES, OPENCODE_ARCHIVE_SHA256, OPENCODE_VERSION } = require('../src/opencode-manager');

const name = 'opencode-windows-x64.zip';
const raw = execFileSync('gh', ['release', 'view', `v${OPENCODE_VERSION}`, '--repo', 'anomalyco/opencode', '--json', 'tagName,isDraft,isPrerelease,assets'], { encoding: 'utf8', windowsHide: true, maxBuffer: 2 * 1024 * 1024 });
const release = JSON.parse(raw);
if (release.tagName !== `v${OPENCODE_VERSION}` || release.isDraft || release.isPrerelease) throw new Error('The pinned OpenCode release is missing, draft, or prerelease.');
const asset = release.assets.find((entry) => entry.name === name);
if (!asset) throw new Error(`The official release does not contain ${name}.`);
if (asset.size !== OPENCODE_ARCHIVE_BYTES) throw new Error(`OpenCode asset size mismatch: expected ${OPENCODE_ARCHIVE_BYTES}, official metadata reports ${asset.size}.`);
if (asset.digest !== `sha256:${OPENCODE_ARCHIVE_SHA256}`) throw new Error(`OpenCode asset digest mismatch: expected ${OPENCODE_ARCHIVE_SHA256}, official metadata reports ${asset.digest}.`);
console.log(`Verified official OpenCode v${OPENCODE_VERSION} metadata: ${name}, ${asset.size} bytes, ${asset.digest}.`);
