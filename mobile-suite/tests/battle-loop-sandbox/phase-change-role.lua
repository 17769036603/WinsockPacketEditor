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
  activePlayer = {getPlayerId=function() return 101 end} -- same role value, different player identity
end
ProtoFunc[3585]({round=4})
advance(0.01, 3)
assert(fastCalls == 1, 'fast analyzer was not called before player identity changed')
assert(countPlayovers(4) == 0, 'playover sent after player identity changed during fast analysis')
assert(battleManager.m_BattleUI.m_AutoStatus == true, 'player identity change left auto mode off')
assert(rawget(_G, '__codex_battle_loop_v1') == nil, 'player identity change did not stop and clear the loop')
for id, fn in pairs(originalHandlers) do assert(ProtoFunc[id] == fn, 'player identity change did not restore ProtoFunc ' .. id) end
assert(netconn.send == originalSend, 'player identity change did not restore netconn hook')
print('PASS phase identity change: same role id with new player object aborts safely after fast analysis')
