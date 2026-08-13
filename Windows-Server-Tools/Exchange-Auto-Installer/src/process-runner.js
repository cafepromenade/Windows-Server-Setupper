'use strict';

const { spawn } = require('node:child_process');
const { redactText } = require('./redaction');

function terminateProcessTree(pid) {
  return new Promise((resolve) => {
    if (!Number.isSafeInteger(pid) || pid <= 0 || process.platform !== 'win32') {
      resolve(false);
      return;
    }
    const killer = spawn('taskkill.exe', ['/PID', String(pid), '/T', '/F'], {
      windowsHide: true,
      stdio: 'ignore',
      shell: false
    });
    killer.once('error', () => resolve(false));
    killer.once('exit', (code) => resolve(code === 0));
  });
}

function runProcess(options) {
  const {
    file,
    args = [],
    cwd,
    env,
    timeoutMs,
    onLine = () => {},
    privatePaths = [],
    signal
  } = options;

  if (!pathIsExecutableNameOrAbsolute(file) || !Array.isArray(args) || args.some((arg) => typeof arg !== 'string')) {
    throw new TypeError('The process request is not structurally valid.');
  }

  return new Promise((resolve) => {
    const startedAt = Date.now();
    let child;
    let settled = false;
    let timedOut = false;
    let aborted = false;
    let stdoutTail = '';
    let stderrTail = '';
    let timeout = null;
    let abortHandler = () => {};

    const finish = (result) => {
      if (settled) return;
      settled = true;
      if (timeout) clearTimeout(timeout);
      if (signal) signal.removeEventListener('abort', abortHandler);
      resolve({
        ...result,
        timedOut,
        aborted,
        durationMs: Date.now() - startedAt,
        stdoutTail,
        stderrTail
      });
    };

    const acceptChunk = (source, chunk) => {
      const redacted = redactText(chunk.toString('utf8'), privatePaths);
      for (const line of redacted.split(/\r?\n/).filter(Boolean)) {
        onLine(source, line);
      }
      if (source === 'stdout') stdoutTail = `${stdoutTail}${redacted}`.slice(-32_768);
      else stderrTail = `${stderrTail}${redacted}`.slice(-32_768);
    };

    try {
      child = spawn(file, args, {
        cwd,
        env,
        windowsHide: true,
        shell: false,
        stdio: ['ignore', 'pipe', 'pipe']
      });
    } catch (error) {
      finish({ exitCode: null, signal: null, spawnError: redactText(error.message, privatePaths) });
      return;
    }

    child.stdout.on('data', (chunk) => acceptChunk('stdout', chunk));
    child.stderr.on('data', (chunk) => acceptChunk('stderr', chunk));
    child.once('error', (error) => finish({ exitCode: null, signal: null, spawnError: redactText(error.message, privatePaths) }));
    child.once('exit', (exitCode, exitSignal) => finish({ exitCode, signal: exitSignal, spawnError: null }));

    const stop = async (reason) => {
      if (settled || !child) return;
      if (reason === 'timeout') timedOut = true;
      if (reason === 'abort') aborted = true;
      const terminated = await terminateProcessTree(child.pid);
      if (!terminated) {
        child.kill('SIGKILL');
      }
    };

    timeout = setTimeout(() => stop('timeout'), Math.max(1_000, timeoutMs || 60_000));
    abortHandler = () => stop('abort');
    if (signal) {
      if (signal.aborted) abortHandler();
      else signal.addEventListener('abort', abortHandler, { once: true });
    }
  });
}

function pathIsExecutableNameOrAbsolute(file) {
  return typeof file === 'string' && file.length > 0 && file.length <= 1_024 && !/[\r\n\0]/.test(file);
}

module.exports = { runProcess, terminateProcessTree };
