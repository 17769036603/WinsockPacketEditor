local function role(id, hp)
  local r = {m_RoleId=id, m_Hp=hp, m_Battlefield=battleManager, alive=true}
  r.getIsAlive = function(self) return self.alive end
  return r
end
battleManager.m_RoleIns[10001] = role(501, 12)
for pos=10002,10005 do battleManager.m_RoleIns[pos] = role(500 + pos, 10) end
advance(0.01)
offAck(4)
for pos=10001,10005 do battleManager.m_RoleIns[pos].m_Hp = 0 end
advance(0.01)
assert(countCalls(3470) == 0, 'action sent despite no living replacement target')
assert(battleManager.m_BattleUI.m_AutoStatus == true, 'auto battle was not restored after no target remained')
local state = readStatus().state
assert(state == 'waiting_battle' or state == 'stopping', 'loop did not wait for a later tick when no target remained')
local loopState = rawget(_G, '__codex_battle_loop_v1')
assert(loopState and not loopState.stopRequested, 'no target incorrectly stopped the battle loop')
battleManager.m_RoleIns[10003].alive = true
battleManager.m_RoleIns[10003].m_Hp = 7
advance(0.01)
offAck(4)
advance(0.01)
assert(countCalls(3470, 101, 4) == 1 and tostring(lastCall(3470).args[4]) == '10503', 'later tick did not resume after a valid target returned')
print('PASS low HP safety: no replacement restores auto without stopping; later tick resumes')
