local function showOnlyReadyAsDynamic()
  npcReady.m_NpcInfo.title = '地煞21星'
  npcReady.m_NpcInfo.posX = 200
  npcReady.m_NpcInfo.posY = 300
  npcReady.fighting = false
  mapView.m_NPC = {}
  mapManager.m_DynamicNPC = {{m_NpcInfo = npcReady.m_NpcInfo}}
  mapManager.m_CurMapFarNpc = {}
end

local function hideAllTargets()
  mapView.m_NPC = {}
  mapManager.m_DynamicNPC = {}
  mapManager.m_CurMapFarNpc = {}
end

local function confirmCurrentTeleport()
  local teleport = lastSend(22568)
  assert(teleport and teleport.args[1] == 1018 and teleport.args[2] == 200 and teleport.args[3] == 300,
    'run did not send the expected target teleport')
  emit(65505, {mapId = 1018, posX = 200, posY = 300})
  mapView.m_LocalRole.m_Gx = 200
  mapView.m_LocalRole.m_Gy = 300
  advance(0.5)
  assert(readStatus().reason == 'position_confirmed', 'matching position was not confirmed before challenge')
end

local function beginAttempt(sequence, expectedTeleports)
  showOnlyReadyAsDynamic()
  setCommand(sequence, 'run', 21, 1018)
  advance(0.5)
  assert(countSends(22568) == expectedTeleports,
    'explicit run command did not start exactly one new teleport attempt')
  confirmCurrentTeleport()
end

beginAttempt(11, 1)
hideAllTargets()
advance(0.5)
local missing = readStatus()
assert(missing.status == 'waiting_targets' and missing.reason == 'challenge_target_rebinding',
  'a temporarily missing challenge target must enter bounded rebind wait')
assert(missing.targetId == '20' and missing.targetX == '200' and missing.targetY == '300',
  'rebind wait discarded the confirmed target identity or position')
assert(countSends(22568) == 1 and countSends(12344) == 0,
  'missing target triggered another teleport or premature challenge')
advance(0.5, 2)
assert(readStatus().status == 'waiting_targets' and countSends(12344) == 0,
  'rebind wait did not remain read-only before the grace limit')
mapView.m_NPC = {npcReady}
mapManager.m_DynamicNPC = {}
advance(0.5)
local reboundChallenge = lastSend(12344)
assert(countSends(22568) == 1 and countSends(12344) == 1,
  'the restored same-ID, same-coordinate target did not challenge exactly once')
assert(readStatus().status == 'challenge_wait' and readStatus().map == '1018' and readStatus().role == '101',
  'rebound challenge did not retain the validated map and role')
assert(reboundChallenge and reboundChallenge.args[1] == 20 and reboundChallenge.args[2] == 'threats_star'
  and reboundChallenge.args[3] == '', 'rebound target challenge arguments changed')

beginAttempt(12, 2)
hideAllTargets()
advance(0.5, 9)
local permanentMissing = readStatus()
assert(permanentMissing.status == 'unknown' and permanentMissing.reason == 'challenge_target_rebinding_timeout',
  'a permanently missing confirmed target did not stop in unknown after the bound')
assert(countSends(22568) == 2 and countSends(12344) == 1,
  'permanent target loss resent a teleport or issued a challenge')
advance(0.5, 4)
assert(countSends(22568) == 2 and countSends(12344) == 1,
  'unknown after permanent target loss resumed automatically')

beginAttempt(13, 3)
npcReady.m_NpcInfo.posX = 201
advance(0.5)
local movedTarget = readStatus()
assert(movedTarget.status == 'unknown'
  and movedTarget.reason == 'challenge_target_position_changed_after_confirmation',
  'a same-ID target whose coordinates changed was challenged or retried')
assert(countSends(22568) == 3 and countSends(12344) == 1,
  'target coordinate change caused a duplicate teleport or challenge')
advance(0.5, 4)
assert(countSends(22568) == 3 and countSends(12344) == 1,
  'unknown after target coordinate change resumed automatically')

beginAttempt(14, 4)
hideAllTargets()
advance(0.5)
assert(readStatus().reason == 'challenge_target_rebinding', 'position guard setup did not enter rebind wait')
mapView.m_NPC = {npcReady}
mapManager.m_DynamicNPC = {}
mapView.m_LocalRole.m_Gx = 201
advance(0.5)
local movedPlayer = readStatus()
assert(movedPlayer.status == 'unknown' and movedPlayer.reason == 'challenge_position_changed_after_confirmation',
  'a player who moved away during target rebind was challenged')
assert(countSends(22568) == 4 and countSends(12344) == 1,
  'player movement during rebind caused another teleport or challenge')

beginAttempt(15, 5)
npcReady.m_NpcInfo.title = '地煞21星无效'
advance(0.5)
assert(readStatus().status == 'waiting_targets' and readStatus().reason == 'challenge_target_rebinding',
  'an invalid target shape was not treated as temporarily absent')
advance(0.5, 8)
local invalidTarget = readStatus()
assert(invalidTarget.status == 'unknown' and invalidTarget.reason == 'challenge_target_rebinding_timeout',
  'an invalid target shape did not time out safely')
assert(countSends(22568) == 5 and countSends(12344) == 1,
  'invalid target data caused a repeated teleport or incorrect challenge')

expectedFinalSequence = '15'
