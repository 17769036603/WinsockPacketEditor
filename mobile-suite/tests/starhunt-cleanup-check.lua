local final = readStatus()
assert(final.status == 'stopped', 'stop control did not finish the owned Lua state')
assert(final.acceptedSequence == expectedFinalSequence, 'final status lost the last acknowledged command sequence')
assert(final.hooksRemoved == 'true' and final.bindingCleared == 'true', 'stop cleanup was not explicitly confirmed')
assert(fakeTimerCancelled, 'stop cleanup did not cancel the scheduler')
assert(fakeFiles.binding == '', 'stop cleanup left a role binding behind')
assert(netconn.useFunc == originalUseBefore, 'stop cleanup did not restore the original useFunc')
assert(netconn.send == originalSendBefore and ProtoFunc == protoTableBefore,
  'stop cleanup changed a non-owned business hook')
