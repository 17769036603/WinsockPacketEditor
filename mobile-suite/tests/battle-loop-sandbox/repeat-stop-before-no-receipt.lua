repeatStopScenario = 'no-receipt'
retainedSend = function() end
retainedHandler = function() end
netconn.send = retainedSend
ProtoFunc[3587] = retainedHandler
if repeatStopScenario == 'completed-run' then
  dofile(repeatStopCleanupFile)
  local s = rawget(_G, '__codex_battle_loop_v1')
  assert(s, 'initial run state missing')
  s.stopRequested = true
  advance(0.01)
elseif repeatStopScenario == 'prior-complete' then
  fakeFiles.result = 'state=stopped\nstatus=stopped\nrunId=old-run\nhooksRemoved=true\nbindingCleared=true\nreason=done\ncalls=2\nrestoreConfirmed=true\n'
elseif repeatStopScenario == 'current-incomplete' then
  fakeFiles.result = 'state=stopped\nstatus=stopped\nrunId=test-run\nhooksRemoved=true\nbindingCleared=true\nreason=restore_unconfirmed\ncalls=2\nrestoreConfirmed=false\n'
end

