local state=assert(rawget(_G,'__codex_battle_loop_v1'),'throwing send getter aborted hide-only')
assert(state.sendHook==nil and state.sendOriginal==nil,'throwing getter installed a send wrapper')
assert(ProtoFunc[3584]~=originalHandlers[3584] and ProtoFunc[65505]~=originalMapHandler,
  'throwing getter prevented other diagnostic observers')
local text=fakeFiles['visual.log'] or ''
assert(text:find('event=observation_unavailable',1,true) and text:find('reason=send_unavailable',1,true),
  'throwing getter was not isolated as unavailable')
fakeFiles.lease='0'
advance(0.01)
assert(rawget(_G,'__codex_battle_loop_v1')==nil,'throwing getter prevented cleanup')
assert(ProtoFunc[3584]==originalHandlers[3584] and ProtoFunc[65505]==originalMapHandler,
  'cleanup did not restore observers after getter failure')
assert(readStatus().state=='stopped' and readStatus().hooksRemoved=='true' and readStatus().bindingCleared=='true',
  'cleanup receipt did not match restored hook references')
print('PASS hide-only send getter error: failure isolated and owned hooks clean up')
