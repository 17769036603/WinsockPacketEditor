local state=assert(rawget(_G,'__codex_battle_loop_v1'),'send setter error aborted hide-only startup')
assert(state.sendHook==nil and state.sendOriginal==nil and state.sendConnection==nil,
  'send setter failure left an uncertain wrapper reference when restoration was confirmed')
assert(netconn.send==originalSend,'send setter failure did not leave the original connection method')
assert(ProtoFunc[3584]~=originalHandlers[3584] and ProtoFunc[65505]~=originalMapHandler,
  'send setter failure stopped the other hide-only observers')
local log=fakeFiles['visual.log'] or ''
assert(log:find('event=observation_unavailable',1,true) and log:find('reason=send_unavailable',1,true),
  'send setter failure was not logged as unavailable')
fakeFiles.lease='0'
advance(0.01)
assert(rawget(_G,'__codex_battle_loop_v1')==nil,'send setter failure cleanup did not finish')
assert(ProtoFunc[3584]==originalHandlers[3584] and ProtoFunc[65505]==originalMapHandler,
  'send setter failure cleanup left protocol hooks installed')
assert(readStatus().state=='stopped' and readStatus().hooksRemoved=='true'
  and readStatus().bindingCleared=='true','send setter failure cleanup receipt was incorrect')
print('PASS hide-only send setter failure: original restored and observers clean up')
