local state=assert(rawget(_G,'__codex_battle_loop_v1'))
assert(type(ProtoFunc[65505])=='function','65505 observer wrapper missing')
local startupText=fakeFiles['visual.log'] or ''
assert(not startupText:find('event=map_receive',1,true),'initialization fabricated a map receive')
local mapped=testPack(ProtoFunc[65505]({mapId=4321},nil,'tail'))
assert(mapHandlerCalls==1,'original 65505 handler was not called once')
assert(originalMapArgs.n==3 and originalMapArgs[1].mapId==4321 and originalMapArgs[2]==nil and originalMapArgs[3]=='tail',
  '65505 input tuple changed')
assert(mapped.n==4 and mapped[1]==nil and mapped[2]=='map-return' and mapped[3]==false and mapped[4]==nil,
  '65505 return tuple changed')
local mapText=fakeFiles['visual.log'] or ''
assert(mapText:find('event=map_receive',1,true),'map_receive missing')
assert(mapText:find('serverMapId=4321',1,true),'server map id was not sourced from handler data')
assert(mapText:find('event=map_handler_return',1,true),'post-handler observation missing')
assert(mapText:find('stage=receive evidence=server_message',1,true),'receive provenance missing')
assert(mapText:find('stage=after evidence=client_state',1,true),'handler-return provenance missing')
assert(mapText:find('runId=map-log-test',1,true) and mapText:find('seq=',1,true)
  and mapText:find('battleId=',1,true) and mapText:find('round=',1,true)
  and mapText:find('inWar=',1,true) and mapText:find('localMapId=',1,true), 'common context fields missing')
assert(not mapText:find('serverMapId=10',1,true),'local map id was mislabeled as server map id')

local before=#sendCalls
local sendResult=testPack(netconn:send(22568,7654,nil,'tail'))
assert(#sendCalls==before+1,'observer changed outgoing send count')
assert(sendResult.n==0,'outgoing nil return changed')
mapText=fakeFiles['visual.log'] or ''
assert(mapText:find('event=send_call',1,true) and mapText:find('protocol=22568',1,true)
  and mapText:find('targetMap=7654',1,true),'22568 call was not logged with numeric first argument')
assert(mapText:find('stage=send_call evidence=client_call',1,true),'send provenance missing')
assert(not mapText:find('event=map_receive',mapText:find('event=send_call',1,true) or 1,true),
  'send observation fabricated a server receive')

local badData=testPack(ProtoFunc[65505]({mapId='unknown'},nil))
assert(badData.n==4 and badData[2]=='map-return','non-numeric map handler result changed')
mapText=fakeFiles['visual.log'] or ''
assert(mapText:find('serverMapId=unknown',1,true),'non-numeric map id was not marked unknown')
assert(not mapText:find('targetMap=10',1,true),'local map was guessed as requested target')

MapMgr.m_CurMapId=11
advance(0.01)
mapText=fakeFiles['visual.log'] or ''
assert(mapText:find('event=map_snapshot',1,true) or mapText:find('event=map_local_change',1,true),
  'local state snapshot/change was not logged')
assert(#sendCalls==before+1,'visual and map observations added a network request')
for index=1,25 do netconn:send(22568,index) end
mapText=fakeFiles['visual.log'] or ''
local sendEvents=0
for _ in mapText:gmatch('event=send_call') do sendEvents=sendEvents+1 end
local limitedEvents=0
for _ in mapText:gmatch('event=send_sample_limited') do limitedEvents=limitedEvents+1 end
assert(sendEvents==20 and limitedEvents==1,'send sampling did not cap events at 20 plus one limited marker per second')
assert(#sendCalls==before+26,'send sampling suppressed an original client request')
print('PASS map log contract: receive/send provenance, tuples, numeric fields and local map observation')
