startAnimationRound(4, false)
finishAnimationRound(4, false, {playIndex=2, cmdIndex=2, finished=false})
assert(countCalls(3470, 101, 4) == 1 and countCalls(3470, 202, 4) == 0, 'no-pet scenario included a pet action')
assert(fastCalls == 1 and battleManager.m_AnalyzeFinish == true, 'no-pet round did not complete pending animation')
assert(countPlayovers(4) == 1, 'no-pet round did not emit exactly one playover')
print('PASS no pet animation: character-only round completed and emitted one playover')

