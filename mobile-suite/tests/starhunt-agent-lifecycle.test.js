'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const test = require('node:test');

const moduleRoot = path.resolve(__dirname, '..');
const agentPath = process.env.STARHUNT_AGENT_SOURCE
  || path.join(moduleRoot, 'app', 'src', 'main', 'assets', 'starhunt_agent.js');
const agentSource = fs.readFileSync(agentPath, 'utf8');
const marker = '// agent.js';
const markerAt = agentSource.lastIndexOf(marker);
assert.notEqual(markerAt, -1, `could not find production agent tail marker in ${agentPath}`);
const tailStart = agentSource.indexOf('\n', markerAt) + 1;
const agentTail = agentSource.slice(tailStart);

function createRuntime() {
  const events = [];
  const luaCalls = [];
  const performQueue = [];
  const glQueue = [];
  const submissions = [];
  const timeouts = new Map();
  const intervals = new Map();
  let clock = 1000;
  let timerId = 0;
  const runtime = {
    events,
    luaCalls,
    performQueue,
    glQueue,
    submissions,
    timeouts,
    intervals,
    luaStatus: 'running',
    cleanupResult: '0',
    beforeFutureDone: null,
    nextTimeoutAt() {
      return Math.min(Infinity, ...Array.from(timeouts.values(), item => item.at));
    },
    flushPerform() {
      let count = 0;
      while (performQueue.length) {
        assert.ok(count++ < 200, 'Java.perform queue did not settle');
        performQueue.shift()();
      }
    },
    runNextGl() {
      const task = glQueue.shift();
      assert.ok(task, 'expected a queued GL task');
      if (typeof task.run === 'function') task.run();
      else task.runnable.run();
      return task;
    },
    runDueTimer() {
      const dueTimeout = Array.from(timeouts.entries())
        .filter(([, item]) => item.at <= clock)
        .sort((a, b) => a[1].at - b[1].at)[0];
      const dueInterval = Array.from(intervals.entries())
        .filter(([, item]) => item.at <= clock)
        .sort((a, b) => a[1].at - b[1].at)[0];
      if (!dueTimeout && !dueInterval) return false;
      if (dueTimeout && (!dueInterval || dueTimeout[1].at <= dueInterval[1].at)) {
        timeouts.delete(dueTimeout[0]);
        dueTimeout[1].fn();
      } else {
        dueInterval[1].at = clock + dueInterval[1].delay;
        dueInterval[1].fn();
      }
      return true;
    },
    advance(ms) {
      clock += ms;
      let count = 0;
      while (this.runDueTimer()) assert.ok(count++ < 200, 'timer queue did not settle');
    },
    pump({ runGl = true } = {}) {
      let count = 0;
      for (;;) {
        assert.ok(count++ < 400, 'agent lifecycle did not settle');
        if (performQueue.length) {
          this.flushPerform();
          continue;
        }
        if (runGl && glQueue.length) {
          this.runNextGl();
          clock += 10;
          continue;
        }
        if (this.runDueTimer()) continue;
        const futureWasRun = sandbox.inFlightFuture
          && sandbox.inFlightFuture.isDone()
          && !glQueue.includes(sandbox.inFlightFuture);
        const canAdvanceForProgress = runGl
          ? (futureWasRun || sandbox.inFlightFuture === null)
          : (sandbox.inFlightFuture === null && glQueue.length === 0);
        if (canAdvanceForProgress && timeouts.size) {
          clock = Math.min(...Array.from(timeouts.values(), item => item.at));
          continue;
        }
        return;
      }
    },
    hasDrainAck() {
      return events.some(event => event.event === 'drained');
    },
    cleanupSubmissionCount() {
      return submissions.filter(item => item.cleanup).length;
    },
    luaCleanupRan() {
      return luaCalls.some(item => item.cleanup);
    },
    start(runId = '123456789') {
      return sandbox.rpc.exports.start('/cache/tick.lua', runId);
    },
    stop() {
      return sandbox.rpc.exports.stop('/cache/cleanup.lua');
    }
  };

  class FakeFutureTask {
    constructor(runnable) {
      this.runnable = runnable;
      this.done = false;
      this.cancelled = false;
      this.running = false;
      this.error = null;
      this.cleanup = Boolean(runnable.cleanup && runnable.cleanup.value);
      this.kind = this.cleanup ? 'cleanup' : 'tick';
    }
    run() {
      if (this.done || this.cancelled) return;
      this.running = true;
      try {
        try {
          this.runnable.run();
        } catch (error) {
          this.error = error;
        }
        if (runtime.beforeFutureDone) runtime.beforeFutureDone(this);
      } finally {
        this.running = false;
        this.done = true;
      }
    }
    isDone() { return this.done; }
    isCancelled() { return this.cancelled; }
    get() {
      if (!this.done) throw new Error('get called before FutureTask completed');
      if (this.error) throw this.error;
      return null;
    }
  }

  const helper = {
    runOnGLThread(task) {
      const cleanup = Boolean(task.cleanup || (task.runnable && task.runnable.cleanup && task.runnable.cleanup.value));
      submissions.push({ task, cleanup });
      glQueue.push(task);
    }
  };
  const luaBridge = {
    callLuaGlobalFunctionWithString(_functionName, scriptPath) {
      const cleanup = String(scriptPath).includes('cleanup.lua');
      luaCalls.push({ cleanup, scriptPath: String(scriptPath) });
      if (cleanup) runtime.luaStatus = runtime.cleanupResult === '0' ? 'stopped' : 'stopping';
      return cleanup ? runtime.cleanupResult : '0';
    }
  };
  const Runnable = {};
  const javaBridge = {
    perform(callback) { performQueue.push(callback); },
    use(className) {
      if (className === 'java.lang.Runnable') return Runnable;
      if (className === 'java.util.UUID') {
        return { randomUUID: () => ({ toString: () => '12345678-1234-1234-1234-123456789012' }) };
      }
      if (className === 'java.util.concurrent.FutureTask') {
        return { $new: runnable => new FakeFutureTask(runnable) };
      }
      if (className === 'org.cocos2dx.lib.Cocos2dxHelper') return helper;
      if (className === 'org.cocos2dx.lib.Cocos2dxLuaJavaBridge') return luaBridge;
      throw new Error(`unexpected Java class ${className}`);
    },
    registerClass(spec) {
      return {
        $new(...args) {
          const instance = { path: { value: null }, cleanup: { value: false }, queuedAt: { value: 0 } };
          spec.methods.$init[0].implementation.apply(instance, args);
          instance.run = spec.methods.run.bind(instance);
          return instance;
        }
      };
    }
  };
  const rpc = { exports: {} };
  const sandbox = {
    Date: class extends Date { static now() { return clock; } },
    Java: javaBridge,
    frida_java_bridge_default: javaBridge,
    rpc,
    recv() {},
    send(event) { events.push(JSON.parse(JSON.stringify(event))); },
    setTimeout(fn, delay = 0) {
      const id = ++timerId;
      timeouts.set(id, { fn, at: clock + Number(delay || 0) });
      return id;
    },
    clearTimeout(id) { timeouts.delete(id); },
    setInterval(fn, delay = 0) {
      const id = ++timerId;
      intervals.set(id, { fn, delay: Number(delay || 0), at: clock + Number(delay || 0) });
      return id;
    },
    clearInterval(id) { intervals.delete(id); }
  };
  vm.runInNewContext(agentTail, sandbox, { filename: path.basename(agentPath) + '#agent-tail' });
  return runtime;
}

test('a queued tick cannot emit drained before it runs and completes', () => {
  const runtime = createRuntime();
  assert.equal(runtime.start().started, true);
  runtime.pump({ runGl: false });
  assert.equal(runtime.glQueue.length, 1);

  runtime.stop();
  runtime.pump({ runGl: false });
  assert.equal(runtime.hasDrainAck(), false);
  assert.equal(runtime.glQueue.length, 1, 'the already queued tick must remain tracked');

  runtime.pump();
  assert.equal(runtime.hasDrainAck(), true);
});

test('cleanup result is not acknowledged while the Java Runnable is still on its stack', () => {
  const runtime = createRuntime();
  assert.equal(runtime.start().started, true);
  runtime.pump();
  runtime.beforeFutureDone = task => {
    if (task.kind !== 'cleanup') return;
    assert.equal(runtime.luaStatus, 'stopped', 'the cleanup Lua call has returned a stopped result');
    assert.equal(runtime.hasDrainAck(), false, 'FutureTask has not completed yet');
  };

  runtime.stop();
  runtime.pump();
  assert.equal(runtime.hasDrainAck(), true);
});

test('repeated stop requests submit exactly one cleanup task', () => {
  const runtime = createRuntime();
  assert.equal(runtime.start().started, true);
  runtime.pump({ runGl: false });
  runtime.stop();
  runtime.stop();
  runtime.pump();

  assert.equal(runtime.cleanupSubmissionCount(), 1);
  assert.equal(runtime.hasDrainAck(), true);

  runtime.stop();
  runtime.pump();
  assert.equal(runtime.cleanupSubmissionCount(), 1, 'an acknowledged duplicate stop must not enqueue cleanup again');
});

test('a timed out queued tick still drains after it eventually returns', () => {
  const runtime = createRuntime();
  assert.equal(runtime.start().started, true);
  runtime.pump({ runGl: false });
  runtime.advance(3001);
  assert.ok(runtime.events.some(event => event.event === 'fatal' && event.error === 'gl_thread_queue_timeout'));

  runtime.stop();
  runtime.pump();
  assert.equal(runtime.hasDrainAck(), true);
  assert.equal(runtime.cleanupSubmissionCount(), 1);
});

test('stop before the pending Java.perform start prevents late activation and drains', () => {
  const runtime = createRuntime();
  assert.equal(runtime.start().started, true);
  runtime.stop();
  runtime.pump();

  assert.equal(runtime.intervals.size, 0, 'a pending start must not create the tick interval');
  assert.equal(runtime.events.some(event => event.event === 'started'), false);
  assert.equal(runtime.luaCleanupRan(), true, 'stop-before-start must execute Lua cleanup before acknowledging');
  assert.equal(runtime.luaStatus, 'stopped');
  assert.equal(runtime.hasDrainAck(), true);
  assert.equal(runtime.cleanupSubmissionCount(), 1);
});

test('cleanup failure never emits a drained acknowledgement', () => {
  const runtime = createRuntime();
  assert.equal(runtime.start().started, true);
  runtime.pump();
  runtime.cleanupResult = '-1';
  runtime.stop();
  runtime.pump();

  assert.equal(runtime.luaStatus, 'stopping');
  assert.equal(runtime.hasDrainAck(), false);
});
