
local state = assert(rawget(_G, '__codex_battle_loop_v1'), 'battle-loop state missing')
assert(#sendCalls == 0, 'unexpected send before visual test')
local start = testPack(ProtoFunc[3584]({token='start'}, nil, 7, false))
assert(original3584Calls == 1, '3584 original was not called exactly once')
assert(original3584Args.n == 4 and original3584Args[2] == nil and original3584Args[3] == 7 and original3584Args[4] == false,
    '3584 nil-containing arguments changed')
assert(start.n == 5 and start[1] == nil and start[2] == 'original_start' and start[3] == false
    and start[4] == nil and start[5] == 'tail', '3584 multiple returns changed')
assert(battleManager.visible == false, 'enabled mode did not hide battle root after original handler')
assert(g_CMainMenuHandler.m_MapLayout.visible == true and g_CMainMenuHandler.m_MapLayout.x == 10
    and g_CMainMenuHandler.m_MapLayout.y == 11, 'hidden battle did not restore exploration map menu')
assert(g_CMainMenuHandler.m_LeftLayout.visible == false and g_CMainMenuHandler.m_LeftLayout.x == 12,
    'hidden battle did not restore exploration left menu')
assert(#sendCalls == 0, 'visual handler added a send')
assert(setInWar and JudgeIsInWar() == true, 'visual handler changed battle state')
assert(state.hideBattleRequested == true and state.visualState == 'hidden' and state.hiddenBattles == 1,
    'first hidden battle state was not recorded')

-- Same-identity same-battle redraws must be hidden again by the observer.
battleManager:setVisible(true)
advance(0.11)
assert(battleManager.visible == false, 'same-battle redraw exposed the battle root')

battleManager.m_BattleId = 78
local endResult = testPack(ProtoFunc[3585]({round=4}, nil, 'end-tail'))
assert(original3585Calls == 1, '3585 original end handler was not called exactly once')
assert(endResult.n == 3 and endResult[1] == 'original_end' and endResult[2] == nil and endResult[3] == true,
    '3585 multiple returns changed')
assert(battleManager.visible == false and g_CMainMenuHandler.m_MapLayout.visible == true
    and g_CMainMenuHandler.m_MapLayout.x == 10, 'natural end handler UI was overwritten')
assert(JudgeIsInWar() == false, 'visual handler changed natural battle state')
advance(0.01)
assert(rawget(_G, '__codex_battle_loop_v1') == state, 'visual module uninstalled after natural battle end')
assert(state.hiddenBattles == 1 and state.visualState ~= 'hidden', 'natural battle end state was not recorded')


-- A later encounter must be hidden by the same still-installed wrapper.
local second = testPack(ProtoFunc[3584]({token='second'}, nil, 8, false))
assert(second.n == 5 and second[1] == nil and second[2] == 'original_start' and second[5] == 'tail',
    'second battle original return tuple changed')
assert(original3584Calls == 2 and state.hiddenBattles == 2, 'second battle was not captured')
assert(battleManager.visible == false and g_CMainMenuHandler.m_MapLayout.visible == true,
    'second battle was not hidden while restoring exploration menu')
print('PASS enabled visuals: handler tuples, no sends, natural end and second battle')
