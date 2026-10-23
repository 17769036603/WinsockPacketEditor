visualIoFailure = true
local state = assert(rawget(_G, '__codex_battle_loop_v1'), 'battle-loop state missing')
local beforeSends = #sendCalls
local result = testPack(ProtoFunc[3584]({token='io-failure'}, nil, 4))
assert(original3584Calls == 1, 'log I/O failure skipped original handler')
assert(result.n == 5 and result[1] == nil and result[2] == 'original_start' and result[5] == 'tail',
    'log I/O failure changed original return tuple')
assert(battleManager.visible == false and state.visualState == 'hidden',
    'log I/O failure blocked visual behavior')
assert(#sendCalls == beforeSends, 'log I/O failure caused a send')
assert(JudgeIsInWar() == true, 'visual code changed battle state while logging failed')
print('PASS log I/O failure: handler, visual behavior, flags and send count preserved')
