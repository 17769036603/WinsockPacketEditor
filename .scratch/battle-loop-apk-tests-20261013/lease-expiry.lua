advance(0.01)
assert(countCalls(3473) == 1, 'assistant did not start before lease expiry')
now = 106
advance(0.01)
local st = readStatus()
assert(st.state == 'stopping' and st.reason == 'lease_expired', 'expired lease did not request cleanup')
assert(countCalls(3473) == 2, 'lease expiry did not request auto restore')
battleManager.m_BattleUI.m_AutoStatus = true
advance(0.01)
assert(rawget(_G, '__codex_battle_loop_v1') == nil, 'lease expiry did not finish cleanup after restore')
local cleared = fakeFiles.binding or ''
assert(cleared:find('player=' .. string.char(10), 1, true) and cleared:find('manager=' .. string.char(10), 1, true), 'lease expiry did not clear binding identity')
assert(fakeTimerCancelled, 'lease expiry did not unschedule timer')
print('PASS lease: expired lease requests restore and cleans after confirmation')
