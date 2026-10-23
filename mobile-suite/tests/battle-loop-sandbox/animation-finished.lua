startAnimationRound(4, true)
finishAnimationRound(4, true, {playIndex=3, cmdIndex=3, finished=true})
assert(fastCalls == 0, 'already-finished animation must not be fast-completed again')
assert(verifyCalls == 0, 'already-finished animation must not invoke fast-path verification')
assert(countPlayovers(4) == 1, 'already-finished round must emit one playover')
print('PASS animation finished: no fast call or fast-path verify, one playover')

