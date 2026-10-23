local function role(id, hp)
  local r = {m_RoleId=id, m_Hp=hp, m_Battlefield=battleManager, alive=true}
  r.getIsAlive = function(self) return self.alive end
  return r
end
battleManager.m_RoleIns[10001] = role(501, 0)
battleManager.m_RoleIns[10002] = role(502, -2)
battleManager.m_RoleIns[10003] = role(503, 0/0)
battleManager.m_RoleIns[10004] = role(504, math.huge)
battleManager.m_RoleIns[10005] = role(505, nil)
advance(0.01)
offAck(4)
advance(0.01)
assert(countCalls(3470, 101, 4) == 1 and tostring(lastCall(3470).args[4]) == '503', 'invalid known HP values did not fall back to first legal candidate')
print('PASS low HP fallback: excludes zero/negative and NaN/inf, rejects target that reaches zero, preserves unknown HP fallback')
