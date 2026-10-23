advance(0.01)
assert(countCalls(3473) == 1, 'assistant did not disable auto before identity change')
local sentBeforeIdentityChange = #sendCalls
activePlayer = {getPlayerId = function() return 909 end}
advance(0.01)
assert(#sendCalls == sentBeforeIdentityChange, 'identity change sent a restore or action operation to the new role')
assert(rawget(_G, '__codex_battle_loop_v1') == nil, 'identity change did not release owned state')
for id, fn in pairs(originalHandlers) do assert(ProtoFunc[id] == fn, 'identity change did not restore ProtoFunc ' .. id) end
assert(netconn.send == originalSend, 'identity change did not restore send hook')
assert(fakeTimerCancelled, 'identity change did not unschedule timer')
local binding = fakeFiles.binding or ''
assert(binding:find('player=' .. string.char(10), 1, true) and binding:find('manager=' .. string.char(10), 1, true), 'identity change did not clear binding identity')
local st = readStatus()
assert(st.state == 'stopped', 'identity change cleanup did not finish')
assert(st.restoreConfirmed == 'false', 'identity change did not report the pending restore as unconfirmed')
assert(st.hooksRemoved == 'true' and st.bindingCleared == 'true', 'identity change did not report its cleanup')
print('PASS safety cleanup: changed identity gets no restore send, owned hooks/binding clear, restore remains unconfirmed')
