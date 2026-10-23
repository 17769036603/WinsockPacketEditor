-- 回合开场 m_PlayerPos 暂时无效时，仍应按 roleId 在场上找到本机角色并正常出手，
-- 而不是静默停在 waiting_role、等客户端自动战斗抢先提交本回合。
battleManager.m_PlayerPos = 0
advance(0.01)
assert(countCalls(3473) == 1, 'invalid player position blocked the auto-off request')
offAck(4)
advance(0.01)
assert(countCalls(3470, 101, 4, 111) == 1, 'invalid player position blocked the character action')
assert(countCalls(3470, 202, 4, 222) == 1, 'invalid player position blocked the pet action')
local st = readStatus()
assert(st.state == 'running', 'round did not stay in running state after fallback lookup')
print('PASS fallback: local role found by roleId when m_PlayerPos is invalid')
