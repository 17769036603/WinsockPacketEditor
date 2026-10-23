local state = assert(rawget(_G, '__codex_battle_loop_v1'), 'battle-loop state missing')
local starts = 450
for index = 1, starts do
    battleManager.m_BattleId = 1000 + index
    ProtoFunc[3584]({token='start'})
    ProtoFunc[3585]({round=4})
    advance(0.01)
end
local current = fakeFiles['visual.log'] or ''
local previous = fakeFiles['visual.log.1'] or ''
assert(#current <= 65536, 'active visual log exceeded 64 KiB')
assert(#previous <= 65536, 'rotated visual log exceeded 64 KiB')
assert(#previous > 0 or #current < 65536, 'visual log did not rotate when capacity was exceeded')
assert(state.hiddenBattles == starts, 'visual event log pressure changed battle tracking')
assert(#sendCalls == 0, 'visual log stress generated sends')
print('PASS visual log: bounded files and no battle/send side effects')
