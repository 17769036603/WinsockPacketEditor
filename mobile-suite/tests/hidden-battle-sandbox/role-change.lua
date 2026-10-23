local state = assert(rawget(_G, '__codex_battle_loop_v1'), 'state missing')
ProtoFunc[3584]({token='start'})
assert(battleManager.visible == false, 'role-change setup did not hide original root')
assert(state.visualState == 'hidden' and state.visibilityRestoreConfirmed == false,
    'active hidden state was not established')

local replacementPlayer = {getPlayerId=function() return 202 end}
activePlayer = replacementPlayer
local replacementRoot = {m_PlayerId=202,m_BattleId=77,m_CurRoundNum=4,visible=true}
function replacementRoot:isVisible() return self.visible end
function replacementRoot:setVisible(value) self.visible=value end
ClassIns['BattlefieldView.Ins'] = replacementRoot
local replacementMap = makeMenuLayout(false, 900, 901)
g_CMainMenuHandler = {m_MapLayout=replacementMap}

advance(0.01)
assert(rawget(_G, '__codex_battle_loop_v1') == state, 'identity cleanup incorrectly removed live state')
assert(state.visualState == 'restore_unconfirmed_identity_changed', 'role identity change was not reported')
assert(state.visibilityRestoreConfirmed == false, 'role identity change claimed visual restoration')
local status = readStatus()
assert(status.state == 'stopping' and status.visibilityRestoreConfirmed == 'false',
    'role identity change was not reported as stopping/unconfirmed')
assert(replacementRoot.visible == true, 'cleanup changed replacement role root visibility')
assert(replacementMap.visible == false and replacementMap.x == 900 and replacementMap.y == 901,
    'cleanup changed replacement role menu layout')
assert(battleManager.visible == false, 'stale root was modified after role identity changed')
assert(#sendCalls == 0, 'role identity change generated sends')
print('PASS role change: new role UI untouched; cleanup stays stopping with false receipt')
