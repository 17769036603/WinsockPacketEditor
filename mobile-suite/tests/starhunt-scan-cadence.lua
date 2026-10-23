-- A selected level that appears after startup is acted on by the next real scheduler beat.
assert(type(fakeTimerInterval) == 'number', 'scheduler interval was not captured')
setCommand(11, 'run', 8, 1018)
advanceScheduled(1)
assert(readStatus().status == 'waiting_targets', 'absent selected level must wait')
local appearing = {roleId = 80, mapId = 1018, title = '地煞8星', name = '刷新目标', posX = 280, posY = 380}
mapManager.m_DynamicNPC[#mapManager.m_DynamicNPC + 1] = {m_NpcInfo = appearing}
advanceScheduled(1)
assert(fakeTimerInterval <= 0.25, 'scheduler must discover a new target within 250ms; interval=' .. tostring(fakeTimerInterval))
assert(readStatus().targetId == '80' and countSends(22568) == 1,
  'new target must be selected and first action taken on the next scheduled callback')
emit(65505, {mapId = 1018, posX = 280, posY = 380})
mapView.m_LocalRole.m_Gx, mapView.m_LocalRole.m_Gy = 280, 380
advanceScheduled(2)
assert(countSends(12344) == 1 and readStatus().status == 'challenge_wait',
  'matching position must enable one challenge')
advanceScheduled(47)
assert(readStatus().status == 'challenge_wait',
  'challenge must remain pending through 11.75 seconds at the 250ms cadence')
advanceScheduled(1)
assert(readStatus().status == 'waiting_targets' and readStatus().reason == 'challenge_unconfirmed_rotate',
  'challenge timeout must rotate at twelve seconds')
advanceScheduled(1)
local afterChallengeTimeout = countSends(22568)
advanceScheduled(19)
assert(countSends(22568) == afterChallengeTimeout and readStatus().reason == 'round_cooldown',
  'next round must remain closed through 4.75 seconds')
advanceScheduled(1)
assert(countSends(22568) == afterChallengeTimeout + 1,
  'next round must reopen at the five-second cooldown boundary')

-- A nil delta must consume the registered cadence rather than the old 500ms fallback.
setCommand(12, 'scan', 0, 0)
advanceScheduled(1)
setCommand(13, 'run', 8, 1018)
local teleportsBeforeTimeout = countSends(22568)
advanceScheduled(1)
assert(readStatus().status == 'teleport_wait', 'nil-dt setup must begin at the teleport deadline')
for _ = 1, 31 do fakeTimer(nil) end
assert(readStatus().status == 'teleport_wait',
  'nil-dt elapsed must use 250ms: 31 callbacks must remain before the eight-second deadline')
fakeTimer(nil)
assert(readStatus().status == 'unknown' and readStatus().reason == 'teleport_confirmation_timeout',
  '32 nil-dt callbacks must reach the eight-second teleport timeout')
assert(countSends(22568) == teleportsBeforeTimeout + 1,
  'teleport timeout must not automatically retry or rotate')
expectedFinalSequence = '13'
