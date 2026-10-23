setCommand(11, 'run', 21, 1018)
advance(0.5)
assert(lastSend(22568).args[1] == 1018 and lastSend(22568).args[2] == 200,
  'run must skip the locally busy first candidate and teleport to the next candidate')
emit(65505, {mapId = 1018, posX = 200, posY = 300})
mapView.m_LocalRole.m_Gx = 200
mapView.m_LocalRole.m_Gy = 300
advance(0.5, 2)
assert(countSends(12344) == 1, 'matching teleport confirmation must enable one challenge')

emit(8247, {npcId = 30, btns = '我要观战/guard'})
assert(readStatus().status == 'challenge_wait', 'other NPC button payload must not rotate the target')
emit(8247, {npcId = 20, btns = '我要观战/guard|挑战/threats_star'})
assert(readStatus().status == 'challenge_wait', 'mixed attack action must not be treated as watch-only')
emit(8247, {npcId = 20, btns = 'guard/我要观战'})
assert(readStatus().status == 'challenge_wait', 'reversed label/action fields must not match guard')
emit(8247, {npcId = 20, btns = '我要观战|再见/离开'})
assert(readStatus().status == 'challenge_wait', 'malformed button groups must not rotate the target')
emit(8247, {npcId = 20, btns = '提示/unknown|再见/离开'})
assert(readStatus().status == 'challenge_wait', 'unknown action must not be treated as watch-only')

-- The real payload is a string: label/action groups separated by |, fields by /.
emit(8247, {npcId = 20, btns = '我要观战/guard|再见/离开'})
assert(readStatus().targetId == 'unknown', 'guard plus leave must immediately rotate away from this target')
assert(readStatus().reason == '目标提供观战入口', 'watch-only action must retain the busy reason')

-- No target may be retried in the same round. The next round opens after five seconds.
local sendsAfterFirstRound = countSends()
-- Make the previously busy candidate available for the next round.
npcBusy.fighting = false
advance(0.5, 8)
assert(countSends() == sendsAfterFirstRound, 'all attempted candidates must wait before starting a new round')
advance(0.5, 3)
assert(countSends(22568) == 2, 'a fresh round must resume after the five-second wait')

-- A challenge timeout rotates; it is not a busy skip. A late own-role 3584 then blocks sends.
local secondTeleport = lastSend(22568)
local secondId = secondTeleport.args[2] == 110 and 10 or 20
local secondX, secondY = secondTeleport.args[2], secondTeleport.args[3]
emit(65505, {mapId = 1018, posX = secondX, posY = secondY})
mapView.m_LocalRole.m_Gx = secondX
mapView.m_LocalRole.m_Gy = secondY
advance(0.5, 2)
assert(countSends(12344) == 2, 'second target challenge was not sent')
local skippedBeforeTimeout = tonumber(readStatus().skipped)
advance(0.5, 24)
local timeoutStatus = readStatus()
assert(timeoutStatus.status == 'waiting_targets' and timeoutStatus.reason == 'challenge_unconfirmed_rotate',
  '12-second challenge timeout must rotate with an unconfirmed reason')
assert(tonumber(timeoutStatus.skipped) == skippedBeforeTimeout, 'challenge timeout must not be counted as busy')
advance(0.5, 1)
assert(countSends(22568) == 3 and lastSend(22568).args[2] ~= secondX,
  'a timed-out candidate must rotate to the remaining candidate without retrying this round')
emit(3584, {roleId = activeRole})
local sendCountAtLateResponse = countSends()
advance(0.5, 3)
assert(countSends() == sendCountAtLateResponse, 'late own-role battle response must block later target sends')
inWar = true
advance(0.5)
assert(readStatus().status == 'battle', 'own-role response plus local war state must confirm battle')
inWar = false
emit(3581, {})
advance(0.5)
assert(readStatus().status == 'reading', 'battle end must resume the run')

setCommand(12, 'scan', 0, 0)
advance(0.5)
assert(readStatus().hooksRemoved == 'true', 'stop command must remove the observer')
local sendsAtStop = countSends()
emit(3584, {roleId = activeRole})
advance(0.5, 4)
assert(countSends() == sendsAtStop, 'a response after stop must not restore challenge ownership or send again')
expectedFinalSequence = '12'
