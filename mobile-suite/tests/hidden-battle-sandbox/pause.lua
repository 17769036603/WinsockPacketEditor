local state = assert(rawget(_G, '__codex_battle_loop_v1'), 'battle-loop state missing')
ProtoFunc[3584]({token='start'})
assert(battleManager.visible == false, 'pause fixture was not hidden first')
fakeFiles['codex-battle-loop-pause'] = 'paused'
advance(0.01)
assert(battleManager.visible == true, 'pause did not restore active battle root')
assert(g_CMainMenuHandler.m_MapLayout.visible == false and g_CMainMenuHandler.m_MapLayout.x == 90,
    'pause did not restore active battle map menu')
assert(g_CMainMenuHandler.m_LeftLayout.visible == true and g_CMainMenuHandler.m_LeftLayout.x == 92,
    'pause did not restore active battle control menu')
assert(state.visualState == 'restored_paused' and state.visibilityRestoreConfirmed == true,
    'pause restore state was not confirmed')
local hiddenCount = state.hiddenBattles
fakeFiles['codex-battle-loop-pause'] = nil
advance(0.01)
assert(battleManager.visible == true and state.hiddenBattles == hiddenCount,
    'resume re-hid the already-running battle')
assert(state.visualState ~= 'hidden', 'resume did not wait until next battle')
print('PASS pause: restore current battle and do not re-hide on resume')
