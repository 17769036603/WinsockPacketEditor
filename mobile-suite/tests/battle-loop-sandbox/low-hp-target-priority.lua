local function role(id, hp, alive)
  local r = {m_RoleId=id, m_Hp=hp, m_Battlefield=battleManager, alive=alive ~= false}
  r.getIsAlive = function(self) return self.alive end
  return r
end
battleManager.m_RoleIns[10001] = role(501, 40)
battleManager.m_RoleIns[10002] = role(502, {17})
battleManager.m_RoleIns[10003] = role(503, '17')
battleManager.m_RoleIns[10004] = role(504, 0)
battleManager.m_RoleIns[10005] = role(505, -3)
battleManager.m_RoleIns[10004].alive = false
advance(0.01)
offAck(4)
advance(0.01)
assert(tostring(lastCall(3470).args[4]) == '502', 'lowest finite positive HP did not win; equal HP must retain position order')
assert(tostring(lastCall(3470).args[3]) == '202', 'pet action did not use shared low-HP selector')
local playerCall
for _, c in ipairs(sendCalls) do if c.op == 3470 and tostring(c.args[3]) == '101' then playerCall = c end end
assert(playerCall and tostring(playerCall.args[4]) == '502', 'character action did not use shared low-HP selector')
print('PASS low HP priority: bounded candidates, stable tie, number/table/string HP, character and pet')
