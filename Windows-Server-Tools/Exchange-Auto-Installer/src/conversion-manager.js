'use strict';

const ADAPTER_CATEGORIES = Object.freeze([
  { id: 'documents-pdf', label: 'Documents / PDF' },
  { id: 'images', label: 'Images' },
  { id: 'audio', label: 'Audio' },
  { id: 'video', label: 'Video' },
  { id: 'archives', label: 'Archives' },
  { id: 'structured-data', label: 'Structured Data / Spreadsheets' },
  { id: 'code-text', label: 'Code / Text' },
  { id: 'binary-encodings', label: 'Binary Encodings' }
]);

const ADAPTERS = Object.freeze([
  { id: 'json-pretty', category: 'structured-data', source: 'application/json', target: 'application/json', bundled: true, enabled: true, lossiness: 'none', label: 'Format JSON' },
  { id: 'text-normalize', category: 'code-text', source: 'text/plain', target: 'text/plain', bundled: true, enabled: true, lossiness: 'line endings become CRLF', label: 'Normalize text for Windows' },
  { id: 'binary-base64', category: 'binary-encodings', source: 'application/octet-stream', target: 'text/base64', bundled: true, enabled: true, lossiness: 'none', label: 'Encode as Base64' },
  { id: 'base64-binary', category: 'binary-encodings', source: 'text/base64', target: 'application/octet-stream', bundled: true, enabled: true, lossiness: 'none', label: 'Decode Base64' },
  { id: 'pdf-tools', category: 'documents-pdf', bundled: false, enabled: false, missing: 'No verified offline PDF adapter is bundled in this release.', label: 'Inspect, split, merge, extract, reorder, rotate, and edit PDF metadata' },
  { id: 'image-convert', category: 'images', bundled: false, enabled: false, missing: 'No isolated offline image decoder is bundled in this release.', label: 'Convert image formats' },
  { id: 'audio-convert', category: 'audio', bundled: false, enabled: false, missing: 'No isolated offline audio adapter is bundled in this release.', label: 'Convert audio formats' },
  { id: 'video-convert', category: 'video', bundled: false, enabled: false, missing: 'No isolated offline video adapter is bundled in this release.', label: 'Convert video formats' },
  { id: 'archive-convert', category: 'archives', bundled: false, enabled: false, missing: 'No verified offline archive adapter is bundled in this release.', label: 'Convert archives' }
]);

function catalog() { return { schemaVersion: 1, categories: ADAPTER_CATEGORIES, adapters: ADAPTERS }; }

function detectType(buffer) {
  if (!Buffer.isBuffer(buffer) || buffer.length > 64 * 1024 * 1024) throw new Error('Source bytes are outside the conversion bound.');
  const sample = buffer.subarray(0, 4_096);
  if (sample.subarray(0, 5).toString('ascii') === '%PDF-') return 'application/pdf';
  if (sample.length >= 8 && sample.subarray(0, 8).equals(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]))) return 'image/png';
  const text = sample.toString('utf8');
  if (Buffer.from(text, 'utf8').length === sample.length) {
    try { JSON.parse(buffer.toString('utf8')); return 'application/json'; } catch { /* Plain UTF-8 text is handled below. */ }
    if (/^[A-Za-z0-9+/\s]*={0,2}$/.test(text) && text.replace(/\s/g, '').length % 4 === 0) return 'text/base64';
    return 'text/plain';
  }
  return 'application/octet-stream';
}

function convertBuffer(adapterId, source) {
  if (!Buffer.isBuffer(source)) throw new Error('Conversion input must be bytes.');
  if (source.length > 64 * 1024 * 1024) throw new Error('Conversion input exceeds the per-file bound.');
  const adapter = ADAPTERS.find((entry) => entry.id === adapterId);
  if (!adapter) throw new Error('Unknown conversion adapter.');
  if (!adapter.enabled || !adapter.bundled) throw new Error(adapter.missing || 'The conversion adapter is unavailable.');
  if (adapterId === 'json-pretty') return Buffer.from(`${JSON.stringify(JSON.parse(source.toString('utf8')), null, 2)}\n`, 'utf8');
  if (adapterId === 'text-normalize') return Buffer.from(source.toString('utf8').replace(/\r\n|\r|\n/g, '\r\n'), 'utf8');
  if (adapterId === 'binary-base64') return Buffer.from(source.toString('base64'), 'ascii');
  if (adapterId === 'base64-binary') {
    const text = source.toString('ascii').replace(/\s/g, '');
    if (!text || !/^[A-Za-z0-9+/]*={0,2}$/.test(text) || text.length % 4 !== 0) throw new Error('Base64 input is malformed.');
    return Buffer.from(text, 'base64');
  }
  throw new Error('The adapter has no implementation.');
}

module.exports = { ADAPTERS, ADAPTER_CATEGORIES, catalog, convertBuffer, detectType };
