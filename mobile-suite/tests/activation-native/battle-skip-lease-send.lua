mockFiles.lease = 'deadlineElapsedMs=999999\nbootId=boot-a\n'
local result = netconn:send(123, 'arg')
assert(result == 'original-send' and stored.sendCalls == 1, 'expired lease did not pass through original send callback')
assert(netconn.send ~= nil and rawget(_G, '__codex_apk_battle_skip_v1') == nil, 'expired lease left state or Hook installed')
assert(stored.result and stored.result:find('reason=activation_lease_expired', 1, true), 'send callback expiry was not reported')
print('PASS battle skip send callback expiry: cleanup and original callback forwarding')
