-- Two selected star levels share the existing instance-order round robin.
npcReady.m_NpcInfo.title = '地煞8星'
npcThree.title = '地煞21星'
local nonTarget = makeInfo(60, '地煞9星', '未选等级目标', 400, 410)
mapManager.m_DynamicNPC[#mapManager.m_DynamicNPC + 1] = {m_NpcInfo = nonTarget}

setLevelsCommand(11, 'run', '8,21', 1018)
advance(0.5)
local firstStatus = readStatus()
assert(firstStatus.selectedLevels == '8,21' and firstStatus.selectedLevel == '8',
  'status must expose the full canonical selection and retain its first level for compatibility')
assert((firstStatus.levels or ''):find('9:1', 1, true) and (firstStatus.levels or ''):find('21:2', 1, true),
  'level counts must continue to include selected and non-selected stars')
assert(firstStatus.targetId == '20' and lastSend(22568).args[2] == 200,
  'a busy first instance at level 21 must be skipped so the selected level 8 instance is tried next')
assert(tonumber(firstStatus.skipped) == 1,
  'the busy selected instance must be counted once while considering the following selected level')

emit(65505, {mapId = 1018, posX = 200, posY = 300})
mapView.m_LocalRole.m_Gx, mapView.m_LocalRole.m_Gy = 200, 300
advance(0.5, 2)
assert(countSends(12344) == 1 and lastSend(12344).args[1] == 20,
  'the first selected level instance must challenge only after position confirmation')
advance(0.5, 24)
assert(readStatus().reason == 'challenge_unconfirmed_rotate', 'the first instance must retain the 12-second challenge wait')
advance(0.5)
assert(readStatus().targetId == '30' and lastSend(22568).args[2] == 310,
  'the other selected level must be tried in the same round after the first target rotates')
emit(65505, {mapId = 1018, posX = 310, posY = 320})
mapView.m_LocalRole.m_Gx, mapView.m_LocalRole.m_Gy = 310, 320
advance(0.5, 2)
assert(countSends(12344) == 2 and lastSend(12344).args[1] == 30,
  'the level 21 instance must be challenged once in the same round')
advance(0.5, 24)
local secondTimeout = readStatus()
assert(secondTimeout.reason == 'challenge_unconfirmed_rotate', 'the second instance must retain the challenge confirmation timeout')
advance(0.5)
local sendsAtRoundEnd = countSends(22568)
assert(sendsAtRoundEnd == 2, 'each selected instance may be attempted only once in one round')
advance(0.5, 8)
assert(countSends(22568) == sendsAtRoundEnd and readStatus().reason == 'round_cooldown',
  'after all selected instances are attempted, the five-second round cooldown must hold')

-- Reusing a sequence with a different full collection must not silently keep the old run.
fakeFiles.command = 'sequence=11\nmode=run\nlevels=8\nmap=1018\n'
advance(0.5)
assert(readStatus().status == 'unknown' and readStatus().reason == 'command_sequence_reused',
  'sameCommand must compare every selected level at the same sequence')
local sendsBeforeInvalid = countSends()
setLevelsCommand(12, 'scan', '', 0)
advance(0.5)
assert(readStatus().selectedLevels == '' and readStatus().selectedLevel == '0' and readStatus().hooksRemoved == 'true',
  'empty SCAN selection must be accepted and remove the action observer')

for _, invalid in ipairs({
  'sequence=13\nmode=run\nlevels=21,8\nmap=1018\n',
  'sequence=13\nmode=run\nlevels=8,8\nmap=1018\n',
  'sequence=13\nmode=run\nlevels=8\nlevel=8\nmap=1018\n',
}) do
  fakeFiles.command = invalid
  advance(0.5)
  assert(readStatus().acceptedSequence == '12' and countSends() == sendsBeforeInvalid,
    'an invalid selection must not be accepted or send game actions')
end

-- The legacy single-level command remains a precise one-star selection.
fakeFiles.command = 'sequence=14\nmode=run\nlevel=8\nmap=1018\n'
advance(0.5)
assert(readStatus().selectedLevels == '8' and readStatus().selectedLevel == '8'
    and readStatus().targetId == '20' and lastSend(22568).args[2] == 200,
  'legacy level=8 must normalize to exactly the level 8 target')
emit(65505, {mapId = 1018, posX = 200, posY = 300})
mapView.m_LocalRole.m_Gx, mapView.m_LocalRole.m_Gy = 200, 300
advance(0.5, 2)
assert(countSends(12344) == 3 and lastSend(12344).args[1] == 20,
  'legacy single-level selection must challenge only its selected star')

fakeFiles.command = 'sequence=15\nmode=scan\nlevel=8\nmap=0\n'
advance(0.5)
assert(readStatus().selectedLevels == '8' and readStatus().selectedLevel == '8'
    and readStatus().hooksRemoved == 'true',
  'legacy positive SCAN selection must be normalized while leaving actions stopped')
local stoppedSends = countSends()
advance(0.5, 4)
assert(countSends() == stoppedSends, 'legacy SCAN must leave all game actions stopped')
expectedFinalSequence = '15'
