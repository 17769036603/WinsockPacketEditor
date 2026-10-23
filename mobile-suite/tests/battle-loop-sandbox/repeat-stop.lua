local scenario = repeatStopScenario
local function statusFields()
  return readStatus()
end

if scenario == 'completed-run' then
  assert(fakeFiles.result == repeatReceiptBefore, 'repeated stop overwrote the complete cleanup receipt')
  assert(#sendCalls == repeatSendsBefore, 'repeated stop sent an additional packet')
  local after = statusFields()
  assert(after.restoreConfirmed == 'true' and after.hooksRemoved == 'true' and after.bindingCleared == 'true',
    'repeated stop overwrote the complete cleanup receipt')
elseif scenario == 'same-run-complete' then
  local f = statusFields()
  assert(f.state == 'stopped' and f.restoreConfirmed == 'true', 'same-run complete receipt was not preserved')
  assert(netconn.send == originalSend, 'same-run stop changed send hook')
elseif scenario == 'cross-run-complete' then
  local f = statusFields()
  assert(f.restoreConfirmed ~= 'true', 'cross-run receipt incorrectly claimed restoration')
elseif scenario == 'duplicate-key' then
  local f = statusFields()
  assert(f.restoreConfirmed ~= 'true', 'duplicate restoreConfirmed key incorrectly claimed restoration')
elseif scenario == 'current-incomplete' then
  local f = statusFields()
  assert(f.restoreConfirmed ~= 'true', 'incomplete current run was incorrectly reported restored')
  assert(ProtoFunc[3587] == retainedHandler and netconn.send == retainedSend, 'unconfirmed recovery changed retained hooks')
elseif scenario == 'no-receipt' then
  local f = statusFields()
  assert(f.restoreConfirmed ~= 'true', 'missing receipt was incorrectly reported restored')
  assert(netconn.send == retainedSend and ProtoFunc[3587] == retainedHandler, 'missing receipt recovery changed retained hooks')
end
print('PASS repeat-stop: ' .. scenario)
