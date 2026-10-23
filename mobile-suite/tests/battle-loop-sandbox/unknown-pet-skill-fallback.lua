data_SeniorPetSkill = {[222] = {targetType = {0}}}
battleManager.m_BattleUI.m_AutoSkill[202] = {actType = 7, skillId = 222}

local startRound = 4
local roundCount = 80
for offset = 0, roundCount - 1 do
  local round = startRound + offset
  battleManager.m_CurRoundNum = round
  battleManager.m_CmdRound = round - 1
  battleManager.m_AnalyzeFinish = true

  advance(0.01)
  offAck(round)
  advance(0.01)

  assert(countCalls(3470, 101, round, 111) == 1,
    'character command missing after pet fallback in round ' .. round)
  assert(countCalls(3470, 202, round, 0) == 1,
    'pet did not fall back to normal attack in round ' .. round)
  assert(countCalls(3470, 202, round, 222) == 0,
    'unknown pet skill was sent in round ' .. round)

  local petCommand = lastCall(3470)
  assert(petCommand.args[3] == 202 and petCommand.args[5] == 0 and petCommand.args[6] == 0,
    'fallback command did not encode pet normal attack in round ' .. round)

  commandsSeen(round)
  actorEvent(101, round, 1)
  actorEvent(202, round, 1)
  advance(0.01, 3)

  assert(countPlayovers(round) == 1,
    'one confirmed actor pair should produce one playover in round ' .. round)
  assert(battleManager.m_BattleUI.m_AutoStatus == true,
    'automatic mode was not restored after round ' .. round)

  advance(0.25, 3)
  assert(countCalls(3470, 202, round, 0) == 1,
    'completed round resent pet normal attack in round ' .. round)
  assert(countCalls(3470, 202, round, 222) == 0,
    'unknown pet skill was retried in round ' .. round)
  assert(countPlayovers(round) == 1,
    'completed round emitted a duplicate playover in round ' .. round)
end

assert(countCalls(3470) == roundCount * 2,
  'only one character command and one pet fallback command are expected per round')
print('PASS unknown pet skill fallback: 80 simulated rounds, normal attack only, actor confirmations, one playover each')
