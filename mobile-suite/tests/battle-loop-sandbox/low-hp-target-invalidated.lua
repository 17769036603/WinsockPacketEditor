local function role(id, hp)
  local r = {m_RoleId=id, m_Hp=hp, m_Battlefield=battleManager, alive=true}
  r.getIsAlive = function(self) return self.alive end
  return r
end
battleManager.m_RoleIns[10001] = role(501, 12)
battleManager.m_RoleIns[10002] = role(502, 30)
advance(0.01)
offAck(4)
battleManager.m_RoleIns[10001].m_Hp = 0
advance(0.01)
assert(countCalls(3470, 101, 4) == 1 and tostring(lastCall(3470).args[4]) == '502', 'first send did not reselect a replacement after the chosen target reached zero HP')
print('PASS low HP safety: first send reselects a valid replacement')
