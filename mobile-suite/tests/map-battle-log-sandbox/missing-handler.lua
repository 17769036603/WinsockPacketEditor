local text=fakeFiles['visual.log'] or ''
assert(text:find('event=observation_unavailable',1,true)
  and text:find('reason=handler_missing',1,true),'missing 65505 handler was not reported as unavailable')
assert(ProtoFunc[3584]~=nil,'missing observation handler prevented normal setup')
assert(#sendCalls==0,'missing handler generated a request')
print('PASS map log unavailable handler: configuration remains operational')
