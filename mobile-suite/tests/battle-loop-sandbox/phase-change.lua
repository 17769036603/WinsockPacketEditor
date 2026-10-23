startAnimationRound(4, true)
completeActorActions(4, true)
battleManager.m_CmdRound = 4
battleManager.m_RoundCmdList = {{round=4,cmd='actor1'},{round=4,cmd='actor2'}}
battleManager.m_CurCmdPlayIndex = 2
battleManager.m_CurCmdIndex = 2
battleManager.m_PauseAnalyze = false
battleManager.m_AnalyzeFinish = false
battleManager.fastAnalyzeSurplusCMD = function(self)
  fastCalls = fastCalls + 1
  self.m_AnalyzeFinish = true
  self.m_CurRoundNum = 5 -- simulated round phase change during fast analysis
end
ProtoFunc[3585]({round=4})
advance(0.01, 3)
assert(fastCalls == 1, 'fast analyzer was not called for pending round')
assert(countPlayovers(4) == 0, 'playover sent for stale round after fast analysis changed CurRound')
assert(battleManager.m_BattleUI.m_AutoStatus == true, 'fast-analysis phase change left auto mode off')
assert(rawget(_G, '__codex_battle_loop_v1') == nil, 'fast-analysis phase change did not stop and clean the owned loop')
for id, fn in pairs(originalHandlers) do assert(ProtoFunc[id] == fn, 'fast-analysis phase change did not restore ProtoFunc ' .. id) end
print('PASS phase change: stale round after fast analysis is rejected before playover')


