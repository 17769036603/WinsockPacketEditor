assert(readStatus().status == 'reading', 'starhunt must begin in passive scan mode')
assert(countSends() == 0 and netconn.useFunc == originalUseBefore,
  'passive scan must remain available while the Anlei state key is active')

__codex_anlei_v1 = {runId = 'anlei-fixture'}
setCommand(11, 'run', 21, 1018)
advance(0.5)
local conflict = readStatus()
assert(conflict.acceptedSequence == '11' and conflict.status == 'paused',
  'run command was not acknowledged as paused during an active Anlei session')
assert(conflict.reason == '暗雷模块正在运行，请手动停止后重新开始', 'Anlei conflict reason was not actionable')
assert(countSends() == 0 and netconn.useFunc == originalUseBefore,
  'StarHunt installed an observer or sent while Anlei was active')
assert(conflict.hooksRemoved == 'true' and conflict.bindingCleared == 'true',
  'conflict rejection must leave no StarHunt hook or role binding')

__codex_anlei_v1 = nil
advance(0.5, 3)
assert(readStatus().status == 'paused' and countSends() == 0,
  'clearing the conflict must not implicitly resume a previously rejected run')

setCommand(12, 'run', 21, 1018)
advance(0.5)
local resumed = readStatus()
assert(resumed.acceptedSequence == '12' and resumed.status == 'teleport_wait',
  'a new explicit run command did not resume after Anlei stopped')
assert(countSends(22568) == 1 and countSends(12344) == 0,
  'resumed run must emit one teleport and wait for confirmation')

setCommand(13, 'scan', 0, 0)
advance(0.5)
local scan = readStatus()
assert(scan.acceptedSequence == '13' and scan.status == 'reading', 'scan command did not stop resumed actions')
assert(scan.hooksRemoved == 'true' and scan.bindingCleared == 'true', 'scan command did not clear StarHunt state')
local sendsAtStop = countSends()
advance(0.5, 3)
assert(countSends() == sendsAtStop, 'scan command allowed a later business request')
expectedFinalSequence = '13'
