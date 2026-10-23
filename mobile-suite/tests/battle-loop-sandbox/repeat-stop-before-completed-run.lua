repeatStopScenario = 'completed-run'
if repeatStopScenario == 'completed-run' then
advance(0.01)
assert(countCalls(3473) == 1, 'expected auto disable')
battleManager.m_BattleUI.m_AutoStatus = true
local s = rawget(_G, '__codex_battle_loop_v1')
assert(s, 'initial run state missing')
s.stopRequested = true
advance(0.01, 2)
repeatReceiptBefore = fakeFiles.result
repeatSendsBefore = #sendCalls
local r = readStatus()
assert(r.state == 'stopped' and r.hooksRemoved == 'true' and r.bindingCleared == 'true' and r.restoreConfirmed == 'true',
  'first cleanup receipt incomplete: ' .. tostring(r.hooksRemoved) .. '/' .. tostring(r.bindingCleared) .. '/' .. tostring(r.restoreConfirmed))
elseif repeatStopScenario == 'prior-complete' then
  fakeFiles.result = 'state=stopped\nstatus=stopped\nrunId=test-run\nhooksRemoved=true\nbindingCleared=true\nreason=done\ncalls=2\nrestoreConfirmed=true\n'
elseif repeatStopScenario == 'current-incomplete' then
  fakeFiles.result = 'state=stopped\nstatus=stopped\nrunId=test-run\nhooksRemoved=true\nbindingCleared=true\nreason=restore_unconfirmed\ncalls=2\nrestoreConfirmed=false\n'
end

