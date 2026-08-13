'use strict';

const os = require('node:os');
const path = require('node:path');

const SECRET_ASSIGNMENT = /\b(password|passwd|pwd|secret|token|apikey|api_key|credential)\b\s*[:=]\s*([^\s,;]+)/gi;
const CONNECTION_SECRET = /(password|pwd|access[_-]?token|client[_-]?secret)=([^;\s]+)/gi;
const UNCREDENTIAL = /(?:https?:\/\/)([^\s/@:]+):([^\s/@]+)@/gi;

function redactText(value, additionalPrivatePaths = []) {
  let text = String(value ?? '');
  text = text.replace(SECRET_ASSIGNMENT, '$1=[REDACTED]');
  text = text.replace(CONNECTION_SECRET, '$1=[REDACTED]');
  text = text.replace(UNCREDENTIAL, (match) => match.replace(/\/\/.*@/, '//[REDACTED]@'));

  const privatePaths = [os.homedir(), process.env.APPDATA, process.env.LOCALAPPDATA, ...additionalPrivatePaths]
    .filter(Boolean)
    .map((entry) => path.resolve(entry));

  for (const privatePath of privatePaths) {
    const escaped = privatePath.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    text = text.replace(new RegExp(escaped, 'gi'), '%PRIVATE_PATH%');
  }

  return text.slice(0, 16_384);
}

function redactObject(value, additionalPrivatePaths = [], depth = 0) {
  if (depth > 6) {
    return '[TRUNCATED]';
  }
  if (Array.isArray(value)) {
    return value.slice(0, 200).map((item) => redactObject(item, additionalPrivatePaths, depth + 1));
  }
  if (value && typeof value === 'object') {
    const output = {};
    for (const [key, item] of Object.entries(value).slice(0, 200)) {
      if (/password|passwd|secret|token|credential|authorization/i.test(key)) {
        output[key] = '[REDACTED]';
      } else {
        output[key] = redactObject(item, additionalPrivatePaths, depth + 1);
      }
    }
    return output;
  }
  return typeof value === 'string' ? redactText(value, additionalPrivatePaths) : value;
}

module.exports = { redactObject, redactText };
