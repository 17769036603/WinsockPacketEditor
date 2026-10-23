battleManager.m_PetPos = nil
advance(0.01)
offAck(4)
advance(0.01)
assert(countCalls(3470, 101, 4, 111) == 1, 'character did not act without a pet')
assert(countCalls(3470, 202) == 0, 'missing pet produced a pet action')
print('PASS roster: character action runs without a pet')
