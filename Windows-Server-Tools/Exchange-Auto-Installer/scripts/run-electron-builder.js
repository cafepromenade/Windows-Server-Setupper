'use strict';

const path = require('node:path');
const { spawnSync } = require('node:child_process');

const mode = process.argv[2];
if (!['--dir', '--squirrel'].includes(mode)) {
  throw new Error('Expected --dir or --squirrel.');
}

const projectRoot = path.resolve(__dirname, '..');
const builderCli = path.join(projectRoot, 'node_modules', 'electron-builder', 'cli.js');
const args = mode === '--dir'
  ? [builderCli, '--dir', '--win', '--publish', 'never']
  : [builderCli, '--win', 'squirrel', '--publish', 'never'];
const environment = { ...process.env };

for (const name of [
  'CSC_LINK',
  'CSC_KEY_PASSWORD',
  'CSC_NAME',
  'WIN_CSC_LINK',
  'WIN_CSC_KEY_PASSWORD',
  'AZURE_TENANT_ID',
  'AZURE_CLIENT_ID',
  'AZURE_CLIENT_SECRET',
  'AZURE_CODE_SIGNING_ACCOUNT_NAME',
  'AZURE_CERTIFICATE_PROFILE_NAME'
]) {
  delete environment[name];
}

environment.CSC_IDENTITY_AUTO_DISCOVERY = 'false';

const result = spawnSync(process.execPath, args, {
  cwd: projectRoot,
  env: environment,
  stdio: 'inherit',
  windowsHide: true,
  shell: false
});

if (result.error) throw result.error;
process.exit(Number.isInteger(result.status) ? result.status : 1);
