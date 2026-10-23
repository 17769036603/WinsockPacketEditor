startAnimationRound(4, true)
completeActorActions(4, true)
battleManager.m_CmdRound = 4
battleManager.m_RoundCmdList = {}
battleManager.m_CurCmdPlayIndex = 2 -- count + 1 after natural completion cleared the command list
battleManager.m_CurCmdIndex = 2
battleManager.m_PauseAnalyze = false
battleManager.m_AnalyzeFinish = true
ProtoFunc[3585]({round=4})
advance(0.01, 3)
assert(fastCalls == 0, 'completed/cleared round must not call fast analyzer')
assert(countPlayovers(4) == 1, 'completed round with cleared commands and exhausted index did not emit one playover')
assert(rawget(_G, '__codex_battle_loop_v1').target == nil, 'completed round remained pending after playover')
print('PASS finished exhausted index: empty command list and idx=count+1 still finalize once without fast analysis')
