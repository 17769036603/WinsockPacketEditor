startAnimationRound(4, true)
naturalPlayoverOnCommands = true
completeActorActions(4, false)
battleManager.m_CmdRound = 4
battleManager.m_RoundCmdList = {{round=4,cmd='actor1'},{round=4,cmd='actor2'}}
battleManager.m_CurCmdPlayIndex = 2
battleManager.m_CurCmdIndex = 2
battleManager.m_PauseAnalyze = false
battleManager.m_AnalyzeFinish = false
ProtoFunc[3585]({round=4}) -- natural callback arrives before pet ack and animation completion
assert(countCalls(3472) == 0, 'early natural playover escaped before prerequisites')
actorEvent(202, 4, 1)
naturalPlayoverOnFastAnalyze = true -- callback also arrives during completion
advance(0.01, 3)
assert(fastCalls == 1 and naturalPlayoverCount == 2, 'original callback was not preserved before/during fast completion')
assert(countPlayovers(4) == 1, 'natural and module playover paths were not deduplicated')
ProtoFunc[3585]({round=4}) -- late duplicate callback
assert(originalCalls[3585] >= 2, 'late original callback was swallowed')
assert(countPlayovers(4) == 1, 'late natural playover was not deduplicated')
print('PASS natural playover: early/during/late callback preserved and deduplicated')

