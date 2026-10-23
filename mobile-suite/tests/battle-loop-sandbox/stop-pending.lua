local state = rawget(_G, '__codex_battle_loop_v1')
assert(state ~= nil and state.stopRequested == true, 'stop did not request cleanup for pending round')
advance(0.01, 2)
assert(rawget(_G, '__codex_battle_loop_v1') == nil, 'stop while pending did not clear owned state')
assert(battleManager.m_BattleUI.m_AutoStatus == true, 'stop while pending did not restore auto mode')
assert(countCalls(3472) == 0, 'stop while pending emitted a playover')
for id, fn in pairs(originalHandlers) do assert(ProtoFunc[id] == fn, 'stop while pending did not restore ProtoFunc ' .. id) end
assert(netconn.send == originalSend, 'stop while pending did not restore netconn hook')
assert(fakeTimerCancelled, 'stop while pending did not unschedule timer')
print('PASS stop pending: auto mode, owned hooks, and timer restored without playover')

