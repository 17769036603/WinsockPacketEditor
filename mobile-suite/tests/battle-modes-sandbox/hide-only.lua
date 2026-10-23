local s=rawget(_G,'__codex_battle_loop_v1')
assert(s and s.skipBattleRequested==false and s.hideBattleRequested==true,'hide-only mode was not retained')
assert(readStatus().skipBattleRequested=='false' and readStatus().hideBattleRequested=='true','hide-only status mismatch')
assert(type(fakeTimer)=='function','hide-only did not install its visual observer')
assert(ProtoFunc[3584]~=originalHandlers[3584],'hide-only did not wrap battle start')
for _,id in ipairs({3585,3586,3587,3581}) do
 assert(ProtoFunc[id]==originalHandlers[id],'hide-only changed unrelated ProtoFunc['..id..']')
end
assert(netconn.send==s.sendHook and s.sendOriginal==expectedOriginalSend,
 'hide-only did not install its transparent diagnostic send wrapper')

local values=testPack(netconn:send(3470,'round',0,101,909,7,123))
assert(values.n==9 and values[1]==nil and values[2]=='send-result' and values[3]==3470
 and values[4]=='round' and values[5]==0 and values[6]==101 and values[7]==909 and values[8]==7 and values[9]==123,
 '3470 arguments or return tuple changed')
local playover=testPack(netconn:send(3472,'',101,4))
local repeatedPlayover=testPack(netconn:send(3472,'',101,4))
local autoChange=testPack(netconn:send(3473,1,'extra'))
assert(playover.n==6 and playover[1]==nil and playover[2]=='send-result' and playover[3]==3472
 and playover[4]=='' and playover[5]==101 and playover[6]==4,'3472 return tuple or arguments changed')
assert(repeatedPlayover.n==playover.n and repeatedPlayover[3]==3472,'repeated 3472 was not passed through')
assert(autoChange.n==5 and autoChange[1]==nil and autoChange[2]=='send-result' and autoChange[3]==3473
 and autoChange[4]==1 and autoChange[5]=='extra','3473 return tuple or arguments changed')
assert(countCalls(3470)==1 and countCalls(3472)==2 and countCalls(3473)==1,'hide-only suppressed or deduplicated a client send')
assert(sendCalls[1].args[1]=='round' and sendCalls[1].args[3]==101 and sendCalls[1].args[4]==909
 and sendCalls[2].args[1]=='' and sendCalls[2].args[2]==101 and sendCalls[3].args[2]==101
 and sendCalls[4].args[1]==1 and sendCalls[4].args[2]=='extra','hide-only changed send arguments')

local result=testPack(ProtoFunc[3584]({token='battle-start'},nil,9,false))
assert(original3584Calls==1 and original3584Args.n==4 and original3584Args[2]==nil
 and original3584Args[3]==9 and original3584Args[4]==false,'3584 original lifecycle handler/arguments changed')
assert(result.n==5 and result[1]==nil and result[2]=='original_start' and result[3]==false
 and result[4]==nil and result[5]=='tail','3584 return tuple changed')
assert(JudgeIsInWar()==true and battleManager.visible==false,'ordinary battle-start lifecycle was not handled while visuals were hidden')
ProtoFunc[3585]({round=4})
assert(original3585Calls==1 and JudgeIsInWar()==false,'ordinary battle-end lifecycle was not handled')
assert(countCalls(3470)==1 and countCalls(3472)==2 and countCalls(3473)==1,'handler lifecycle added automatic sends')

fakeFiles['codex-battle-loop-pause']='1'
advance(0.01)
fakeFiles['codex-battle-loop-pause']=nil
fakeFiles.lease='0'
advance(0.01)
assert(#sendCalls==4,'pause/lease cleanup caused extra sends')
assert(battleManager.m_BattleUI.m_AutoStatus==true,'hide-only changed auto-battle state')
assert(fakeTimerCancelled==true,'lease expiry did not clean up its owned observer')
assert(netconn.send==expectedOriginalSend,'lease cleanup did not restore the original send reference')
print('PASS hide-only: only 3584 wrapped; original lifecycle and client sends pass through')
