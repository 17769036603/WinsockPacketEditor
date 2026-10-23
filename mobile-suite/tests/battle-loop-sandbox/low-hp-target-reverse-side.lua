local function role(id, hp, localRole)
  local r = {m_RoleId=id, m_Hp=hp, m_IsLocalPlayer=localRole == true, m_Battlefield=battleManager, alive=true}
  r.getIsAlive = function(self) return self.alive end
  return r
end
battleManager.m_RoleIns[1] = role(601, 28)
battleManager.m_RoleIns[2] = role(602, 9)
battleManager.m_RoleIns[10001] = nil
battleManager.m_RoleIns[10002] = role(101, 100, true)
battleManager.m_RoleIns[10003] = role(202, 100)
battleManager.m_PlayerPos = 10002
battleManager.m_PetPos = 10003
battleManager.m_RoleIdPosDict[101] = {10002}
advance(0.01)
offAck(4)
advance(0.01)
local calls = {}
for _, c in ipairs(sendCalls) do if c.op == 3470 then calls[tostring(c.args[3])] = c end end
assert(calls['101'] and tostring(calls['101'].args[4]) == '602', 'reverse-side player did not select lowest HP from positions 1..5')
assert(calls['202'] and tostring(calls['202'].args[4]) == '602', 'reverse-side pet did not use shared selector')
print('PASS low HP reverse side: bounded opposing range applies to player and pet')
