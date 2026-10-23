data_SeniorPetSkill = {[222] = {targetType = {0}}}
battleManager.m_BattleUI.m_AutoSkill[202] = {actType = 7, skillId = 222}
advance(0.01)
assert(countCalls(3470) == 0 and countCalls(3473) == 0, 'unconfirmed skill target caused a send')
local st = readStatus()
assert(st.state == 'waiting_config', 'unknown pet skill target did not fail closed')
assert(st.reason == 'skill_target_type_unconfirmed', 'unknown target reason was not reported')
print('PASS safety: unknown skill target sends no operation')
