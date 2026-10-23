const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const lua = read('app/src/main/assets/anlei.lua.in');
const rust = read('worker-rust/src/main.rs');
const service = read('app/src/main/java/com/xnas/wpe/anlei/AnleiService.java');
const activity = read('app/src/main/java/com/xnas/wpe/anlei/MainActivity.java');
const manifest = read('app/src/main/AndroidManifest.xml');
const gradle = read('app/build.gradle');

const routeSource = lua.split('local function route', 2)[1].split('local function tick', 1)[0];
const route = [...routeSource.matchAll(/\{(\d+),(\d+)\}/g)].map(([, x, y]) => [Number(x), Number(y)]);
assert.equal(route.length, 10, 'route must have ten positions');
assert.equal(new Set(route.map(([x, y]) => `${x},${y}`)).size, 10, 'route positions must be unique');
assert(route.slice(1).every(([x, y], i) => Math.abs(x - route[i][0]) + Math.abs(y - route[i][1]) === 1), 'route positions must be adjacent');

const tick = lua.split('local function tick(dt)', 2)[1].split('s.timer=', 1)[0];
assert(tick.includes('JudgeIsInWar()'), 'combat must pause position calls');
assert(!tick.includes('1003') && !tick.includes('m_IsAutoXunLuo') && !tick.includes('m_Moving'), 'map and patrol/movement state must not gate batches');
assert(!tick.includes('getPosCanGo') && !tick.includes('pathRetryAt'), 'route must not add passability checks');
assert(lua.includes('scheduler.scheduleGlobal(tick,0)') && lua.includes('local function tick(dt)'), 'zero-interval global scheduler must invoke the callback every frame with dt');
assert(tick.includes('s.sendElapsed=s.sendElapsed+dt') && tick.includes('if s.sendElapsed<0.1 then'), 'normal position calls must be throttled to at most 10 per second');
assert(tick.includes('local resumeFromBattle=s.wasWar') && tick.includes('if not resumeFromBattle then'), 'first valid frame after combat must send immediately');
assert(!tick.includes('s.batchAt') && !tick.includes('while ') && !tick.includes('for _,pt'), 'stream must have no batch cap or blocking send loop');
const combatGuard = tick.indexOf('if inWar then'), sendDecision = tick.indexOf('local resumeFromBattle');
assert(combatGuard >= 0 && sendDecision >= 0 && combatGuard < sendDecision, 'combat must be checked before each send decision');
assert(tick.includes("progress(s,'battle')"), 'battle state must be published');
const ownershipGuard = tick.indexOf('rawget(_G,key)==s and LocalPlayerMgr:getInstance()==p'), positionCall = tick.indexOf('mm:sendPlayerPos');
assert(ownershipGuard >= 0 && positionCall >= 0 && ownershipGuard < positionCall, 'each call must recheck combat and role ownership');
assert((lua.match(/mm:sendPlayerPos\(/g)||[]).length===1, 'one scheduler callback may invoke at most one game position call');
assert(lua.includes('s.lastStatus==status and s.lastMap==mapId and s.lastProgressSent==s.sent'), 'progress file writes must be cached to status/call changes');
for (const token of ['if not pOk or not p then s.sendElapsed=0;progress(s,\'waiting_role\');return end', 'if not viewOk or not view then s.sendElapsed=0;progress(s,\'waiting_map\');return end', 'if not mapOk or mapId==nil then s.sendElapsed=0;progress(s,\'waiting_map\');return end']) assert(tick.includes(token), `transient state must wait safely: ${token}`);

assert(rust.includes('ident[0] != (pid, ticks)'), 'worker must compare PID and startTicks after attach');
assert(rust.includes('fs::read_link(format!("/proc/{pid}/exe")).ok() != Some(exe.clone())'), 'worker must compare executable identity');
assert(rust.includes('proc_start(pid)'), 'worker must read startTicks');
assert(rust.includes('Err(_) => {\n                status(&dir, "waiting_game");'), 'racing process exit during exe lookup must wait and retry');
assert(rust.includes('fn parse_result_field'), 'worker must read live state and map status');
assert(!rust.includes('parse_rounds'), 'worker status must not expose obsolete batch counts');
assert(rust.includes('result_checked.elapsed() >= Duration::from_millis(100)') && rust.includes('status_at.elapsed() >= Duration::from_millis(100)'), 'worker must refresh battle state at 100ms cadence');

assert(lua.includes('os.time()-n>5') && lua.includes('if not leaseAlive() then finish'), 'expired lease must fail closed');
assert(lua.includes("tostring(why):gsub('[\\r\\n=]',' ')") && lua.includes('result(\'stopped\',true,why)'), 'Lua cleanup must retain a sanitized failure reason');
for (const token of ['status=', 'hooksRemoved=', 'bindingCleared=true']) assert(lua.includes(token), `Lua result must preserve worker cleanup confirmation: ${token}`);
assert(rust.includes('fn lease_fresh(') && rust.includes('if !lease_fresh(&dir.join("lease"))'), 'worker must stop when app lease expires');
assert(rust.includes('write_game_file(&worker_beat') && rust.includes('write_game_file(&lease_mirror'), 'worker must mirror leases into game-readable cache');
for (const token of ['status=stopped', 'hooksRemoved=true', 'bindingCleared=true']) assert(rust.includes(`result_snapshot.contains("${token}")`), `detach must require ${token}`);
assert(service.includes('if(generation==stopGeneration&&stopping)') && service.includes('generation!=expected||!stopping||!stopPending'), 'old stop completion must not stop a later START');
assert(service.includes('stopSelfResult(stopStartId)') && service.includes('if(stopping&&stopPending){stopStartId=startId;return;}'), 'stop must use the latest stop request startId');
const monitor = service.split('private synchronized void monitorTick()', 2)[1].split('private synchronized void stopWorker', 1)[0];
assert(monitor.includes('if(desiredRunning||stopPending)getSystemService(NotificationManager.class).notify'), 'stopped service must not recreate its notification');
assert(monitor.includes('generation==expected&&stopping'), 'monitor stop completion must retain generation guard');
assert(service.includes('mainHandler.post(this::removeOverlay);stopSelfResult(stopStartId)'), 'confirmed STOP must schedule HUD removal on the main thread');
assert(service.includes('timer.shutdownNow();removeOverlay();super.onDestroy()'), 'onDestroy must remove HUD directly on the main thread');
assert(service.includes('if(desiredRunning&&!stopping&&worker==null){startWorker();}'), 'service must restart a missing worker with cooldown');
assert(service.includes('WindowManager') && service.includes('TYPE_APPLICATION_OVERLAY') && service.includes('removeOverlay()'), 'service must display and remove an overlay');
assert(service.includes('return "持续运行"') && service.includes('return "战斗暂停"') && service.includes('postDelayed(this,100)'), 'overlay must refresh at 100ms and use only compact active-state labels');
assert(activity.includes('ACTION_MANAGE_OVERLAY_PERMISSION'), 'activity must provide the overlay permission entry');
assert(activity.includes('位置同步调用 " + sent + " 次') && activity.includes('战斗中，暂停位置同步'), 'activity must label call count accurately and translate battle status');
assert(manifest.includes('android.permission.SYSTEM_ALERT_WINDOW') && manifest.includes('android:label="暗雷遇敌助手"'), 'manifest must declare overlay access and product label');
assert(gradle.includes('versionCode 3') && gradle.includes("versionName '0.3.0'"), 'new behavior must use version 0.3.0');
for (const file of ['build.ps1', 'worker-rust/build.ps1']) {
  const build = read(file);
  assert(build.includes('ANDROID_HOME') && build.includes('ANDROID_SDK_ROOT'), `${file} must accept SDK environment variables`);
  assert(build.includes('ANDROID_NDK_HOME') && build.includes('ANDROID_NDK_ROOT'), `${file} must accept NDK environment variables`);
}
console.log('暗雷助手源静态契约自检通过：帧级战斗检查与恢复、100ms节流、单点回调、简短HUD、租约与停止确认');
