local state = assert(rawget(_G, '__codex_battle_loop_v1'), 'battle-loop state missing')
assert(state.hideBattleRequested == false, 'off scenario requires default/disabled cfg')
assert(#sendCalls == 0, 'unexpected send before battle start')
local result = testPack(ProtoFunc[3584]({token='start'}, nil, 7, false))
assert(original3584Calls == 1, '3584 original handler was not called exactly once')
assert(original3584Args.n == 4 and original3584Args[2] == nil and original3584Args[3] == 7 and original3584Args[4] == false,
    '3584 nil arguments were not preserved')
assert(result.n == 5 and result[1] == nil and result[2] == 'original_start' and result[3] == false
    and result[4] == nil and result[5] == 'tail', '3584 multiple return tuple changed')
assert(battleManager.visible == true, 'default-off changed original root visibility')
assert(g_CMainMenuHandler.m_MapLayout.visible == false and g_CMainMenuHandler.m_MapLayout.x == 90,
    'default-off changed original map-menu layout')
assert(g_CMainMenuHandler.m_LeftLayout.visible == true and g_CMainMenuHandler.m_LeftLayout.x == 92,
    'default-off changed original battle-menu layout')
assert(#sendCalls == 0, 'default-off added battle sends')
assert(readStatus().hideBattleRequested == 'false', 'default-off request status is not false')
assert(readStatus().visualState == 'disabled', 'default-off visual state is not disabled')
print('PASS default-off: original 3584 UI, arguments, returns and sends preserved')
