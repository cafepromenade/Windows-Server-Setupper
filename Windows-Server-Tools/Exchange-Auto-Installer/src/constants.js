'use strict';

const path = require('node:path');

const SCHEMA_VERSION = 1;
const SUCCESS_EXIT_CODES = new Set([0, 1641, 3010]);
const REBOOT_EXIT_CODES = new Set([1641, 3010]);
const TRANSIENT_EXIT_CODES = new Set([1618]);
const PROFILE_KEYS = new Set([
  'organizationName',
  'targetDomain',
  'role',
  'installPath',
  'databaseName',
  'databasePath',
  'logPath',
  'installPrerequisites',
  'prepareSchema',
  'prepareActiveDirectory',
  'prepareDomains',
  'disableTelemetry',
  'resumeAfterRestart',
  'diagnosticData',
  'installWindowsComponents',
  'maxTransientRetries'
]);

const WINDOWS_FEATURES = Object.freeze([
  'Server-Media-Foundation',
  'NET-Framework-45-Features',
  'RPC-over-HTTP-proxy',
  'RSAT-Clustering',
  'RSAT-Clustering-CmdInterface',
  'RSAT-Clustering-Mgmt',
  'RSAT-Clustering-PowerShell',
  'WAS-Process-Model',
  'Web-Asp-Net45',
  'Web-Basic-Auth',
  'Web-Client-Auth',
  'Web-Digest-Auth',
  'Web-Dir-Browsing',
  'Web-Dyn-Compression',
  'Web-Http-Errors',
  'Web-Http-Logging',
  'Web-Http-Redirect',
  'Web-Http-Tracing',
  'Web-ISAPI-Ext',
  'Web-ISAPI-Filter',
  'Web-Lgcy-Mgmt-Console',
  'Web-Metabase',
  'Web-Mgmt-Console',
  'Web-Mgmt-Service',
  'Web-Net-Ext45',
  'Web-Request-Monitor',
  'Web-Server',
  'Web-Stat-Compression',
  'Web-Static-Content',
  'Web-Windows-Auth',
  'Web-WMI',
  'Windows-Identity-Foundation',
  'RSAT-ADDS'
]);

const STAGES = Object.freeze([
  {
    id: 'windows-features',
    title: 'Install Windows Server prerequisites',
    description: 'Installs the fixed Microsoft Windows feature set required by Exchange.',
    kind: 'powershell',
    timeoutMs: 90 * 60 * 1000
  },
  {
    id: 'prepare-schema',
    title: 'Prepare the Active Directory schema',
    description: 'Extends the schema using the selected Microsoft-signed Exchange media.',
    kind: 'exchange',
    setupArguments: ['/PrepareSchema'],
    timeoutMs: 3 * 60 * 60 * 1000
  },
  {
    id: 'prepare-ad',
    title: 'Prepare Active Directory',
    description: 'Creates the Exchange organization with the reviewed organization name.',
    kind: 'exchange',
    setupArguments: ['/PrepareAD'],
    timeoutMs: 3 * 60 * 60 * 1000
  },
  {
    id: 'prepare-domains',
    title: 'Prepare all Active Directory domains',
    description: 'Prepares every domain in the current forest for Exchange.',
    kind: 'exchange',
    setupArguments: ['/PrepareAllDomains'],
    timeoutMs: 3 * 60 * 60 * 1000
  },
  {
    id: 'install-mailbox',
    title: 'Install the Mailbox role',
    description: 'Installs the Exchange Mailbox role and any remaining Windows components.',
    kind: 'exchange',
    setupArguments: ['/Mode:Install', '/Roles:Mailbox'],
    timeoutMs: 8 * 60 * 60 * 1000
  },
  {
    id: 'postflight',
    title: 'Confirm the local Exchange installation',
    description: 'Reads the local Exchange registry and services without changing them.',
    kind: 'postflight',
    timeoutMs: 10 * 60 * 1000
  }
]);

function makeDefaultProfile(defaults = {}) {
  const domain = typeof defaults.domain === 'string' ? defaults.domain.trim().toLowerCase() : '';
  const firstLabel = domain.split('.')[0] || 'Exchange Organization';
  return {
    organizationName: firstLabel.replace(/[^a-z0-9 -]/gi, '').slice(0, 64) || 'Exchange Organization',
    targetDomain: domain,
    role: 'Mailbox',
    installPath: 'C:\\Program Files\\Microsoft\\Exchange Server\\V15',
    databaseName: 'Mailbox Database 01',
    databasePath: 'C:\\ExchangeDatabases\\Mailbox Database 01\\Mailbox Database 01.edb',
    logPath: 'C:\\ExchangeDatabases\\Mailbox Database 01\\Logs',
    installPrerequisites: true,
    prepareSchema: true,
    prepareActiveDirectory: true,
    prepareDomains: true,
    disableTelemetry: true,
    resumeAfterRestart: true,
    diagnosticData: 'OFF',
    installWindowsComponents: true,
    maxTransientRetries: 2
  };
}

function makeStageState(stage) {
  return {
    id: stage.id,
    title: stage.title,
    description: stage.description,
    status: 'pending',
    attempts: 0,
    startedAt: null,
    finishedAt: null,
    exitCode: null,
    lastError: null,
    reconciliation: null
  };
}

function makeInitialState(defaults = {}) {
  return {
    schemaVersion: SCHEMA_VERSION,
    revision: 0,
    phase: 'review',
    profile: makeDefaultProfile(defaults),
    detected: defaults,
    media: null,
    preflight: {
      status: 'not-run',
      checkedAt: null,
      checks: []
    },
    stages: STAGES.map(makeStageState),
    currentStageId: null,
    cancelRequested: false,
    rebootRequired: false,
    lastError: null,
    logPath: null,
    openCode: {
      status: 'not-checked',
      yoloMode: false,
      activeRepair: null,
      lastResult: null
    },
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString()
  };
}

function getSetupPath(media) {
  return media && media.path ? path.resolve(media.path) : null;
}

module.exports = {
  PROFILE_KEYS,
  REBOOT_EXIT_CODES,
  SCHEMA_VERSION,
  STAGES,
  SUCCESS_EXIT_CODES,
  TRANSIENT_EXIT_CODES,
  WINDOWS_FEATURES,
  getSetupPath,
  makeInitialState
};
