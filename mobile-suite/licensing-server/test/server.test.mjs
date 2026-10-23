import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { Worker } from 'node:worker_threads';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { request as httpRequest } from 'node:http';
import { createLicensingServer } from '../server.mjs';

const adminToken = 'test-admin-token-that-is-long-enough-123456';
const bridgeToken = 'test-bridge-token-that-is-long-enough-123456';
const installA = Buffer.alloc(32, 0xaa).toString('base64url'), installB = Buffer.alloc(32, 0xbb).toString('base64url');
function fixture(options = {}) {
  const dir = mkdtempSync(path.join(tmpdir(), 'piaomiao-license-'));
  let clock = 1_800_000_000_000;
  const apps = [];
  const make = () => { const app = createLicensingServer({ dbPath: path.join(dir, 'data.sqlite'), adminToken, bridgeToken, now: () => clock, ...options }); apps.push(app); return app; };
  return { dir, make, time: () => clock, advance: ms => { clock += ms; }, cleanup: () => { for (const app of apps) { try { app.server.close(); } catch {} try { app.close(); } catch {} } rmSync(dir, { recursive: true, force: true, maxRetries: 5, retryDelay: 50 }); } };
}
function newCode(app, durationDays = 1) { return app.admin('generate', { count: 1, durationDays }).codes[0]; }
const activation = (code, installationId = installA) => ({ code, installationId, product: 'piaomiao-suite' });
async function fakeRequest(handler, { method = 'POST', url = '/v1/activate', peer = '198.51.100.10', local = '127.0.0.1', rawHeaders = [], headers = {}, body } = {}) {
  const request = {
    method, url, socket: { remoteAddress: peer, localAddress: local }, rawHeaders, headers,
    setTimeout() {},
    async *[Symbol.asyncIterator]() { if (body !== undefined) yield Buffer.from(JSON.stringify(body)); }
  };
  const response = { headersSent: false, writeHead(status) { this.status = status; this.headersSent = true; }, end(value) { this.body = value; } };
  await handler(request, response);
  return { status: response.status, body: response.body ? JSON.parse(response.body) : undefined };
}

async function rawPost(url, body, headers = {}) {
  const serialized = JSON.stringify(body);
  return new Promise((resolve, reject) => {
    const request = httpRequest(url, { method: 'POST', headers: { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(serialized), ...headers } }, response => {
      response.resume(); response.on('end', () => resolve(response.statusCode));
    });
    request.on('error', reject); request.end(serialized);
  });
}

test('同码重复激活保持到期时间，跨安装冲突', t => {
  const f = fixture(); t.after(f.cleanup); const app = f.make(); t.after(app.close);
  const code = newCode(app);
  const results = [app.activate(activation(code), '127.0.0.1'), app.activate(activation(code), '127.0.0.1')];
  assert.equal(results[0].expiresAt, results[1].expiresAt);
  assert.notEqual(results[0].sessionToken, results[1].sessionToken);
  assert.throws(() => app.activate(activation(code, installB), '127.0.0.1'), { status: 409 });
  assert.equal(app.verify({ sessionToken: results[0].sessionToken, installationId: installA, product: 'piaomiao-suite' }).expiresAt, results[0].expiresAt);
  assert.equal(app.verify({ sessionToken: results[1].sessionToken, installationId: installA, product: 'piaomiao-suite' }).expiresAt, results[0].expiresAt);
});

test('HTTP 并发重复激活保留两个 token，解绑与吊销都撤销该码的全部会话', async t => {
  const f = fixture(); t.after(f.cleanup); const app = f.make(), code = newCode(app);
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${app.server.address().port}`;
  t.after(() => new Promise(resolve => app.server.close(() => { app.close(); resolve(); })));
  const post = async (route, body) => {
    const response = await fetch(base + route, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    return { status: response.status, body: await response.json() };
  };
  const issued = await Promise.all([post('/v1/activate', activation(code)), post('/v1/activate', activation(code))]);
  assert.deepEqual(issued.map(x => x.status), [200, 200]);
  const verifyBody = item => ({ sessionToken: item.body.sessionToken, installationId: installA, product: 'piaomiao-suite' });
  assert.deepEqual((await Promise.all(issued.map(item => post('/v1/verify', verifyBody(item))))).map(x => x.status), [200, 200]);
  assert.equal((await post('/v1/activate', activation(code, installB))).status, 409);
  const id = app.admin('list').licenses[0].id;
  app.admin('unbind', { id });
  assert.deepEqual((await Promise.all(issued.map(item => post('/v1/verify', verifyBody(item))))).map(x => x.status), [401, 401]);
  const rebound = await post('/v1/activate', activation(code, installB)); assert.equal(rebound.status, 200);
  app.admin('revoke', { id });
  assert.equal((await post('/v1/verify', { sessionToken: rebound.body.sessionToken, installationId: installB, product: 'piaomiao-suite' })).status, 401);
});

test('HTTP 接受 Android 发送的无连字符小写激活码并可验证', async t => {
  const f = fixture(); t.after(f.cleanup); const app = f.make();
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  t.after(() => new Promise(resolve => app.server.close(() => { app.close(); resolve(); })));
  const base = `http://127.0.0.1:${app.server.address().port}`;
  const code = newCode(app);
  const issued = await fetch(base + '/v1/activate', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(activation(code.replaceAll('-', '').toLowerCase())) });
  assert.equal(issued.status, 200);
  const result = await issued.json();
  const verified = await fetch(base + '/v1/verify', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ sessionToken: result.sessionToken, installationId: installA, product: 'piaomiao-suite' }) });
  assert.equal(verified.status, 200);
  const spaced = await fetch(base + '/v1/activate', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(activation(` ${code.replaceAll('-', ' ').toLowerCase()} `)) });
  assert.equal(spaced.status, 200);
  const invalid = await fetch(base + '/v1/activate', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(activation(`\t${code.replaceAll('-', '')}`)) });
  assert.equal(invalid.status, 401);
});

test('默认不信任伪造的 X-Real-IP，可信 loopback proxy 按单一 IP 分桶并拒绝非法头', async t => {
  const f = fixture({ rateLimit: 1 }); t.after(f.cleanup); const app = f.make();
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  t.after(() => new Promise(resolve => app.server.close(() => { app.close(); resolve(); })));
  const base = `http://127.0.0.1:${app.server.address().port}/v1/activate`;
  const unknown = activation('A'.repeat(32));
  assert.equal(await rawPost(base, unknown, { 'X-Real-IP': '198.51.100.1' }), 401);
  assert.equal(await rawPost(base, { ...unknown, installationId: installB }, { 'X-Real-IP': '203.0.113.2' }), 429);

  const previous = process.env.PIAOMIAO_TRUST_PROXY;
  process.env.PIAOMIAO_TRUST_PROXY = '1';
  let trusted;
  try { trusted = f.make(); } finally { if (previous === undefined) delete process.env.PIAOMIAO_TRUST_PROXY; else process.env.PIAOMIAO_TRUST_PROXY = previous; }
  await new Promise(resolve => trusted.server.listen(0, '127.0.0.1', resolve));
  t.after(() => new Promise(resolve => trusted.server.close(() => { trusted.close(); resolve(); })));
  const trustedBase = `http://127.0.0.1:${trusted.server.address().port}/v1/activate`;
  assert.equal(await rawPost(trustedBase, unknown), 400);
  assert.equal(await rawPost(trustedBase, unknown, { 'X-Real-IP': '198.51.100.1, 203.0.113.1' }), 400);
  assert.equal(await rawPost(trustedBase, unknown, { 'X-Real-IP': ['198.51.100.1', '203.0.113.1'] }), 400);
  assert.equal(await rawPost(trustedBase, unknown, { 'X-Real-IP': 'not-an-ip' }), 400);
  assert.equal(await rawPost(trustedBase, unknown, { 'X-Real-IP': '198.51.100.1' }), 401);
  assert.equal(await rawPost(trustedBase, { ...unknown, installationId: installB }, { 'X-Real-IP': '203.0.113.1' }), 401);
  assert.equal(await rawPost(trustedBase, unknown, { 'X-Real-IP': '198.51.100.1' }), 429);

  const admin = await fetch(`http://127.0.0.1:${trusted.server.address().port}/admin/list`, { headers: {
    Authorization: `Bearer ${adminToken}`, Origin: `http://127.0.0.1:${trusted.server.address().port}`, 'X-Real-IP': '198.51.100.8'
  } });
  assert.equal(admin.status, 200);
});

test('可信代理模式下非 loopback peer 不采纳 X-Real-IP', async t => {
  const previous = process.env.PIAOMIAO_TRUST_PROXY;
  process.env.PIAOMIAO_TRUST_PROXY = '1';
  const f = fixture({ rateLimit: 1 }); t.after(f.cleanup); let app;
  try { app = f.make(); } finally { if (previous === undefined) delete process.env.PIAOMIAO_TRUST_PROXY; else process.env.PIAOMIAO_TRUST_PROXY = previous; }
  t.after(app.close);
  const request = (ip, installationId) => fakeRequest(app.handler, {
    peer: '198.51.100.10', rawHeaders: ['X-Real-IP', ip], headers: {}, body: activation('A'.repeat(32), installationId)
  });
  assert.equal((await request('203.0.113.1', installA)).status, 401);
  assert.equal((await request('203.0.113.2', installB)).status, 429);
});

test('两个 SQLite 连接并发争抢同一激活码时只绑定一个安装', async t => {
  const f = fixture(); t.after(f.cleanup); const app = f.make(), code = newCode(app);
  const workerSource = `const { parentPort, workerData } = require('node:worker_threads');
    import(workerData.serverUrl).then(({createLicensingServer}) => {
      const app = createLicensingServer({dbPath:workerData.dbPath,adminToken:'${adminToken}',now:()=>1800000000000});
      try { app.activate({code:workerData.code,installationId:workerData.installationId,product:'piaomiao-suite'}); parentPort.postMessage(200); }
      catch (error) { parentPort.postMessage(error.status || 500); }
      finally { app.close(); }
    });`;
  const run = installationId => new Promise((resolve, reject) => {
    const worker = new Worker(workerSource, { eval: true, workerData: { serverUrl: pathToFileURL(path.resolve('server.mjs')).href, dbPath: path.join(f.dir, 'data.sqlite'), code, installationId } });
    worker.once('message', resolve); worker.once('error', reject);
  });
  const statuses = await Promise.all([run(installA), run(installB)]);
  assert.deepEqual(statuses.sort(), [200, 409]);
  app.close();
});

test('重启保留计时与会话，verify 不轮换 token', t => {
  const f = fixture(); t.after(f.cleanup); let app = f.make(); const code = newCode(app);
  const issued = app.activate(activation(code)); app.close(); app = f.make(); t.after(app.close);
  const verified = app.verify({ sessionToken: issued.sessionToken, installationId: installA, product: 'piaomiao-suite' });
  assert.equal(verified.sessionToken, issued.sessionToken); assert.equal(verified.expiresAt, issued.expiresAt);
  assert.equal(app.activate(activation(code)).expiresAt, issued.expiresAt);
});

test('过期与撤销会使已有会话失效', t => {
  const f = fixture(); t.after(f.cleanup); const app = f.make(); t.after(app.close);
  const code = newCode(app), issued = app.activate(activation(code));
  f.advance(86_400_000);
  assert.throws(() => app.verify({ sessionToken: issued.sessionToken, installationId: installA, product: 'piaomiao-suite' }), { status: 403, code: 'LICENSE_EXPIRED' });
  assert.throws(() => app.activate(activation(code)), { status: 403 });
  const second = newCode(app); const session = app.activate(activation(second));
  const id = app.admin('list').licenses.find(x => x.expiresAt === session.expiresAt).id;
  app.admin('revoke', { id });
  assert.throws(() => app.verify({ sessionToken: session.sessionToken, installationId: installA, product: 'piaomiao-suite' }), { status: 401 });
  assert.throws(() => app.activate(activation(second)), { status: 401 });
});

test('到期会话保留 30 天供续期恢复，超窗清理后无法恢复', t => {
  const f = fixture(); t.after(f.cleanup); const app = f.make(); t.after(app.close);
  const code = newCode(app), issued = app.activate(activation(code));
  const id = app.admin('list').licenses[0].id;
  f.advance(86_400_000);
  assert.throws(() => app.verify({ sessionToken: issued.sessionToken, installationId: installA, product: 'piaomiao-suite' }), { status: 403, code: 'LICENSE_EXPIRED' });
  app.admin('extend', { id, days: 2 });
  assert.equal(app.verify({ sessionToken: issued.sessionToken, installationId: installA, product: 'piaomiao-suite' }).sessionToken, issued.sessionToken);
  f.advance(3 * 86_400_000);
  assert.throws(() => app.verify({ sessionToken: issued.sessionToken, installationId: installA, product: 'piaomiao-suite' }), { status: 403, code: 'LICENSE_EXPIRED' });
  f.advance(30 * 86_400_000 + 1);
  assert.throws(() => app.verify({ sessionToken: issued.sessionToken, installationId: installA, product: 'piaomiao-suite' }), { status: 401 });
  app.admin('extend', { id, days: 2 });
  assert.throws(() => app.verify({ sessionToken: issued.sessionToken, installationId: installA, product: 'piaomiao-suite' }), { status: 401 });
});

test('吊销和解绑令牌不返回过期恢复码，HTTP 到期响应提供 LICENSE_EXPIRED', async t => {
  const f = fixture(); t.after(f.cleanup); const app = f.make();
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  t.after(() => new Promise(resolve => app.server.close(() => { app.close(); resolve(); })));
  const base = `http://127.0.0.1:${app.server.address().port}`;
  const post = async (route, body) => {
    const response = await fetch(base + route, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    return { status: response.status, body: await response.json() };
  };
  const [expiredCode, unbindCode, revokeCode] = app.admin('generate', { count: 3, durationDays: 1 }).codes;
  const expired = app.activate(activation(expiredCode));
  const unbound = app.activate(activation(unbindCode));
  const revoked = app.activate(activation(revokeCode));
  const codeId = value => createHash('sha256').update(value.replaceAll('-', '')).digest('hex');
  const unbindId = codeId(unbindCode), revokeId = codeId(revokeCode);
  f.advance(86_400_000);
  const response = await post('/v1/verify', { sessionToken: expired.sessionToken, installationId: installA, product: 'piaomiao-suite' });
  assert.equal(response.status, 403);
  assert.deepEqual(response.body, { error: '授权已过期', code: 'LICENSE_EXPIRED' });
  app.admin('unbind', { id: unbindId });
  app.admin('revoke', { id: revokeId });
  app.admin('extend', { id: unbindId, days: 2 });
  app.admin('extend', { id: revokeId, days: 2 });
  assert.equal((await post('/v1/verify', { sessionToken: unbound.sessionToken, installationId: installA, product: 'piaomiao-suite' })).status, 401);
  assert.equal((await post('/v1/verify', { sessionToken: revoked.sessionToken, installationId: installA, product: 'piaomiao-suite' })).status, 401);
});

test('解绑撤销旧会话并保留首次计时，续期按剩余到期或当前时间增加', t => {
  const f = fixture(); t.after(f.cleanup); const app = f.make(); t.after(app.close);
  const code = newCode(app), session = app.activate(activation(code));
  const id = app.admin('list').licenses[0].id;
  app.admin('unbind', { id });
  assert.throws(() => app.verify({ sessionToken: session.sessionToken, installationId: installA, product: 'piaomiao-suite' }), { status: 401 });
  const rebound = app.activate(activation(code, installB)); assert.equal(rebound.expiresAt, session.expiresAt);
  app.admin('extend', { id, days: 3 });
  assert.equal(app.verify({ sessionToken: rebound.sessionToken, installationId: installB, product: 'piaomiao-suite' }).expiresAt, session.expiresAt + 3 * 86_400_000);
});

test('未激活续期只调整初始时长', t => {
  const f = fixture(); t.after(f.cleanup); const app = f.make(); t.after(app.close);
  const code = newCode(app, 2), id = app.admin('list').licenses[0].id;
  app.admin('extend', { id, days: 5 });
  const issued = app.activate(activation(code)); assert.equal(issued.expiresAt, f.time() + 7 * 86_400_000);
});

test('管理列表返回与客户端一致的 12 位安装识别码前缀但不返回安装凭证', t => {
  const f = fixture(); t.after(f.cleanup); const app = f.make(); t.after(app.close);
  const [codeA, codeB] = app.admin('generate', { count: 3, durationDays: 30 }).codes;
  const sessionA = app.activate(activation(codeA, installA));
  const sessionB = app.activate(activation(codeB, installB));
  const records = app.admin('list').licenses;
  const hintA = createHash('sha256').update(installA, 'utf8').digest('hex').slice(0, 12).toUpperCase();
  const hintB = createHash('sha256').update(installB, 'utf8').digest('hex').slice(0, 12).toUpperCase();
  assert.equal(records.find(x => x.expiresAt === sessionA.expiresAt && x.installationHint === hintA).installationHint, hintA);
  assert.equal(records.find(x => x.expiresAt === sessionB.expiresAt && x.installationHint === hintB).installationHint, hintB);
  assert.equal(records.find(x => x.installationHint === '').bound, false);
  const serialized = JSON.stringify(records);
  assert.equal(serialized.includes(installA), false);
  assert.equal(serialized.includes(installB), false);
  assert.equal(serialized.includes(sessionA.sessionToken), false);
  assert.equal(serialized.includes(sessionB.sessionToken), false);
});

test('限流、类型和激活码格式边界明确', t => {
  const f = fixture({ rateLimit: 3 }); t.after(f.cleanup); const app = f.make(); t.after(app.close);
  const code = newCode(app);
  assert.throws(() => app.activate({ ...activation(code), installationId: [] }), { status: 400 });
  assert.throws(() => app.activate(activation('bad')), { status: 401 });
  app.activate(activation(code));
  assert.throws(() => app.verify({ sessionToken: 'x', installationId: installA, product: 'wrong' }), { status: 429 });
});

test('管理接口认证且仅监听 loopback，管理请求不能匿名调用', async t => {
  const f = fixture(); t.after(f.cleanup); const app = f.make();
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve)); t.after(() => new Promise(resolve => app.server.close(() => { app.close(); resolve(); })));
  const base = `http://127.0.0.1:${app.server.address().port}`;
  const denied = await fetch(base + '/admin/list'); assert.equal(denied.status, 401);
  const allowed = await fetch(base + '/admin/list', { headers: { Authorization: `Bearer ${adminToken}`, Origin: base } }); assert.equal(allowed.status, 200);
  const wrongOrigin = await fetch(base + '/admin/list', { headers: { Authorization: `Bearer ${adminToken}`, Origin: 'https://evil.example' } }); assert.equal(wrongOrigin.status, 403);
  assert.equal(app.server.address().address, '127.0.0.1');
});

test('Sites 桥接端以一次事务导入授权与会话，拒绝重复导入并保留有效租约', async t => {
  const f = fixture(); t.after(f.cleanup); const app = f.make();
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  t.after(() => new Promise(resolve => app.server.close(() => { app.close(); resolve(); })));
  const base = `http://127.0.0.1:${app.server.address().port}`;
  const codeHash = createHash('sha256').update('synthetic-license-hash').digest('hex');
  const installHash = createHash('sha256').update(installA).digest('hex');
  const sessionToken = Buffer.alloc(32, 0x4a).toString('base64url');
  const tokenHash = createHash('sha256').update(sessionToken).digest('hex');
  const now = f.time();
  const snapshot = { version: 1, licenses: [{
    code_hash: codeHash, product: 'piaomiao-suite', duration_days: 30, created_at: now - 1000,
    first_activated_at: now - 500, expires_at: now + 30 * 86_400_000, installation_hash: installHash,
    binding_generation: 4, revoked_at: null, bound_at: now - 500
  }], sessions: [{
    token_hash: tokenHash, code_hash: codeHash, installation_hash: installHash,
    binding_generation: 4, created_at: now - 400, invalidated_at: null
  }] };
  const headers = { Authorization: `Bearer ${bridgeToken}`, 'Content-Type': 'application/json' };
  const imported = await fetch(`${base}/bridge/license/import`, { method: 'POST', headers, body: JSON.stringify(snapshot) });
  assert.equal(imported.status, 200);
  assert.deepEqual(await imported.json(), { licensesImported: 1, sessionsImported: 1 });
  const repeated = await fetch(`${base}/bridge/license/import`, { method: 'POST', headers, body: JSON.stringify(snapshot) });
  assert.equal(repeated.status, 409);
  const verify = await fetch(`${base}/bridge/license/verify`, { method: 'POST', headers, body: JSON.stringify({ sessionToken, installationId: installA, product: 'piaomiao-suite' }) });
  assert.equal(verify.status, 200);
  assert.equal((await verify.json()).sessionToken, sessionToken);
  const list = await fetch(`${base}/bridge/license/admin/list`, { headers: { Authorization: `Bearer ${bridgeToken}` } });
  assert.equal(list.status, 200);
  assert.equal((await list.json()).licenses.length, 1);
});

test('Sites 桥接拒绝缺失的 secret，导入的旧 generation 会话不能续验', async t => {
  const f = fixture(); t.after(f.cleanup); const app = f.make();
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  t.after(() => new Promise(resolve => app.server.close(() => { app.close(); resolve(); })));
  const base = `http://127.0.0.1:${app.server.address().port}`;
  const rejected = await fetch(`${base}/bridge/license/admin/list`);
  assert.equal(rejected.status, 401);
  const codeHash = createHash('sha256').update('synthetic-stale-license').digest('hex');
  const installHash = createHash('sha256').update(installA).digest('hex');
  const sessionToken = Buffer.alloc(32, 0x5b).toString('base64url');
  const now = f.time();
  app.importSnapshot({ version: 1, licenses: [{ code_hash: codeHash, product: 'piaomiao-suite', duration_days: 30, created_at: now,
    first_activated_at: now, expires_at: now + 30 * 86_400_000, installation_hash: installHash, binding_generation: 2, revoked_at: null, bound_at: now }],
    sessions: [{ token_hash: createHash('sha256').update(sessionToken).digest('hex'), code_hash: codeHash, installation_hash: installHash,
      binding_generation: 1, created_at: now, invalidated_at: null }] });
  assert.throws(() => app.verify({ sessionToken, installationId: installA, product: 'piaomiao-suite' }), { status: 401 });
});

test('HTTP body 大小与请求类型受限', async t => {
  const f = fixture(); t.after(f.cleanup); const app = f.make();
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve)); t.after(() => new Promise(resolve => app.server.close(() => { app.close(); resolve(); })));
  const base = `http://127.0.0.1:${app.server.address().port}`;
  const bad = await fetch(base + '/v1/activate', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '[]' }); assert.equal(bad.status, 400);
  const large = await fetch(base + '/v1/activate', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ x: 'x'.repeat(9000) }) }); assert.equal(large.status, 413);
});

test('授权哈希表不含原码，时限始终毫秒与整秒租约', t => {
  const f = fixture(); t.after(f.cleanup); const app = f.make(); t.after(app.close);
  const code = newCode(app), issued = app.activate(activation(code));
  assert.equal(issued.leaseSeconds, 120); assert.equal(Number.isInteger(issued.expiresAt), true); assert.equal(Number.isInteger(issued.serverTime), true);
  const rows = app.admin('list'); assert.equal(JSON.stringify(rows).includes(code), false);
});

test('不足一秒的剩余授权不发放租约，总授权条目数有界', t => {
  const f = fixture({ maxCodes: 1 }); t.after(f.cleanup); const app = f.make(); t.after(app.close);
  const code = newCode(app), issued = app.activate(activation(code));
  assert.throws(() => app.admin('generate', { count: 1, durationDays: 1 }), { status: 409 });
  f.advance(86_400_000 - 500);
  assert.throws(() => app.verify({ sessionToken: issued.sessionToken, installationId: installA, product: 'piaomiao-suite' }), { status: 403 });
});
