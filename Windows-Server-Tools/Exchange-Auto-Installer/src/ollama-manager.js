'use strict';

const http = require('node:http');

const OLLAMA_HOST = '127.0.0.1';
const OLLAMA_PORT = 11434;
const MAX_RESPONSE_BYTES = 8 * 1024 * 1024;
const ALLOWED_GET_PATHS = new Set(['/api/version', '/api/tags', '/api/ps']);

function createOllamaManager() {
  async function status() {
    try {
      const [version, tags, running] = await Promise.all([requestJson('/api/version'), requestJson('/api/tags'), requestJson('/api/ps')]);
      return { status: 'healthy', version: String(version.version || 'unknown'), installed: boundedModels(tags.models), running: boundedModels(running.models), host: `http://${OLLAMA_HOST}:${OLLAMA_PORT}`, checkedAt: new Date().toISOString() };
    } catch (error) {
      return { status: 'unavailable', reason: safeMessage(error), installed: [], running: [], host: `http://${OLLAMA_HOST}:${OLLAMA_PORT}`, checkedAt: new Date().toISOString() };
    }
  }
  return { status };
}

function requestJson(route) {
  if (!ALLOWED_GET_PATHS.has(route)) return Promise.reject(new Error('The local Ollama route is not allowlisted.'));
  return new Promise((resolve, reject) => {
    const request = http.get({ hostname: OLLAMA_HOST, port: OLLAMA_PORT, path: route, timeout: 5_000, headers: { Accept: 'application/json' } }, (response) => {
      if (response.statusCode !== 200) { response.resume(); reject(new Error(`Local Ollama returned HTTP ${response.statusCode}.`)); return; }
      const chunks = [];
      let received = 0;
      response.on('data', (chunk) => {
        received += chunk.length;
        if (received > MAX_RESPONSE_BYTES) request.destroy(new Error('Local Ollama response exceeded the bound.'));
        else chunks.push(chunk);
      });
      response.on('end', () => {
        try { resolve(JSON.parse(Buffer.concat(chunks).toString('utf8'))); }
        catch { reject(new Error('Local Ollama returned malformed JSON.')); }
      });
    });
    request.once('timeout', () => request.destroy(new Error('Local Ollama did not respond before the timeout.')));
    request.once('error', reject);
  });
}

function boundedModels(models) {
  if (!Array.isArray(models)) return [];
  return models.slice(0, 10_000).map((model) => ({ name: String(model.name || model.model || '').slice(0, 240), size: Number(model.size) || null, digest: String(model.digest || '').slice(0, 128), modifiedAt: String(model.modified_at || '').slice(0, 80) }));
}

function safeMessage(error) { return String(error?.message || error || 'Local Ollama is unavailable.').replace(/[\r\n\0]/g, ' ').slice(0, 500); }

module.exports = { ALLOWED_GET_PATHS, MAX_RESPONSE_BYTES, OLLAMA_HOST, OLLAMA_PORT, createOllamaManager, requestJson };
