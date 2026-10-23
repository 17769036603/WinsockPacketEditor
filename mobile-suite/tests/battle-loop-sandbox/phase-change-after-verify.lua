startAnimationRound(4, true)
completeActorActions(4, true)
battleManager.m_CmdRound = 4
battleManager.m_RoundCmdList = {{round=4,cmd='actor1'},{round=4,cmd='actor2'}}
battleManager.m_CurCmdPlayIndex = 2
battleManager.m_CurCmdIndex = 2
battleManager.m_PauseAnalyze = false
battleManager.m_AnalyzeFinish = false
local inheritedVerify = battleManager.verifyRoundData
battleManager.verifyRoundData = function(self, fromLoop)
  inheritedVerify(self, fromLoop)
  self.m_BattleId = 78 -- simulated battle phase change during verification
  return true
end
ProtoFunc[3585]({round=4})
advance(0.01, 3)
assert(fastCalls == 1 and verifyCalls == 1, 'round completion sequence did not reach verify')
assert(lastVerifyFromLoop == true, 'verification did not use loop mode')
assert(countPlayovers(4) == 0, 'playover sent after verifyRoundData changed battle identity')
assert(battleManager.m_BattleUI.m_AutoStatus == true, 'verify-phase change left auto mode off')
assert(rawget(_G, '__codex_battle_loop_v1') == nil, 'verify-phase change did not stop and clean the owned loop')
for id, fn in pairs(originalHandlers) do assert(ProtoFunc[id] == fn, 'verify-phase change did not restore ProtoFunc ' .. id) end
print('PASS phase change after verify: changed battle identity blocks playover')


