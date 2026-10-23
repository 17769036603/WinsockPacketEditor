local state = assert(rawget(_G, '__codex_battle_loop_v1'), 'battle-loop state missing')
advance(0.01)
assert(state.visualState == 'waiting_next_battle', 'starting midbattle did not wait for next battle')
local current = testPack(ProtoFunc[3584]({token='current'}, nil, 1, false))
assert(current.n == 5 and original3584Calls == 1, 'current battle start callback forwarding failed')
assert(battleManager.visible == true and state.hiddenBattles == 0,
    'midbattle start hid the already-running battle')

local ended = testPack(ProtoFunc[3585]({round=4}))
assert(ended[1] == 'original_end' and JudgeIsInWar() == false, 'current battle did not end normally')
advance(0.01)
assert(state.visualState == 'waiting_battle' and rawget(_G, '__codex_battle_loop_v1') == state,
    'module did not arm after observing battle end')

battleManager.m_BattleId = 78
local nextBattle = testPack(ProtoFunc[3584]({token='next'}, nil, 2, false))
assert(nextBattle.n == 5 and original3584Calls == 2, 'next battle original handler forwarding failed')
assert(state.hiddenBattles == 1 and state.visualState == 'hidden' and battleManager.visible == false,
    'next battle after observed end was not hidden')
print('PASS midbattle start: current battle untouched; next battle hidden')
