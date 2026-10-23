local state=assert(rawget(_G,'__codex_battle_loop_v1'),'hide-only failed without a send function')
assert(state.hideBattleRequested and not state.skipBattleRequested,'fallback changed hide-only mode')
assert(state.sendHook==nil and state.sendOriginal==nil,'fallback installed a send wrapper without an original')
assert(ProtoFunc[3584]~=originalHandlers[3584],'visual observer was not installed')
assert(ProtoFunc[65505]~=originalMapHandler,'map handler observation was not installed')
local text=fakeFiles['visual.log'] or ''
assert(text:find('event=observation_unavailable',1,true) and text:find('reason=send_unavailable',1,true),
  'missing send capability was not recorded')
local returned=testPack(ProtoFunc[65505]({mapId=991},nil))
assert(returned.n==4 and returned[2]=='map-return','map observation stopped with send logging unavailable')
local before=ProtoFunc[3584]
fakeFiles.lease='0'
advance(0.01)
assert(rawget(_G,'__codex_battle_loop_v1')==nil,'missing-send observer did not clean up after lease expiry')
assert(ProtoFunc[3584]==originalHandlers[3584] and ProtoFunc[65505]==originalMapHandler,
  'missing-send cleanup left protocol hooks installed')
assert(readStatus().state=='stopped' and readStatus().hooksRemoved=='true' and readStatus().bindingCleared=='true',
  'missing-send cleanup did not report the actual restored state')
assert(before~=nil,'visual wrapper reference was not captured')
print('PASS hide-only missing send: map and visual observers run and owned hooks clean up')
