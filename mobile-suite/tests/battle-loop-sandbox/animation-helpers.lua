function startAnimationRound(round, includePet)
  battleManager.m_CurRoundNum = round
  battleManager.m_CmdRound = round - 1
  battleManager.m_PetPos = includePet == false and 0 or 2
  advance(0.01)
  offAck(round)
  advance(0.01)
  assert(countCalls(3470, 101, round) == 1, 'character action missing in round ' .. round)
  if includePet ~= false then assert(countCalls(3470, 202, round) == 1, 'pet action missing in round ' .. round) end
end
function publishAnimationCommands(round, opts)
  opts = opts or {}
  battleManager.m_CmdRound = opts.cmdRound == nil and round or opts.cmdRound
  battleManager.m_RoundCmdList = opts.commands == nil and {{round=round,cmd='actor1'},{round=round,cmd='actor2'},{round=round,cmd='actor3'},{round=round,cmd='actor4'}} or opts.commands
  battleManager.m_CurCmdPlayIndex = opts.playIndex == nil and 1 or opts.playIndex
  battleManager.m_CurCmdIndex = opts.cmdIndex == nil and battleManager.m_CurCmdPlayIndex or opts.cmdIndex
  battleManager.m_PauseAnalyze = opts.paused == true
  battleManager.m_AnalyzeFinish = opts.finished == true
  ProtoFunc[3585]({round=round})
end
function finishAnimationRound(round, includePet, opts)
  completeActorActions(round, includePet)
  publishAnimationCommands(round, opts)
  advance(0.01, opts and opts.ticks or 3)
end
function countPlayovers(round)
  local n = 0
  for _, c in ipairs(sendCalls) do
    if c.op == 3472 and (round == nil or tostring(c.args[3]) == tostring(round)) then n = n + 1 end
  end
  return n
end
