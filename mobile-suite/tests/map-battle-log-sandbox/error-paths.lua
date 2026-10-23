local ok,err=pcall(ProtoFunc[65505],{mapId=321},nil,'tail')
assert(not ok and tostring(err):find('mock_map_handler_error',1,true),'65505 original handler error was not propagated')
assert(mapHandlerCalls==1,'failing 65505 original was not called once')
local sendOk,sendErr=pcall(netconn.send,netconn,22568,7654,nil,'tail')
assert(not sendOk and tostring(sendErr):find('mock_send_error',1,true),'original send error was not propagated')
local text=fakeFiles['visual.log'] or ''
assert(text:find('event=map_receive',1,true) and text:find('event=send_call',1,true),
  'diagnostic events were lost when originals raised')
print('PASS map log errors: original errors propagate through transparent wrappers')
