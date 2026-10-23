local state = assert(rawget(_G, '__codex_battle_loop_v1'), 'battle-loop state missing')
ProtoFunc[3584]({token='start'})
assert(battleManager.visible == false, 'restore-failure fixture was not hidden first')
failRootSetVisible = true
state.stopRequested = true
state.stopReason = 'user_stop'
advance(0.01)
assert(rawget(_G, '__codex_battle_loop_v1') == state, 'restore failure incorrectly removed live module state')
assert(state.visibilityRestoreConfirmed == false, 'restore failure was marked confirmed')
local status = readStatus()
assert(status.state == 'stopping' and status.visibilityRestoreConfirmed == 'false',
    'restore failure was reported as a clean terminal state')
assert(status.hooksRemoved ~= 'true' or status.bindingCleared ~= 'true',
    'restore failure claimed both hooks and binding were clean')
assert(battleManager.visible == false, 'failed restore unexpectedly changed root')
print('PASS restore failure: state remains stopping and visibility receipt is false')
