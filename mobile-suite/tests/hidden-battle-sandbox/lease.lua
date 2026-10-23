local state = assert(rawget(_G, '__codex_battle_loop_v1'), 'battle-loop state missing')
ProtoFunc[3584]({token='start'})
assert(battleManager.visible == false, 'lease fixture was not hidden first')
now = now + 6
fakeFiles.lease = tostring(now - 6)
fakeFiles.worker = tostring(now - 6)
advance(0.01)
assert(rawget(_G, '__codex_battle_loop_v1') == nil, 'expired lease did not finish cleanup after visual restore')
local status = readStatus()
assert(status.state == 'stopped' and status.hideBattleRequested == 'true', 'lease terminal status missing')
assert(status.visibilityRestoreConfirmed == 'true' and status.hooksRemoved == 'true' and status.bindingCleared == 'true',
    'lease stop receipt claimed clean without visibility restore evidence')
assert(battleManager.visible == true and g_CMainMenuHandler.m_MapLayout.visible == false,
    'lease expiry did not restore battle root/menu')
assert(fakeTimerCancelled, 'lease cleanup did not cancel scheduler')
print('PASS lease: active visual restored before clean stop receipt')
