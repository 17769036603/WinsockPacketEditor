advance(0.01)
local before = #sendCalls
activePlayer = {getPlayerId = function() return 101 end}
advance(0.01)
assert(#sendCalls == before, 'role identity change emitted another network operation')
local st = readStatus()
assert(st.state == 'stopping' and st.reason == 'role_changed', 'identity change did not stop safely')
print('PASS identity: switched local-player object sends nothing')
