-- A selected level may be absent at startup and appear later without another RUN.
assert(countSends() == 0, 'opening the page must remain passive')
setCommand(11, 'run', 8, 1018)
advance(0.5, 12)
assert(readStatus().status == 'waiting_targets', 'absent selected level must keep waiting')
assert(readStatus().reason == 'no_targets_for_selected_level', 'waiting must explain absent targets')
assert(readStatus().selectedLevel == '8' and countSends() == 0,
  'waiting must retain the selected level without issuing game requests')

local first = {roleId = 80, mapId = 1018, title = '地煞8星', name = '刷新目标甲', posX = 280, posY = 380}
mapManager.m_DynamicNPC[#mapManager.m_DynamicNPC + 1] = {m_NpcInfo = first}
advance(0.5, 3)
assert(readStatus().targetId == '80' and countSends(22568) == 1,
  'a freshly appeared target must be found without another start command')
emit(65505, {mapId = 1018, posX = 280, posY = 380})
mapView.m_LocalRole.m_Gx = 280
mapView.m_LocalRole.m_Gy = 380
advance(0.5, 2)
assert(countSends(12344) == 1 and lastSend(12344).args[1] == 80,
  'newly appeared selected-level target must be challenged once')
emit(3584, {roleId = activeRole})
inWar = true
advance(0.5)
local battleSends = countSends()
advance(0.5, 10)
assert(readStatus().status == 'battle' and countSends() == battleSends,
  'waiting-refresh mode must not send during battle')

table.remove(mapManager.m_DynamicNPC)
inWar = false
emit(3581, {})
advance(0.5, 14)
assert(readStatus().status == 'waiting_targets' and readStatus().selectedLevel == '8',
  'after the last target disappears, resume waiting for the same level')
assert(countSends() == battleSends, 'no-target waiting after battle must stay passive')

local second = {roleId = 81, mapId = 1018, title = '地煞8星', name = '刷新目标乙', posX = 281, posY = 381}
mapManager.m_DynamicNPC[#mapManager.m_DynamicNPC + 1] = {m_NpcInfo = second}
advance(0.5, 3)
assert(readStatus().targetId == '81' and countSends(22568) == 2,
  'a later refresh must resume automatically after battle without re-starting')
assert(readStatus().acceptedSequence == '11', 'waiting and refresh must not manufacture new start commands')
setCommand(12, 'scan', 0, 0)
advance(0.5)
assert(readStatus().hooksRemoved == 'true', 'stop must remove the action observer')
local stoppedSends = countSends()
advance(0.5, 10)
assert(countSends() == stoppedSends, 'refresh after stop must not resume actions')
expectedFinalSequence = '12'
