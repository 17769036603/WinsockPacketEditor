import { createServer as httpServer } from 'node:http';
import { randomBytes, createHash, timingSafeEqual } from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';
import { readFile } from 'node:fs/promises';
import { mkdirSync } from 'node:fs';
import { isIP } from 'node:net';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const PRODUCT = 'piaomiao-suite';
const MAX_BODY = 8192;
const MAX_IMPORT_BODY = 256 * 1024;
const MAX_CODES = 100_000;
const B32 = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
const here = path.dirname(fileURLToPath(import.meta.url));
const hash = (value) => createHash('sha256').update(value).digest('hex');
const token = () => randomBytes(32).toString('base64url');
const code = () => {
  let bits = BigInt(`0x${randomBytes(20).toString('hex')}`), out = '';
  for (let i = 0; i < 32; i++) { out = B32[Number(bits & 31n)] + out; bits >>= 5n; }
  return out.match(/.{1,4}/g).join('-');
};
const normalizeCode = (value) => {
  if (typeof value !== 'string') return null;
  const normalized = value.replace(/[ -]/g, '').toUpperCase();
  return /^[A-HJ-NP-Z2-9]{32}$/.test(normalized) ? normalized : null;
};
const isLoopbackIp = (address) => {
  const version = isIP(address);
  if (version === 4) return address.startsWith('127.');
  if (version === 6) return address.toLowerCase() === '::1' || /^::ffff:127(?:\.\d{1,3}){3}$/i.test(address);
  return false;
};
const clientIp = (req, trustProxy) => {
  const peer = req.socket.remoteAddress ?? '';
  if (isIP(peer) === 0) throw fail(400, '客户端地址无效');
  if (!trustProxy || !isLoopbackIp(peer)) return peer;
  const forwardedValues = [];
  const headers = req.rawHeaders ?? [];
  for (let i = 0; i + 1 < headers.length; i += 2) {
    if (String(headers[i]).toLowerCase() === 'x-real-ip') forwardedValues.push(headers[i + 1]);
  }
  if (forwardedValues.length !== 1 || typeof forwardedValues[0] !== 'string') throw fail(400, '代理客户端地址无效');
  const forwarded = forwardedValues[0];
  if (forwarded !== forwarded.trim() || isIP(forwarded) === 0) throw fail(400, '代理客户端地址无效');
  return forwarded;
};
const validInstallationId = (value) => typeof value === 'string' && /^[A-Za-z0-9_-]{43}$/.test(value) && Buffer.from(value, 'base64url').length === 32 && Buffer.from(value, 'base64url').toString('base64url') === value;
const safeEqual = (a, b) => timingSafeEqual(createHash('sha256').update(String(a ?? '')).digest(), createHash('sha256').update(String(b ?? '')).digest());
const fail = (status, message, code) => Object.assign(new Error(message), { status, code });

export function createLicensingServer({ dbPath = process.env.PIAOMIAO_DB_PATH ?? path.join(here, 'data', 'licenses.sqlite'), adminToken = process.env.PIAOMIAO_ADMIN_TOKEN, bridgeToken = process.env.PIAOMIAO_BRIDGE_TOKEN, now = Date.now, maxCodes = MAX_CODES, rateLimit = 120, rateWindowMs = 60_000 } = {}) {
  if (!adminToken || adminToken.length < 24) throw new Error('PIAOMIAO_ADMIN_TOKEN must contain at least 24 characters');
  mkdirSync(path.dirname(dbPath), { recursive: true });
  const db = new DatabaseSync(dbPath);
  db.exec(`PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;
    CREATE TABLE IF NOT EXISTS licenses (
      code_hash TEXT PRIMARY KEY, duration_days INTEGER NOT NULL, created_at INTEGER NOT NULL,
      first_activated_at INTEGER, expires_at INTEGER, installation_hash TEXT,
      revoked_at INTEGER, bound_at INTEGER, binding_generation INTEGER NOT NULL DEFAULT 0
    ) STRICT;
    CREATE TABLE IF NOT EXISTS sessions (
      token_hash TEXT PRIMARY KEY, code_hash TEXT NOT NULL REFERENCES licenses(code_hash),
      installation_hash TEXT NOT NULL, created_at INTEGER NOT NULL, invalidated_at INTEGER,
      binding_generation INTEGER NOT NULL DEFAULT 0
    ) STRICT;
    CREATE INDEX IF NOT EXISTS sessions_license ON sessions(code_hash);
  `);
  const columns = (table) => new Set(db.prepare(`PRAGMA table_info(${table})`).all().map(column => column.name));
  if (!columns('licenses').has('binding_generation')) db.exec('ALTER TABLE licenses ADD COLUMN binding_generation INTEGER NOT NULL DEFAULT 0');
  if (!columns('sessions').has('binding_generation')) db.exec('ALTER TABLE sessions ADD COLUMN binding_generation INTEGER NOT NULL DEFAULT 0');
  const rates = new Map();
  const trustProxy = process.env.PIAOMIAO_TRUST_PROXY === '1';
  const bumpRate = (key) => {
    const time = now(); let item = rates.get(key);
    if (!item || time - item.start >= rateWindowMs) item = { start: time, count: 0 };
    if (!rates.has(key) && rates.size >= 20_000) {
      for (const [k, v] of rates) if (time - v.start >= rateWindowMs) rates.delete(k);
      if (rates.size >= 20_000) throw fail(429, '请求过于频繁');
    }
    item.count++;
    rates.set(key, item);
    if (item.count > rateLimit) throw fail(429, '请求过于频繁');
  };
  const selectLicense = db.prepare('SELECT * FROM licenses WHERE code_hash = ?');
  const cleanupSessions = () => {
    const time = now();
    db.prepare(`DELETE FROM sessions WHERE invalidated_at IS NOT NULL
      OR code_hash IN (SELECT code_hash FROM licenses WHERE revoked_at IS NOT NULL
        OR (expires_at IS NOT NULL AND expires_at < ?))`).run(time - 30 * 86_400_000);
  };
  const grantSession = (license, installationHash) => {
    const sessionToken = token();
    db.prepare('INSERT INTO sessions(token_hash,code_hash,installation_hash,binding_generation,created_at) VALUES(?,?,?,?,?)').run(hash(sessionToken), license.code_hash, installationHash, license.binding_generation, now());
    return sessionResponse(sessionToken, license.expires_at);
  };
  const sessionResponse = (sessionToken, expiresAt) => {
    const serverTime = now(), leaseSeconds = Math.min(120, Math.floor((expiresAt - serverTime) / 1000));
    if (leaseSeconds < 1) throw fail(403, '授权已过期', 'LICENSE_EXPIRED');
    return { sessionToken, expiresAt, serverTime, leaseSeconds };
  };
  const activate = ({ code: input, installationId, product }, ip = '') => {
    bumpRate(`ip:${ip}`);
    if (product !== PRODUCT || !validInstallationId(installationId)) throw fail(400, '请求参数无效');
    const normalized = normalizeCode(input);
    if (!normalized) throw fail(401, '激活码无效或不可用');
    const codeHash = hash(normalized), installationHash = hash(installationId);
    bumpRate(`installation:${installationHash}`);
    db.exec('BEGIN IMMEDIATE');
    try {
      cleanupSessions();
      const license = selectLicense.get(codeHash);
      const time = now();
      if (!license || license.revoked_at !== null) throw fail(401, '激活码无效或不可用');
      if (license.installation_hash && license.installation_hash !== installationHash) throw fail(409, '激活码已绑定到其他安装');
      if (license.expires_at !== null && license.expires_at <= time) throw fail(403, '授权已失效');
      if (license.first_activated_at === null) {
        const expiresAt = time + license.duration_days * 86_400_000;
        const generation = license.installation_hash === null ? license.binding_generation + 1 : license.binding_generation;
        db.prepare('UPDATE licenses SET first_activated_at=?,expires_at=?,installation_hash=?,bound_at=?,binding_generation=? WHERE code_hash=?').run(time, expiresAt, installationHash, time, generation, codeHash);
        license.first_activated_at = time; license.expires_at = expiresAt; license.installation_hash = installationHash; license.binding_generation = generation;
      } else if (license.installation_hash === null) {
        const generation = license.binding_generation + 1;
        db.prepare('UPDATE licenses SET installation_hash=?,bound_at=?,binding_generation=? WHERE code_hash=?').run(installationHash, time, generation, codeHash);
        license.installation_hash = installationHash; license.binding_generation = generation;
      }
      const activeSessions = db.prepare('SELECT COUNT(*) n FROM sessions WHERE code_hash=? AND invalidated_at IS NULL').get(codeHash).n;
      if (activeSessions >= 8) {
        db.prepare(`DELETE FROM sessions WHERE token_hash IN
          (SELECT token_hash FROM sessions WHERE code_hash=? AND invalidated_at IS NULL ORDER BY created_at ASC,token_hash ASC LIMIT ?)`).run(codeHash, activeSessions - 7);
      }
      const result = grantSession(license, installationHash);
      db.exec('COMMIT');
      return result;
    } catch (error) { db.exec('ROLLBACK'); throw error; }
  };
  const verify = ({ sessionToken, installationId, product }, ip = '') => {
    bumpRate(`ip:${ip}`);
    if (product !== PRODUCT || !validInstallationId(installationId) || typeof sessionToken !== 'string' || sessionToken.length < 32 || sessionToken.length > 128) throw fail(400, '请求参数无效');
    const installationHash = hash(installationId); bumpRate(`installation:${installationHash}`);
    cleanupSessions();
    const row = db.prepare(`SELECT s.*, l.expires_at,l.revoked_at,l.installation_hash,l.binding_generation AS license_generation
      FROM sessions s JOIN licenses l USING(code_hash) WHERE s.token_hash=?`).get(hash(sessionToken));
    const time = now();
    if (!row || row.invalidated_at !== null || row.revoked_at !== null || row.installation_hash !== installationHash || row.expires_at === null || row.binding_generation !== row.license_generation) throw fail(401, '会话无效或已失效');
    if (row.expires_at <= time) throw fail(403, '授权已过期', 'LICENSE_EXPIRED');
    return sessionResponse(sessionToken, row.expires_at);
  };
  const admin = (action, args = {}) => {
    if (action === 'generate') {
      const { count, durationDays } = args;
      if (!Number.isInteger(count) || count < 1 || count > 500 || !Number.isInteger(durationDays) || durationDays < 1 || durationDays > 3650) throw fail(400, '生成参数无效');
      const createdAt = now(), insert = db.prepare('INSERT INTO licenses(code_hash,duration_days,created_at) VALUES(?,?,?)'), codes = [];
      db.exec('BEGIN IMMEDIATE');
      try { if (db.prepare('SELECT COUNT(*) n FROM licenses').get().n + count > maxCodes) throw fail(409, '授权记录数量已达上限'); for (let i = 0; i < count; i++) { let value; do { value = code(); } while (selectLicense.get(hash(value.replaceAll('-', '')))); insert.run(hash(value.replaceAll('-', '')), durationDays, createdAt); codes.push(value); } db.exec('COMMIT'); }
      catch (error) { db.exec('ROLLBACK'); throw error; }
      return { codes };
    }
    if (action === 'list') { cleanupSessions(); return { licenses: db.prepare(`SELECT code_hash,duration_days,created_at,first_activated_at,expires_at,installation_hash,revoked_at,bound_at FROM licenses ORDER BY created_at DESC LIMIT 1000`).all().map(x => ({ id: x.code_hash, durationDays: x.duration_days, createdAt: x.created_at, firstActivatedAt: x.first_activated_at, expiresAt: x.expires_at, installationHint: x.installation_hash ? x.installation_hash.slice(0, 12).toUpperCase() : '', bound: Boolean(x.installation_hash), revoked: Boolean(x.revoked_at) })) }; }
    if (typeof args.id !== 'string' || !/^[a-f0-9]{64}$/.test(args.id)) throw fail(400, '授权标识无效');
    const row = selectLicense.get(args.id);
    if (!row) throw fail(404, '授权不存在');
    if (action === 'revoke') {
      db.exec('BEGIN IMMEDIATE');
      try { const time = now(); db.prepare('UPDATE licenses SET revoked_at=?,binding_generation=binding_generation+1 WHERE code_hash=? AND revoked_at IS NULL').run(time, args.id); db.prepare('UPDATE sessions SET invalidated_at=? WHERE code_hash=? AND invalidated_at IS NULL').run(time, args.id); db.exec('COMMIT'); }
      catch (error) { db.exec('ROLLBACK'); throw error; }
    }
    else if (action === 'extend') {
      if (!Number.isInteger(args.days) || args.days < 1 || args.days > 3650) throw fail(400, '续期天数无效');
      const expiry = row.first_activated_at === null ? null : Math.max(now(), row.expires_at) + args.days * 86_400_000;
      db.prepare('UPDATE licenses SET duration_days=duration_days+?,expires_at=? WHERE code_hash=?').run(args.days, expiry, args.id);
    } else if (action === 'unbind') {
      db.exec('BEGIN IMMEDIATE');
      try { const time = now(); db.prepare('UPDATE sessions SET invalidated_at=? WHERE code_hash=? AND invalidated_at IS NULL').run(time, args.id); db.prepare('UPDATE licenses SET installation_hash=NULL,bound_at=NULL,binding_generation=binding_generation+1 WHERE code_hash=?').run(args.id); db.exec('COMMIT'); }
      catch (error) { db.exec('ROLLBACK'); throw error; }
    } else throw fail(404, '接口不存在');
    return { ok: true };
  };
  const importSnapshot = (input) => {
    if (!bridgeToken) throw fail(503, '迁移接口尚未配置');
    if (!input || typeof input !== 'object' || Array.isArray(input) || input.version !== 1 || !Array.isArray(input.licenses) || !Array.isArray(input.sessions)) throw fail(400, '迁移数据格式无效');
    const { licenses, sessions } = input;
    if (licenses.length > maxCodes || sessions.length > 100_000) throw fail(413, '迁移数据行数超限');
    const isHash = value => typeof value === 'string' && /^[a-f0-9]{64}$/.test(value);
    const nullableTime = value => value === null || (Number.isSafeInteger(value) && value >= 0);
    const seenLicenses = new Set(), seenSessions = new Set();
    for (const row of licenses) {
      if (!row || typeof row !== 'object' || !isHash(row.code_hash) || row.product !== PRODUCT || !Number.isInteger(row.duration_days) || row.duration_days < 1 || !nullableTime(row.created_at) || row.created_at === null || !nullableTime(row.first_activated_at) || !nullableTime(row.expires_at) || !(row.installation_hash === null || isHash(row.installation_hash)) || !Number.isInteger(row.binding_generation) || row.binding_generation < 0 || !nullableTime(row.revoked_at) || !nullableTime(row.bound_at) || seenLicenses.has(row.code_hash)) throw fail(400, '授权迁移记录无效');
      seenLicenses.add(row.code_hash);
    }
    for (const row of sessions) {
      if (!row || typeof row !== 'object' || !isHash(row.token_hash) || !isHash(row.code_hash) || !isHash(row.installation_hash) || !Number.isInteger(row.binding_generation) || row.binding_generation < 0 || !nullableTime(row.created_at) || row.created_at === null || !nullableTime(row.invalidated_at) || seenSessions.has(row.token_hash) || !seenLicenses.has(row.code_hash)) throw fail(400, '会话迁移记录无效');
      seenSessions.add(row.token_hash);
    }
    db.exec('BEGIN IMMEDIATE');
    try {
      if (db.prepare('SELECT COUNT(*) n FROM licenses').get().n !== 0 || db.prepare('SELECT COUNT(*) n FROM sessions').get().n !== 0) throw fail(409, '目标授权库必须为空');
      const insertLicense = db.prepare(`INSERT INTO licenses(code_hash,duration_days,created_at,first_activated_at,expires_at,installation_hash,revoked_at,bound_at,binding_generation)
        VALUES(?,?,?,?,?,?,?,?,?)`);
      const insertSession = db.prepare(`INSERT INTO sessions(token_hash,code_hash,installation_hash,created_at,invalidated_at,binding_generation)
        VALUES(?,?,?,?,?,?)`);
      for (const row of licenses) insertLicense.run(row.code_hash,row.duration_days,row.created_at,row.first_activated_at,row.expires_at,row.installation_hash,row.revoked_at,row.bound_at,row.binding_generation);
      for (const row of sessions) insertSession.run(row.token_hash,row.code_hash,row.installation_hash,row.created_at,row.invalidated_at,row.binding_generation);
      db.exec('COMMIT');
    } catch (error) { db.exec('ROLLBACK'); throw error; }
    return { licensesImported: licenses.length, sessionsImported: sessions.length };
  };
  let closed = false;
  const close = () => { if (!closed) { closed = true; db.close(); } };
  const handler = async (req, res) => {
    const send = (status, data, type = 'application/json; charset=utf-8') => { const body = typeof data === 'string' ? data : JSON.stringify(data); res.writeHead(status, { 'content-type': type, 'content-length': Buffer.byteLength(body), 'cache-control': 'no-store', 'x-content-type-options': 'nosniff' }); res.end(body); };
    try {
      const url = new URL(req.url, 'http://127.0.0.1');
      if (req.method === 'GET' && url.pathname === '/') return send(200, await readFile(path.join(here, 'public', 'index.html'), 'utf8'), 'text/html; charset=utf-8');
      const adminRoute = url.pathname.startsWith('/admin/');
      const bridgeRoute = url.pathname.startsWith('/bridge/license/');
      const clientRoute = req.method === 'POST' && (url.pathname === '/v1/activate' || url.pathname === '/v1/verify' || url.pathname === '/bridge/license/activate' || url.pathname === '/bridge/license/verify');
      const ip = clientRoute ? clientIp(req, trustProxy) : '';
      if (adminRoute) {
        if (req.socket.localAddress !== '127.0.0.1' && req.socket.localAddress !== '::ffff:127.0.0.1') return send(404, { error: '接口不存在' });
        const origin = req.headers.origin;
        if (origin) { let parsed; try { parsed = new URL(origin); } catch { return send(403, { error: '来源无效' }); }
          if (parsed.protocol !== 'http:' || !['127.0.0.1', 'localhost'].includes(parsed.hostname) || parsed.host !== req.headers.host) return send(403, { error: '来源无效' }); }
        if (!safeEqual(req.headers.authorization?.replace(/^Bearer\s+/i, ''), adminToken)) return send(401, { error: '管理认证失败' });
      }
      if (bridgeRoute) {
        if (!bridgeToken) return send(503, { error: '授权桥接尚未配置' });
        if (!safeEqual(req.headers.authorization?.replace(/^Bearer\s+/i, ''), bridgeToken)) return send(401, { error: '授权桥接认证失败' });
      }
      if (!['GET', 'HEAD'].includes(req.method)) req.setTimeout(5000, () => req.destroy());
      let body = {};
      if (!['GET', 'HEAD'].includes(req.method)) {
        const chunks = []; let size = 0, limit = bridgeRoute && url.pathname === '/bridge/license/import' ? MAX_IMPORT_BODY : MAX_BODY;
        for await (const chunk of req) { size += chunk.length; if (size > limit) throw fail(413, '请求体过大'); chunks.push(chunk); }
        try { body = JSON.parse(Buffer.concat(chunks).toString('utf8')); } catch { throw fail(400, 'JSON格式无效'); }
        if (!body || typeof body !== 'object' || Array.isArray(body)) throw fail(400, '请求体必须是对象');
      }
      if (req.method === 'POST' && url.pathname === '/v1/activate') return send(200, activate(body, ip));
      if (req.method === 'POST' && url.pathname === '/v1/verify') return send(200, verify(body, ip));
      if (req.method === 'POST' && url.pathname === '/bridge/license/activate') return send(200, activate(body, ip));
      if (req.method === 'POST' && url.pathname === '/bridge/license/verify') return send(200, verify(body, ip));
      if (bridgeRoute && req.method === 'POST' && url.pathname === '/bridge/license/import') return send(200, importSnapshot(body));
      if (bridgeRoute && req.method === 'GET' && url.pathname === '/bridge/license/admin/list') return send(200, admin('list'));
      if (bridgeRoute && req.method === 'POST' && /^\/bridge\/license\/admin\/(generate|revoke|extend|unbind)$/.test(url.pathname)) return send(url.pathname.endsWith('/generate') ? 201 : 200, admin(url.pathname.split('/').at(-1), body));
      if (adminRoute && req.method === 'GET' && url.pathname === '/admin/list') return send(200, admin('list'));
      if (adminRoute && req.method === 'POST' && url.pathname === '/admin/generate') return send(201, admin('generate', body));
      if (adminRoute && req.method === 'POST' && url.pathname === '/admin/revoke') return send(200, admin('revoke', body));
      if (adminRoute && req.method === 'POST' && url.pathname === '/admin/extend') return send(200, admin('extend', body));
      if (adminRoute && req.method === 'POST' && url.pathname === '/admin/unbind') return send(200, admin('unbind', body));
      return send(404, { error: '接口不存在' });
    } catch (error) { if (!res.headersSent) send(error.status ?? 500, { error: error.status ? error.message : '服务内部错误', ...(error.code ? { code: error.code } : {}) }); else res.destroy(); }
  };
  const server = httpServer(handler);
  server.requestTimeout = 5000;
  server.headersTimeout = 6000;
  return { server, handler, close, activate, verify, admin, importSnapshot };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const app = createLicensingServer();
  const port = Number(process.env.PORT ?? 8787);
  app.server.listen(port, '127.0.0.1', () => console.log(`Licensing management page: http://127.0.0.1:${port}/`));
  for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => { app.server.close(() => { app.close(); process.exit(0); }); });
}
