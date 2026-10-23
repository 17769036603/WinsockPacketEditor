local function role(id, hp)
  local r = {m_RoleId=id, m_Hp=hp, m_Battlefield=battleManager, alive=true}
  r.getIsAlive = function(self) return self.alive end
  return r
end
battleManager.m_RoleIns[10001] = role(501, 12)
battleManager.m_RoleIns[10002] = role(502, 30)
advance(0.01)
offAck(4)
advance(0.01)
local originalTarget = lastCall(3470).args[4]
battleManager.m_RoleIns[10001].m_Hp = 0
actorEvent(101, 4, 3)
advance(0.05)
local playerCalls = {}
for _, c in ipairs(sendCalls) do if c.op == 3470 and tostring(c.args[3]) == '101' then playerCalls[#playerCalls + 1] = c end end
assert(#playerCalls == 2, 'rejected character action was not retried')
assert(tostring(playerCalls[2].args[4]) == tostring(originalTarget), 'retry changed the already selected target ID')
print('PASS low HP safety: retries retain the original target ID')
