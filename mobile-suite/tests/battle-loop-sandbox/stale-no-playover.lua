-- 完成标记先挂住（stale）的回合既可能是"末回合已播完"，也可能是"客户端在等玩家指令"。
-- 实测盲发 3472 会把服务端回合一路推快（67694：27 个回合 / 11 秒），所以这一支只做本机快进，
-- 不允许补发 playover。此用例守住"不因日志好看而制造脱同步"。
battleManager.m_RoleIns[10001].alive = false
setAnimationState(9, {cmdRound = 8, finished = true, playIdx = 1, commands = {{round = 9}, {round = 9}}})
advance(0.01)
advance(0.5)
assert(countCalls(3470) == 0, 'stale round must not send actions')
assert(countCalls(3472) == 0, 'stale round must not force a playover')
assert(verifyCalls == 0, 'stale branch must not claim a verified round')
print('PASS stale: client-side attempt only, no forced playover')
