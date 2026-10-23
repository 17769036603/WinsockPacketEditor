assert(rawget(_G,'__codex_battle_loop_v1')==nil,'both-off created owned global state')
assert(fakeTimer==nil and #sendCalls==0,'both-off installed timer or sent traffic')
assert(ProtoFunc[3584]==originalHandlers[3584] and ProtoFunc[3585]==originalHandlers[3585]
 and ProtoFunc[3586]==originalHandlers[3586] and ProtoFunc[3587]==originalHandlers[3587]
 and ProtoFunc[3581]==originalHandlers[3581] and netconn.send==originalSend,'both-off installed hooks')
assert(rawget(_G,'__codex_battle_skip_v1')==nil,'both-off changed skip global state')
assert(readStatus().state=='stopped' and readStatus().reason=='no_mode_enabled','both-off did not report a no-op terminal state')
print('PASS both-off: no owner, timer, hooks, sends or skip state created')
