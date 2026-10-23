-- 已经在目标地图但不在中心时，中心落点地图仍要发一次同图跳转，移动到中心后收尾。
local s = rawget(_G, '__codex_starhunt_test_v1')
assert(s and s.mode == 'teleport',
  'fresh teleport baseline stayed in ' .. tostring(s and s.mode))
assert(readStatus().acceptedSequence == '10',
  'fresh teleport baseline did not adopt the command sequence')
changeMap(4154)
mapView.m_LocalRole.m_Gx = 20
mapView.m_LocalRole.m_Gy = 15
advanceScheduled(1)
local jump = lastSend(22568)
assert(jump, 'same-map center move did not send a jump')
assert(jump.args[2] == 60 and jump.args[3] == 45,
  'expected same-map center (60,45), got (' .. tostring(jump.args[2]) .. ',' .. tostring(jump.args[3]) .. ')')
local waiting = readStatus()
assert(waiting.status == 'teleport_wait' and waiting.reason == 'jump_sent',
  'same-map center move did not wait: ' .. tostring(waiting.status) .. '/' .. tostring(waiting.reason))
mapView.m_LocalRole.m_Gx = 60
mapView.m_LocalRole.m_Gy = 45
advanceScheduled(1)
local arrived = readStatus()
assert(arrived.status == 'stopped' and arrived.reason == 'teleported',
  'same-map center arrival was not confirmed: ' .. tostring(arrived.status) .. '/' .. tostring(arrived.reason))
expectedFinalSequence = '10'
