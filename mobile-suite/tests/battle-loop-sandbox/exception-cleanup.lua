startAnimationRound(4, true)
completeActorActions(4, true)
battleManager.m_CmdRound = 4
battleManager.m_RoundCmdList = {{round=4,cmd='actor1'},{round=4,cmd='actor2'}}
battleManager.m_CurCmdPlayIndex = 2
battleManager.m_CurCmdIndex = 2
battleManager.m_PauseAnalyze = false
battleManager.m_AnalyzeFinish = false
battleManager.fastAnalyzeSurplusCMD = function() error('fixture_fast_analyzer_failure') end
ProtoFunc[3585]({round=4})
advance(0.01, 2)
assert(countPlayovers(4) == 0, 'playover emitted after fast analyzer exception')
assert(battleManager.m_BattleUI.m_AutoStatus == true, 'exception did not restore original auto mode')
assert(rawget(_G, '__codex_battle_loop_v1') == nil, 'exception did not clean owned state')
for id, fn in pairs(originalHandlers) do assert(ProtoFunc[id] == fn, 'exception did not restore owned ProtoFunc ' .. id) end
assert(netconn.send == originalSend, 'exception did not restore owned netconn hook')
print('PASS exception cleanup: auto mode and owned hooks restored without playover')


