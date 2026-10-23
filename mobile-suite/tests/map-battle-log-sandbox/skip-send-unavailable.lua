assert(rawget(_G,'__codex_battle_loop_v1')==nil,'skip-only remained active after required send capability was missing')
for _,id in ipairs({3584,3585,3586,3587,3581}) do
  assert(ProtoFunc[id]==originalHandlers[id],'skip-only failure left ProtoFunc['..id..'] hooked')
end
assert(netconn.send==nil,'skip-only failure fabricated a send function')
assert(fakeTimerCancelled~=true,'skip-only failure left a scheduled observer')
assert(readStatus().state=='error' and readStatus().reason:find('send_unavailable',1,true),
  'skip-only did not preserve its hard startup failure')
assert(#sendCalls==0,'skip-only missing-send failure emitted traffic')
print('PASS skip-only missing send: hard failure leaves no owned hooks or timer')
