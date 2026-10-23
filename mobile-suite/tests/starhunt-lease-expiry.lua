assert(readStatus().status == 'reading', 'lease scenario must start in passive scan mode')
setCommand(11, 'run', 21, 1018)
advance(0.5)
assert(countSends(22568) == 1 and netconn.useFunc ~= originalUseBefore,
  'run setup did not install its single observer and teleport request')

fakeFiles.lease = '0'
fakeFiles.worker = '0'
advance(0.5, 4)
assert(not fakeTimerCancelled and netconn.useFunc ~= originalUseBefore,
  'worker lease ended before the bounded stale grace elapsed')

fakeFiles.lease = '1000'
fakeFiles.worker = '1000'
advance(0.5)
assert(not fakeTimerCancelled and readStatus().status == 'teleport_wait',
  'fresh lease and heartbeat did not reset the stale timer')

fakeFiles.lease = '0'
fakeFiles.worker = '0'
advance(0.5, 2)
assert(not fakeTimerCancelled and netconn.useFunc ~= originalUseBefore,
  'stale timer was not reset after lease recovery')
advance(0.5, 6)
local expired = readStatus()
assert(expired.status == 'stopped' and expired.reason == 'lease_expired',
  'both stale lease files did not finish the Lua session after the bounded grace')
assert(expired.hooksRemoved == 'true' and expired.bindingCleared == 'true',
  'lease expiry did not confirm observer and binding cleanup')
assert(fakeTimerCancelled and fakeFiles.binding == '', 'lease expiry left timer or role binding active')
assert(netconn.useFunc == originalUseBefore, 'lease expiry did not restore the original useFunc')
assert(countSends(22568) == 1 and countSends(12344) == 0,
  'lease expiry emitted an extra business request')
expectedFinalSequence = '11'
