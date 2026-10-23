local initial = readStatus()
assert(initial.status == 'reading', 'new Lua session must start in scan mode')
assert(initial.acceptedSequence == '10', 'new Lua session did not acknowledge its baseline command')
assert(initial.selectedLevel == '0', 'new Lua session resumed a stale baseline run command')
assert(initial.hooksRemoved == 'true' and initial.bindingCleared == 'true', 'initial scan must have no hook or binding')
assert(countSends() == 0, 'opening the scan must not send business requests')
assert(netconn.useFunc == originalUseBefore, 'passive scanning must not install a network observer')
assert(netconn.send == originalSendBefore, 'starhunt must not replace netconn.send')
assert(ProtoFunc == protoTableBefore and ProtoFunc[3584] == proto3584Before and ProtoFunc[3581] == proto3581Before,
  'starhunt must not replace ProtoFunc handlers')

advance(0.5)
assert(countSends() == 0, 'scan tick emitted a business request')
inWar = true
advance(0.5)
assert(readStatus().status == 'reading' and countSends() == 0,
  'passive scan must remain reading during a battle without entering run-state handling')
inWar = false
local scan = readStatus()
assert(scan.levels == '3:1,21:2', 'level counts must be numeric-sorted and deduplicated by roleId: ' .. tostring(scan.levels))
assert(scan.levelNames:match('^3:地煞三星目标|21:'), 'level names must follow numeric level ordering')

setCommand(11, 'run', 21, 1018)
advance(0.5)
local first = readStatus()
assert(first.acceptedSequence == '11' and first.selectedLevel == '21', 'explicit run command was not acknowledged')
assert(first.targetId == '20' and first.targetX == '200' and first.targetY == '300',
  'busy target was not rotated past: status=' .. tostring(first.status) .. ' reason=' .. tostring(first.reason) ..
  ' levels=' .. tostring(first.levels) .. ' target=' .. tostring(first.targetId) .. ' sends=' .. tostring(countSends()))
assert(first.skipped == '1', 'busy target was not recorded as a conservative skip')
assert(countSends(22568) == 1 and countSends(12344) == 0, 'run must emit exactly one teleport first')
local teleport = lastSend(22568)
assert(teleport.args[1] == 1018 and teleport.args[2] == 200 and teleport.args[3] == 300 and teleport.args[4] == 0,
  'teleport arguments do not match the current target')
assert(netconn.useFunc ~= originalUseBefore, 'run must install only the inbound observer')

local firstReturn, secondReturn = emit(65505, {mapId = 1018, posX = 200, posY = 301})
assert(firstReturn == 'original-first' and secondReturn == 'original-second', 'observer must preserve original return values')
advance(0.5)
assert(countSends(12344) == 0, 'mismatched position response must not challenge')
emit(65505, {mapId = 1018, posX = 200, posY = 300})
advance(0.5)
assert(countSends(12344) == 0, 'matching response without local position update must not challenge')
mapView.m_LocalRole.m_Gx = 200
mapView.m_LocalRole.m_Gy = 300
advance(0.5)
assert(countSends(12344) == 0, 'position confirmation should require the next validated send tick')
advance(0.5)
assert(countSends(12344) == 1, 'exact matching response and local movement must enable one challenge')
local challenge = lastSend(12344)
assert(challenge.args[1] == 20 and challenge.args[2] == 'threats_star' and challenge.args[3] == '',
  'challenge arguments do not match the selected target')
emit(4155, {code = 'rejected'})
advance(0.5)
assert(readStatus().status == 'challenge_wait' and countSends(12344) == 1,
  '4155 must not be treated as success or busy evidence')

local beforeOriginal = #originalUseCalls
local observerErrorData = {npcId = 20, buttons = setmetatable({}, {__pairs = function() error('observer failure') end})}
local errorFirst, errorSecond = emit(8247, observerErrorData)
assert(errorFirst == 'original-first' and errorSecond == 'original-second', 'observer exception blocked the original handler')
assert(#originalUseCalls == beforeOriginal + 1, 'original handler was not called after observer exception')

failWrites.result = true
beforeOriginal = #originalUseCalls
local logFirst, logSecond = emit(3584, {roleId = 101})
assert(logFirst == 'original-first' and logSecond == 'original-second', 'observer status write failure blocked the original handler')
assert(#originalUseCalls == beforeOriginal + 1, 'original handler was not called after status write failure')
failWrites.result = nil

originalUseError = 'original-handler-error'
local originalOk, originalError = pcall(function() emit(7777, {}) end)
assert(not originalOk and tostring(originalError):find('original-handler-error', 1, true),
  'observer wrapper swallowed the original handler exception')
originalUseError = nil

inWar = true
advance(0.5)
assert(readStatus().status == 'battle', 'matching own-role 3584 plus local war state must enter battle')
local sendsAtBattle = countSends()
advance(0.5, 10)
assert(countSends() == sendsAtBattle, 'battle phase emitted a new business request')

npcBusy.fighting = false
inWar = false
emit(3581, {})
advance(0.5)
assert(readStatus().status == 'reading', '3581 plus no-war and ready map must return to scanning')
advance(0.5, 12)
assert(countSends(22568) == 2, 'post-battle scanning did not continue to the next target: sends=' .. tostring(countSends(22568)) .. ' status=' .. tostring(readStatus().status) .. ' reason=' .. tostring(readStatus().reason))

setCommand(12, 'scan', 0, 0)
advance(0.5)
local stoppedRun = readStatus()
assert(stoppedRun.acceptedSequence == '12' and stoppedRun.status == 'reading', 'scan stop command was not acknowledged')
assert(stoppedRun.hooksRemoved == 'true' and stoppedRun.bindingCleared == 'true', 'scan stop did not clear owned hook and binding')
local sendsAtScanStop = countSends()
advance(0.5, 3)
assert(countSends() == sendsAtScanStop, 'scan stop allowed another request')

setCommand(11, 'run', 21, 1018)
advance(0.5)
local stale = readStatus()
assert(stale.acceptedSequence == '12' and countSends() == sendsAtScanStop, 'stale command was replayed after stop acknowledgement')
setCommand(13, 'scan', 0, 0)
expectedFinalSequence = '13'
