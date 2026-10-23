advance(0.01)
offAck(4)
advance(0.01)
assert(countCalls(3472) == 0, 'pending stop setup already emitted playover')
assert(battleManager.m_BattleUI.m_AutoStatus == false, 'stop setup did not reach auto-off state')

