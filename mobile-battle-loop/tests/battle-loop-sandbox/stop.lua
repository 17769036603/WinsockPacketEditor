assert(rawget(_G, '__codex_battle_loop_v1') ~= nil, 'stop command discarded owned state before cleanup')
assert(rawget(_G, '__codex_battle_loop_v1').stopRequested == true, 'stop mode did not request shutdown')
advance(0.01)
assert(rawget(_G, '__codex_battle_loop_v1') == nil, 'stop did not clear owned state')
for id, fn in pairs(originalHandlers) do assert(ProtoFunc[id] == fn, 'stop did not restore ProtoFunc ' .. id) end
assert(netconn.send == originalSend, 'stop did not restore the original send hook')
assert(fakeTimerCancelled, 'stop did not unschedule timer')
local cleared = fakeFiles.binding or ''
assert(cleared:find('player=' .. string.char(10), 1, true) and cleared:find('manager=' .. string.char(10), 1, true), 'stop did not clear binding identity')
assert(readStatus().state == 'stopped', 'stop did not publish stopped state')
print('PASS cleanup: stop restores owned hooks and clears binding identity')
