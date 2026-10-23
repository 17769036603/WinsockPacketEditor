-- Ordinary NPCs should be rejected by title before detailed properties are touched.
local reads = 0
local ordinary = setmetatable({}, {__index = function(_, key)
  if key == 'title' then return '村民' end
  if key == 'roleId' or key == 'mapId' or key == 'posX' or key == 'posY' or key == 'name' then
    reads = reads + 1
  end
  return ({roleId = 900, mapId = 1018, posX = 1, posY = 2, name = '普通NPC'})[key]
end})
mapManager.m_DynamicNPC[#mapManager.m_DynamicNPC + 1] = ordinary
advanceScheduled(1)
assert(reads == 0, 'non-star NPC read detailed fields ' .. tostring(reads) .. ' times')
local levels = readStatus().levels or ''
assert(not levels:find('900', 1, true), 'ordinary NPC must not be collected as a star target')
assert(levels:find('21:2', 1, true), 'valid targets must continue to be collected')
setCommand(11, 'run', 21, 1018)
advanceScheduled(1)
assert(lastSend(22568).args[2] == 200 and lastSend(22568).args[3] == 300,
  'scene NPC must retain priority for a duplicate target')
expectedFinalSequence = '11'
