-- 服务先停扫描、再以 TELEPORT 作为新 worker 的首个命令启动时，
-- Lua 必须离开默认 scan 并真正发送一次跳图。
local s = rawget(_G, '__codex_starhunt_test_v1')
assert(s and s.mode == 'teleport',
  'fresh teleport baseline stayed in ' .. tostring(s and s.mode))
assert(readStatus().acceptedSequence == '10',
  'fresh teleport baseline did not adopt the command sequence')
assert(countSends(22568) == 0, 'teleport must wait for the first scheduled tick')
-- 发送时场景允许操作；落地后目标场景返回队伍限制，不能因此把已到达误判为 waiting_role。
local teamBlocked = false
mapManager.canNotOperationTeam = function() return teamBlocked end
advanceScheduled(1)
local started = readStatus()
assert(started.status == 'teleport_wait' and started.reason == 'jump_sent',
  'fresh teleport baseline did not start: ' .. tostring(started.status) .. '/' .. tostring(started.reason))
assert(countSends(22568) == 1,
  'fresh teleport baseline sent ' .. tostring(countSends(22568)) .. ' jumps')
local jump = lastSend(22568)
assert(jump.args[2] == 50 and jump.args[3] == 60,
  'fresh teleport used the wrong start position')
teamBlocked = true
changeMap(1007)
advanceScheduled(1)
local arrived = readStatus()
assert(arrived.status == 'stopped' and arrived.reason == 'teleported',
  'arrival was not confirmed: ' .. tostring(arrived.status) .. '/' .. tostring(arrived.reason))
assert(arrived.acceptedSequence == '10', 'arrival lost the accepted sequence')
expectedFinalSequence = '10'
