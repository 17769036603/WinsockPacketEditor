local previous=__codex_battle_loop_v1
assert(previous and previous.runId=='old-run' and previous.stopRequested==false,'both-off replaced or cleaned the pre-existing run')
assert(fakeTimer==nil and #sendCalls==0,'both-off installed timer or sent traffic')
assert(ProtoFunc[3584]==originalHandlers[3584] and ProtoFunc[3585]==originalHandlers[3585]
 and ProtoFunc[3586]==originalHandlers[3586] and ProtoFunc[3587]==originalHandlers[3587]
 and ProtoFunc[3581]==originalHandlers[3581] and netconn.send==originalSend,'both-off installed hooks')
assert(fakeFiles.result=='state=running\nstatus=running\nrunId=old-run\n','both-off overwrote existing status evidence')
assert(rawget(_G,'__codex_battle_skip_v1')==nil,'both-off changed skip global state')
print('PASS both-off: no new hooks, timer, sends or status overwrite; existing owner preserved')
