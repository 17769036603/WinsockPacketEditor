mockFiles.lease = nil
local result = manager:startReadNextCMD('arg')
assert(result == 'original-read' and stored.readCalls == 1, 'expired lease did not pass through original callback')
assert(rawget(_G, '__codex_apk_battle_skip_v1') == nil, 'missing lease left state or Hook installed')
assert(stored.result and stored.result:find('reason=activation_lease_expired', 1, true), 'missing lease was not reported')
print('PASS battle skip lease missing: cleanup and original callback forwarding')
