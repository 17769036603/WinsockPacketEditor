import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createLicensingServer } from '../../licensing-server/server.mjs';

const DAY = 86_400_000;
const adminToken = 'activation-integration-admin-token-123456';
const installationId = Buffer.alloc(32, 0xa7).toString('base64url');
const dir = mkdtempSync(path.join(tmpdir(), 'activation-expiry-renewal-'));
let now = 1_800_000_000_000;
const app = createLicensingServer({ dbPath: path.join(dir, 'licenses.sqlite'), adminToken, now: () => now });
const seenIds = new Set();

try {
  await new Promise(resolve => app.server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${app.server.address().port}`;
  const adminHeaders = {
    authorization: `Bearer ${adminToken}`,
    origin: base,
    'content-type': 'application/json'
  };
  const post = async (route, body, headers = {}) => {
    const response = await fetch(base + route, {
      method: 'POST',
      headers: { 'content-type': 'application/json', ...headers },
      body: JSON.stringify(body)
    });
    return { status: response.status, body: await response.json() };
  };
  const generate = async () => {
    const result = await post('/admin/generate', { count: 1, durationDays: 1 }, adminHeaders);
    assert.equal(result.status, 201);
    const rows = await (await fetch(base + '/admin/list', { headers: adminHeaders })).json();
    const row = rows.licenses.find(item => !seenIds.has(item.id));
    assert.ok(row, 'new generated license appears in admin list');
    seenIds.add(row.id);
    return { code: result.body.codes[0], id: row.id };
  };
  const activate = async code => {
    const result = await post('/v1/activate', { code, installationId, product: 'piaomiao-suite' });
    assert.equal(result.status, 200);
    return result.body;
  };
  const verify = sessionToken => post('/v1/verify', { sessionToken, installationId, product: 'piaomiao-suite' });
  const adminAction = (action, body) => post(`/admin/${action}`, body, adminHeaders);

  const renewable = await generate();
  const renewableSession = await activate(renewable.code);
  now += 31 * DAY;
  const expired = await verify(renewableSession.sessionToken);
  assert.equal(expired.status, 403);
  assert.equal(expired.body.code, 'LICENSE_EXPIRED');
  assert.equal((await adminAction('extend', { id: renewable.id, days: 30 })).status, 200);
  const restored = await verify(renewableSession.sessionToken);
  assert.equal(restored.status, 200);
  assert.equal(restored.body.sessionToken, renewableSession.sessionToken);
  console.log('31-day expired session extends and recovers with same token: PASS');

  const beyondWindow = await generate();
  const beyondWindowSession = await activate(beyondWindow.code);
  now += 31 * DAY + 1;
  assert.equal((await verify(beyondWindowSession.sessionToken)).status, 401);
  console.log('expired session past 30-day retention returns 401: PASS');

  const unbound = await generate();
  const unboundSession = await activate(unbound.code);
  const revoked = await generate();
  const revokedSession = await activate(revoked.code);
  now += 31 * DAY;
  assert.equal((await verify(unboundSession.sessionToken)).status, 403);
  assert.equal((await verify(revokedSession.sessionToken)).status, 403);
  assert.equal((await adminAction('unbind', { id: unbound.id })).status, 200);
  assert.equal((await adminAction('revoke', { id: revoked.id })).status, 200);
  assert.equal((await adminAction('extend', { id: unbound.id, days: 30 })).status, 200);
  assert.equal((await adminAction('extend', { id: revoked.id, days: 30 })).status, 200);
  assert.equal((await verify(unboundSession.sessionToken)).status, 401);
  assert.equal((await verify(revokedSession.sessionToken)).status, 401);
  console.log('unbind and revoke prevent expired token recovery after extension: PASS');
} finally {
  await new Promise(resolve => app.server.close(resolve));
  app.close();
  rmSync(dir, { recursive: true, force: true, maxRetries: 5, retryDelay: 50 });
}
