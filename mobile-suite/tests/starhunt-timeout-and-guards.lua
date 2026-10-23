assert(countSends() == 0 and readStatus().selectedLevel == '0', 'timeout scenario must start in a passive baseline scan')
setCommand(11, 'run', 21, 1018)
advance(0.5)
assert(countSends(22568) == 1, 'run must send one teleport before waiting for confirmation')
advance(0.5, 16)
local timeout = readStatus()
assert(timeout.status == 'unknown' and timeout.reason == 'teleport_confirmation_timeout',
  'missing position confirmation must become unknown')
assert(timeout.teleports == '1' and countSends(22568) == 1 and countSends(12344) == 0,
  'teleport timeout retried or proceeded to challenge')
advance(0.5, 4)
assert(countSends(22568) == 1 and countSends(12344) == 0,
  'unknown phase must not resend automatically')

setCommand(12, 'run', 21, 1018)
advance(0.5)
assert(countSends(22568) == 2, 'a newer explicit run command should start a new bounded attempt')
changeMap(2020)
advance(0.5)
local mapPause = readStatus()
assert(mapPause.status == 'paused' and mapPause.reason == 'map_changed', 'map change did not pause the run')
assert(countSends(22568) == 2 and mapPause.hooksRemoved == 'true', 'map change sent again or retained the observer')
inWar = true
advance(0.5)
assert(readStatus().status == 'paused' and readStatus().reason == 'map_changed' and countSends() == 2,
  'a paused run must preserve its pause state during battle')
inWar = false

setCommand(13, 'run', 21, 2020)
advance(0.5)
assert(countSends(22568) == 3, 'new explicit run on the current map did not begin')
activeRole = 102
mapView.m_LocalRole.m_PlayerId = 102
advance(0.5)
local rolePause = readStatus()
assert(rolePause.status == 'paused' and rolePause.reason == 'role_changed', 'role change did not pause the run')
assert(countSends(22568) == 3 and rolePause.hooksRemoved == 'true', 'role change sent again or retained the observer')

fakeFiles.lease = '0'
setCommand(14, 'run', 21, 2020)
advance(0.5)
local leasePause = readStatus()
assert(leasePause.status == 'stopping' and leasePause.reason == 'lease_expired', 'expired lease was not reported before send')
assert(leasePause.acceptedSequence == '14' and countSends() == 3, 'expired lease allowed a business request')
expectedFinalSequence = '14'
