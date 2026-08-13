'use strict';

const modules = [
  '../src/constants', '../src/conversion-manager', '../src/installer-engine', '../src/machine-mutation-lease', '../src/media-hydrator', '../src/ollama-manager',
  '../src/opencode-manager', '../src/preflight', '../src/process-runner', '../src/redaction', '../src/secure-data-root', '../src/settings-store', '../src/state-store', '../src/update-manager'
];
for (const modulePath of modules) {
  const loaded = require(modulePath);
  if (!loaded || typeof loaded !== 'object' || Object.keys(loaded).length === 0) throw new Error(`${modulePath} exposes no contract.`);
}
console.log(`Loaded and inspected ${modules.length} CommonJS module contracts.`);
