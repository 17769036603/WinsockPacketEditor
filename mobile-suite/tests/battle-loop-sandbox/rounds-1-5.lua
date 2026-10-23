battleManager.m_CurRoundNum = 1
local initialVerifyCalls = verifyCalls
for round = 1, 5 do
  startAnimationRound(round, true)
  finishAnimationRound(round, true, {playIndex=2, cmdIndex=2, finished=false})
  assert(battleManager.m_AnalyzeFinish == true, 'animation was not completed in round ' .. round)
  assert(battleManager.m_CurCmdPlayIndex == 2, 'play cursor was reset in round ' .. round)
  assert(fastCalls == round, 'pending animation was not fast-completed exactly once in round ' .. round)
  assert(verifyCalls == initialVerifyCalls + round and lastVerifyFromLoop == true, 'verifyRoundData(true) missing in round ' .. round)
  assert(countPlayovers(round) == 1, 'round emitted other than one playover in round ' .. round)
end
print('PASS animation rounds: same battle rounds 1..5, partial cursor retained, one verified playover each')

