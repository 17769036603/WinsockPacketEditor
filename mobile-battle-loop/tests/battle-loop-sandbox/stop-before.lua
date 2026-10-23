advance(0.01)
assert(countCalls(3473) == 1, 'expected the assistant to disable auto first')
battleManager.m_BattleUI.m_AutoStatus = true
