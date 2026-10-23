local function makeUserdataNpc(info, fighting)
  local handle = assert(io.tmpfile(), 'could not allocate test userdata')
  local closeHandle = handle.close
  closeHandle(handle)
  local reads = 0
  local sceneNpc = handle
  debug.setmetatable(sceneNpc, {
    __index = function(_, key)
      if key == 'm_NpcInfo' then return info end
      if key == 'getIsFight' then
        return function(self)
          assert(self == sceneNpc, 'getIsFight must receive the scene userdata as self')
          reads = reads + 1
          return fighting()
        end
      end
      error('unexpected scene userdata property: ' .. tostring(key))
    end
  })
  return sceneNpc, function() return reads end
end

local busyInfo = makeInfo(77, '地煞21星', '地煞二十一星甲', 150, 160)
local readyInfo = makeInfo(78, '地煞21星', '地煞二十一星乙', 250, 260)
local sceneBusy = true
local busyNpc, busyReads = makeUserdataNpc(busyInfo, function() return sceneBusy end)
local brokenHandle = assert(io.tmpfile(), 'could not allocate broken userdata')
local closeBrokenHandle = brokenHandle.close
closeBrokenHandle(brokenHandle)
debug.setmetatable(brokenHandle, {__index = function() error('broken scene property') end})

mapView.m_NPC = {brokenHandle, busyNpc}
mapManager.m_DynamicNPC = {{m_NpcInfo = busyInfo}, {m_NpcInfo = readyInfo}, {m_NpcInfo = busyInfo}}
mapManager.m_CurMapFarNpc = {{m_NpcInfo = busyInfo}}
advance(0.5)
assert(readStatus().levels == '21:2',
  'scene userdata must be scanned and duplicate dynamic/far IDs counted only once')

setCommand(11, 'run', 21, 1018)
advance(0.5)
local busySkip = readStatus()
assert(busyReads() > 0, 'scene userdata getIsFight was not consulted')
assert(busySkip.skipped == '1' and busySkip.targetId == '78' and countSends(22568) == 1,
  'a busy scene userdata target must be skipped in favor of the next distinct target')
assert(lastSend(22568).args[2] == 250 and lastSend(22568).args[3] == 260,
  'busy target skip changed the selected target teleport coordinates')

setCommand(12, 'scan', 0, 0)
advance(0.5)
assert(readStatus().status == 'reading' and netconn.useFunc == originalUseBefore,
  'scan command did not end the busy-target run cleanly')

sceneBusy = false
mapView.m_NPC = {}
mapManager.m_DynamicNPC = {{m_NpcInfo = busyInfo}}
mapManager.m_CurMapFarNpc = {}
setCommand(13, 'run', 21, 1018)
advance(0.5)
assert(countSends(22568) == 2 and lastSend(22568).args[2] == 150 and lastSend(22568).args[3] == 160,
  'far dynamic table target did not start the userdata handoff scenario')

mapManager.m_DynamicNPC = {{m_NpcInfo = readyInfo}}
mapView.m_NPC = {brokenHandle, busyNpc}
assert(emit(65505, {mapId = 1018, posX = 150, posY = 160}) == 'original-first',
  'the inbound observer did not preserve the original result during the scene handoff')
mapView.m_LocalRole.m_Gx = 150
mapView.m_LocalRole.m_Gy = 160
advance(0.5)
assert(readStatus().levels == '21:2', 'handoff scan lost or double-counted the scene userdata target')
assert(countSends(12344) == 0, 'position confirmation should precede the challenge tick')
advance(0.5)
local challenge = lastSend(12344)
assert(countSends(22568) == 2 and countSends(12344) == 1,
  'the dynamic-to-scene userdata handoff must produce one teleport and one challenge')
assert(challenge and challenge.args[1] == 77 and challenge.args[2] == 'threats_star' and challenge.args[3] == '',
  'scene userdata handoff changed the confirmed challenge arguments')
advance(0.5, 3)
assert(countSends(22568) == 2 and countSends(12344) == 1,
  'a completed userdata challenge was resent')
expectedFinalSequence = '13'

