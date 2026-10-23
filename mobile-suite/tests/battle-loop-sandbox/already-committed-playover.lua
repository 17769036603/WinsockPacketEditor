-- 客户端先提交指令的回合：本机快进后补发的 3472 不能被自己的去重表挡掉。
-- 回归目标：旧实现先把 battleId:round 写进 playoverKeys 再 send，sendHook 看到键已存在
-- 直接 return，包根本没发出去，日志却写 skipped/playover=true。
battleManager.m_CurRoundNum = 9
battleManager.m_CmdRound = 9
battleManager.m_BattleId = 77
battleManager.m_RoundCmdList = {{round = 9, cmd = 'a'}, {round = 9, cmd = 'b'}, {round = 9, cmd = 'c'}}
battleManager.m_CurCmdPlayIndex = 1
battleManager.m_PauseAnalyze = false
battleManager.m_AnalyzeFinish = false
advance(0.01)
assert(countCalls(3470) == 0, 'committed round must not resend actions')
assert(countCalls(3472) == 1, 'fallback playover was swallowed by the dedupe key')
assert(tostring(lastCall(3472).args[3]) == '9', 'fallback playover round mismatch')
print('PASS committed: fallback playover actually leaves the client')
