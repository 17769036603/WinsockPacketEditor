startAnimationRound(4, true)
completeActorActions(4, true)
battleManager.m_CmdRound = 4
battleManager.m_RoundCmdList = {}
battleManager.m_CurCmdPlayIndex = 1
battleManager.m_CurCmdIndex = 1
battleManager.m_PauseAnalyze = false
battleManager.m_AnalyzeFinish = false
ProtoFunc[3585]({round=4})
advance(0.01, 2)
assert(fastCalls == 0 and countCalls(3472) == 0, 'empty command list must wait')
battleManager.m_RoundCmdList = {{round=4,cmd='actor'}}
battleManager.m_CurCmdPlayIndex = 0
battleManager.m_CurCmdIndex = 0
advance(0.01, 2)
assert(fastCalls == 0 and countCalls(3472) == 0, 'play cursor zero must wait')
assert(battleManager.m_CurCmdPlayIndex == 0, 'wait path must not force play cursor to one')
battleManager.m_CurCmdPlayIndex = 1
battleManager.m_CurCmdIndex = 1
battleManager.m_PauseAnalyze = true
advance(0.01, 2)
assert(fastCalls == 0 and countCalls(3472) == 0, 'paused animation must wait')
battleManager.m_PauseAnalyze = false
advance(0.01, 3)
assert(fastCalls == 1 and countPlayovers(4) == 1, 'valid unpaused round did not resume and finish')
print('PASS animation waits: empty list, idx zero, and pause block fast completion without cursor mutation')

