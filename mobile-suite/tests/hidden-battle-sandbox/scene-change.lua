local state = assert(rawget(_G, '__codex_battle_loop_v1'), 'state missing')
ProtoFunc[3584]({token='start'})
assert(battleManager.visible == false, 'scene-change setup did not hide original root')
local replacement = {m_PlayerId=101,m_BattleId=77,m_CurRoundNum=4,visible=true}
function replacement:isVisible() return self.visible end
function replacement:setVisible(value) self.visible=value end
ClassIns['BattlefieldView.Ins'] = replacement
advance(0.11)
assert(state.visualState == 'restore_unconfirmed_scene_changed', 'scene identity change was not reported')
assert(state.visibilityRestoreConfirmed == false, 'scene identity change claimed restore success')
assert(replacement.visible == true, 'cleanup touched replacement scene root')
assert(battleManager.visible == false, 'cleanup unexpectedly restored stale root through replacement')
assert(#sendCalls == 0, 'scene change generated sends')
print('PASS scene change: no mutation of replacement UI; restore remains unconfirmed')
