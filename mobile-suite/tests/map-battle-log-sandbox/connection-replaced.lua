local state=assert(rawget(_G,'__codex_battle_loop_v1'))
local externalSend=function() return 'external' end
netconn.send=externalSend
fakeFiles.lease='0'
advance(0.01)
assert(rawget(_G,'__codex_battle_loop_v1')==state,'connection replacement was falsely treated as cleaned')
assert(netconn.send==externalSend,'cleanup overwrote an external send replacement')
assert(fakeTimerCancelled~=true,'observer stopped despite incomplete hook cleanup')
local status=readStatus()
assert(status.state~='stopped' and status.status~='stopped','cleanup reported success with externally replaced connection')
assert(not (status.hooksRemoved=='true' and status.bindingCleared=='true'),'cleanup receipt claimed all hooks removed')
print('PASS connection replacement: cleanup remains unconfirmed and preserves external send')
