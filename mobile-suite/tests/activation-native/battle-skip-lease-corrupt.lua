mockFiles.lease = 'deadlineElapsedMs=not-a-number\nbootId=boot-a\n'
local result = manager:startReadNextCMD('arg')
assert(result == 'original-read' and stored.readCalls == 1, 'corrupt lease did not pass through original callback')
assert(rawget(_G, '__codex_apk_battle_skip_v1') == nil, 'corrupt lease left state or Hook installed')
assert(stored.result and stored.result:find('reason=activation_lease_expired', 1, true), 'corrupt lease was not reported')
print('PASS battle skip lease corrupt: cleanup and original callback forwarding')
