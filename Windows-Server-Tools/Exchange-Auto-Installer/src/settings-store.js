'use strict';

const fs = require('node:fs');
const path = require('node:path');

const SETTINGS_SCHEMA_VERSION = 1;
const PERSONAL_VOCABULARY_MAX_BYTES = 256 * 1024;
const PERSONAL_VOCABULARY_MAX_ENTRIES = 1_000;
const SAFE_LANGUAGES = new Set(['en', 'yue', 'bilingual']);
const SAFE_THEMES = new Set(['system', 'light', 'dark', 'contrast']);
const SAFE_DENSITIES = new Set(['comfortable', 'compact', 'spacious']);
const SAFE_DOCKS = new Set(['left', 'right', 'top', 'bottom']);
const UNSAFE_KEYS = new Set(['__proto__', 'prototype', 'constructor']);

const DEFAULT_SETTINGS = Object.freeze({
  schemaVersion: SETTINGS_SCHEMA_VERSION,
  language: 'en',
  funnyEnglish: 2,
  funnyCantonese: 3,
  showDialogEmojis: true,
  theme: 'system',
  density: 'comfortable',
  accent: '#1f5f91',
  fontFamily: 'system-ui',
  fontScale: 1,
  fontWeight: 400,
  reducedMotion: false,
  tabDock: 'left',
  appDisplayName: 'Exchange Auto Installer',
  schoolMode: false,
  schoolModeName: 'School mode',
  narratorEnabled: false,
  narratorLanguage: 'en',
  narratorVoiceEnglish: 'auto',
  narratorVoiceCantonese: 'auto',
  narrationRate: 1,
  narrationPitch: 1,
  updateEnabled: true,
  schedules: []
});

class SettingsStore {
  constructor(userDataDir) {
    this.root = path.join(userDataDir, 'exchange-auto-installer');
    this.filePath = path.join(this.root, 'settings.json');
    this.vocabularyPath = path.join(this.root, 'personal-vocabulary.json');
  }

  load() {
    const parsed = readJson(this.filePath, 512 * 1024);
    try { return validateSettings(parsed || DEFAULT_SETTINGS); }
    catch { return { ...DEFAULT_SETTINGS, schedules: [] }; }
  }

  save(candidate) {
    const next = validateSettings(candidate);
    atomicWriteJson(this.filePath, next);
    return next;
  }

  importVocabulary(payload) {
    const validated = validatePersonalVocabulary(payload);
    atomicWriteJson(this.vocabularyPath, validated);
    return { schemaVersion: validated.schemaVersion, entryCount: Object.keys(validated.replacements).length };
  }

  loadVocabulary() {
    const parsed = readJson(this.vocabularyPath, PERSONAL_VOCABULARY_MAX_BYTES);
    if (!parsed) return null;
    try { return validatePersonalVocabulary(Buffer.from(JSON.stringify(parsed), 'utf8')); }
    catch { return null; }
  }

  clearVocabulary() {
    try { fs.rmSync(this.vocabularyPath, { force: true }); } catch { /* Missing private cache is already clear. */ }
    return { cleared: true };
  }

  exportRedacted() {
    return { ...this.load(), personalVocabulary: 'omitted: private local data' };
  }
}

function validateSettings(candidate) {
  if (!isPlainObject(candidate)) throw new Error('Settings must be an object.');
  const unknown = Object.keys(candidate).filter((key) => !(key in DEFAULT_SETTINGS));
  if (unknown.length) throw new Error(`Unknown setting: ${unknown[0]}`);
  const next = { ...DEFAULT_SETTINGS, ...candidate };
  if (next.schemaVersion !== SETTINGS_SCHEMA_VERSION) throw new Error('Unsupported settings schema version.');
  if (!SAFE_LANGUAGES.has(next.language)) throw new Error('Unsupported language mode.');
  if (!SAFE_LANGUAGES.has(next.narratorLanguage)) throw new Error('Unsupported narrator language.');
  if (!SAFE_THEMES.has(next.theme)) throw new Error('Unsupported theme.');
  if (!SAFE_DENSITIES.has(next.density)) throw new Error('Unsupported density.');
  if (!SAFE_DOCKS.has(next.tabDock)) throw new Error('Unsupported tab dock.');
  for (const key of ['funnyEnglish', 'funnyCantonese']) requireNumber(next, key, 1, 5);
  requireNumber(next, 'fontScale', 0.75, 2);
  requireNumber(next, 'fontWeight', 100, 900);
  requireNumber(next, 'narrationRate', 0.5, 2);
  requireNumber(next, 'narrationPitch', 0.5, 2);
  for (const key of ['showDialogEmojis', 'reducedMotion', 'schoolMode', 'narratorEnabled', 'updateEnabled']) {
    if (typeof next[key] !== 'boolean') throw new Error(`${key} must be true or false.`);
  }
  for (const [key, max] of [['accent', 16], ['fontFamily', 120], ['appDisplayName', 80], ['schoolModeName', 80], ['narratorVoiceEnglish', 240], ['narratorVoiceCantonese', 240]]) {
    if (typeof next[key] !== 'string' || !next[key].trim() || next[key].length > max || /[\r\n\0]/.test(next[key])) throw new Error(`${key} is invalid.`);
  }
  if (!/^#[0-9a-f]{6}$/i.test(next.accent)) throw new Error('Accent must use six-digit hexadecimal notation.');
  if (!Array.isArray(next.schedules) || next.schedules.length > 100) throw new Error('Schedules exceed the supported bound.');
  next.schedules = next.schedules.map(validateSchedule);
  return next;
}

function validateSchedule(schedule, index) {
  if (!isPlainObject(schedule)) throw new Error(`Schedule ${index + 1} is invalid.`);
  const allowed = new Set(['id', 'label', 'enabled', 'weekdays', 'startTime', 'endTime', 'startDate', 'endDate', 'values']);
  const unknown = Object.keys(schedule).find((key) => !allowed.has(key));
  if (unknown) throw new Error(`Schedule field ${unknown} is not supported.`);
  const id = boundedString(schedule.id, 80, 'schedule identifier');
  const label = boundedString(schedule.label, 120, 'schedule label');
  if (typeof schedule.enabled !== 'boolean') throw new Error('Schedule enabled state is invalid.');
  const weekdays = Array.isArray(schedule.weekdays) ? schedule.weekdays : [];
  if (!weekdays.length || weekdays.length > 7 || weekdays.some((day) => !Number.isInteger(day) || day < 0 || day > 6)) throw new Error('Schedule weekdays are invalid.');
  if (!/^([01]\d|2[0-3]):[0-5]\d$/.test(schedule.startTime) || !/^([01]\d|2[0-3]):[0-5]\d$/.test(schedule.endTime)) throw new Error('Schedule times are invalid.');
  if (schedule.startDate && !/^\d{4}-\d{2}-\d{2}$/.test(schedule.startDate)) throw new Error('Schedule start date is invalid.');
  if (schedule.endDate && !/^\d{4}-\d{2}-\d{2}$/.test(schedule.endDate)) throw new Error('Schedule end date is invalid.');
  if (!isPlainObject(schedule.values) || Object.keys(schedule.values).length > 20) throw new Error('Schedule values are invalid.');
  return { id, label, enabled: schedule.enabled, weekdays: [...new Set(weekdays)], startTime: schedule.startTime, endTime: schedule.endTime, startDate: schedule.startDate || null, endDate: schedule.endDate || null, values: schedule.values };
}

function validatePersonalVocabulary(payload) {
  const bytes = Buffer.isBuffer(payload) ? payload : Buffer.from(payload || '');
  if (!bytes.length || bytes.length > PERSONAL_VOCABULARY_MAX_BYTES) throw new Error('Personal vocabulary file size is invalid.');
  const text = bytes.toString('utf8');
  if (Buffer.from(text, 'utf8').length !== bytes.length) throw new Error('Personal vocabulary must be valid UTF-8.');
  rejectDuplicateKeys(text);
  let parsed;
  try { parsed = JSON.parse(text); } catch { throw new Error('Personal vocabulary is not valid JSON.'); }
  if (!isPlainObject(parsed) || parsed.schemaVersion !== 1 || !isPlainObject(parsed.replacements)) throw new Error('Personal vocabulary schema is invalid.');
  if (Object.keys(parsed).some((key) => !['schemaVersion', 'replacements'].includes(key))) throw new Error('Personal vocabulary contains an unexpected field.');
  const entries = Object.entries(parsed.replacements);
  if (entries.length > PERSONAL_VOCABULARY_MAX_ENTRIES) throw new Error('Personal vocabulary contains too many entries.');
  const replacements = Object.create(null);
  for (const [key, value] of entries) {
    if (UNSAFE_KEYS.has(key) || !key || key.length > 128 || /[\r\n\0]/.test(key)) throw new Error('Personal vocabulary contains an unsafe key.');
    if (typeof value !== 'string' || value.length > 512 || /\0/.test(value)) throw new Error('Personal vocabulary replacement is invalid.');
    replacements[key] = value;
  }
  return { schemaVersion: 1, replacements: { ...replacements } };
}

function rejectDuplicateKeys(text) {
  const stack = [new Set()];
  let inString = false;
  let escaped = false;
  let token = '';
  for (let index = 0; index < text.length; index += 1) {
    const char = text[index];
    if (inString) {
      if (escaped) { escaped = false; token += char; continue; }
      if (char === '\\') { escaped = true; token += char; continue; }
      if (char === '"') {
        inString = false;
        const rest = text.slice(index + 1);
        if (/^\s*:/.test(rest)) {
          const key = JSON.parse(`"${token}"`);
          if (stack.at(-1).has(key)) throw new Error('Personal vocabulary contains duplicate keys.');
          stack.at(-1).add(key);
        }
        token = '';
      } else token += char;
      continue;
    }
    if (char === '"') { inString = true; token = ''; }
    else if (char === '{') stack.push(new Set());
    else if (char === '}') stack.pop();
  }
}

function atomicWriteJson(filePath, value) {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  const temporary = `${filePath}.${process.pid}.tmp`;
  fs.writeFileSync(temporary, `${JSON.stringify(value, null, 2)}\n`, { mode: 0o600 });
  fs.renameSync(temporary, filePath);
}

function readJson(filePath, maxBytes) {
  try { const stat = fs.statSync(filePath); if (!stat.isFile() || stat.size > maxBytes) return null; return JSON.parse(fs.readFileSync(filePath, 'utf8')); } catch { return null; }
}

function isPlainObject(value) { return Boolean(value) && typeof value === 'object' && !Array.isArray(value) && Object.getPrototypeOf(value) === Object.prototype; }
function boundedString(value, max, label) { if (typeof value !== 'string' || !value.trim() || value.length > max || /[\r\n\0]/.test(value)) throw new Error(`The ${label} is invalid.`); return value.trim(); }
function requireNumber(target, key, min, max) { if (typeof target[key] !== 'number' || !Number.isFinite(target[key]) || target[key] < min || target[key] > max) throw new Error(`${key} is outside its supported range.`); }

module.exports = { DEFAULT_SETTINGS, PERSONAL_VOCABULARY_MAX_BYTES, PERSONAL_VOCABULARY_MAX_ENTRIES, SETTINGS_SCHEMA_VERSION, SettingsStore, validatePersonalVocabulary, validateSettings };
