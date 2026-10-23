-- 指定地图（4154 比武场）直接传送时落在场地中央，而不是地图 startPos 边缘。
local s = rawget(_G, '__codex_starhunt_test_v1')
assert(s and s.mode == 'teleport',
  'fresh teleport baseline stayed in ' .. tostring(s and s.mode))
assert(readStatus().acceptedSequence == '10',
  'fresh teleport baseline did not adopt the command sequence')
advanceScheduled(1)
local jump = lastSend(22568)
assert(jump, 'center teleport did not send a jump')
assert(jump.args[2] == 60 and jump.args[3] == 45,
  'expected center (60,45), got (' .. tostring(jump.args[2]) .. ',' .. tostring(jump.args[3]) .. ')')
mapView.m_LocalRole.m_Gx = 60
mapView.m_LocalRole.m_Gy = 45
changeMap(4154)
advanceScheduled(1)
local arrived = readStatus()
assert(arrived.status == 'stopped' and arrived.reason == 'teleported',
  'arrival was not confirmed: ' .. tostring(arrived.status) .. '/' .. tostring(arrived.reason))
expectedFinalSequence = '10'
