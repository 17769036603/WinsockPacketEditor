now = 100
fakeFiles = { lease = '100', worker = '100' }
fakeTimer = nil
fakeTimerCancelled = false
sendCalls = {}
originalCalls = {}
originalHandlers = {}
originalSend = nil
activePlayer = { getPlayerId = function() return 101 end }
local function makeRole(id, localRole)
  return {
    m_RoleId = id,
    m_IsLocalPlayer = localRole == true,
    alive = true,
    getIsAlive = function(self) return self.alive end
  }
end
battleManager = {
  m_PlayerId = 101,
  m_BattleId = 77,
  m_CurRoundNum = 4,
  m_CmdRound = 0,
  m_PlayerPos = 1,
  m_PetPos = 2,
  m_IsClose = false,
  m_RoleIdPosDict = { [101] = {1}, [202] = {2} },
  m_RoleIns = {},
  m_BattleUI = {
    m_AutoStatus = true,
    m_AutoSkill = {
      [101] = {actType = 0, skillId = 111},
      [202] = {actType = 0, skillId = 222}
    }
  }
}
battleManager.m_RoleIns[1] = makeRole(101, true)
battleManager.m_RoleIns[2] = makeRole(202, false)
battleManager.m_RoleIns[10001] = makeRole(501, false)
for _, role in pairs(battleManager.m_RoleIns) do role.m_Battlefield = battleManager end
LocalPlayerMgr = { getInstance = function() return activePlayer end }
ClassIns = { ['BattlefieldView.Ins'] = battleManager }
JudgeIsInWar = function() return true end
C2S_BattleOperate = 3470
C2S_BattleCmdPlayOver = 3472
C2S_BattleChangeAuto = 3473
CMD_TYPE_USESKILL = 7
ProtoFunc = {}
for _, id in ipairs({3584, 3585, 3586, 3587, 3581}) do
  ProtoFunc[id] = function(...) originalCalls[id] = (originalCalls[id] or 0) + 1 end
  originalHandlers[id] = ProtoFunc[id]
end
netconn = {}
function netconn:send(op, ...)
  local args = {...}
  sendCalls[#sendCalls + 1] = {op = op, args = args}
  if op == 3473 then battleManager.m_BattleUI.m_AutoStatus = args[1] == 1 end
end
originalSend = netconn.send
scheduler = {
  scheduleGlobal = function(fn, interval) fakeTimer = fn; return fn end,
  unscheduleGlobal = function(fn) if fn == fakeTimer then fakeTimerCancelled = true end end
}
os.time = function() return now end
io.open = function(path, mode)
  -- Production derives a sibling pause file from the result directory. Keep
  -- the sandbox's observable fake-file keys while honoring that path shape.
  path = path:gsub('^sandbox/', '')
  if mode == 'w' then
    local chunks = {}
    return {
      write = function(_, ...)
        for i = 1, select('#', ...) do chunks[#chunks + 1] = tostring(select(i, ...)) end
        return true
      end,
      close = function() fakeFiles[path] = table.concat(chunks); return true end
    }
  end
  local data = fakeFiles[path]
  if data == nil then return nil end
  return {
    read = function(_, fmt) return data end,
    close = function() return true end
  }
end
function advance(dt, count)
  for _ = 1, count or 1 do assert(fakeTimer, 'scheduler callback was not installed'); fakeTimer(dt) end
end
function countCalls(op, roleId, round, skillId)
  local n = 0
  for _, c in ipairs(sendCalls) do
    if c.op == op and (roleId == nil or tostring(c.args[3]) == tostring(roleId))
       and (round == nil or tostring(c.args[1]) == tostring(round))
       and (skillId == nil or tostring(c.args[6]) == tostring(skillId)) then n = n + 1 end
  end
  return n
end
function lastCall(op)
  for i = #sendCalls, 1, -1 do if sendCalls[i].op == op then return sendCalls[i] end end
end
function readStatus()
  local out = {}
  for line in string.gmatch(fakeFiles.result or '', '[^\n]+') do
    local k, v = line:match('^([^=]+)=(.*)$')
    if k then out[k] = v end
  end
  return out
end
function actorEvent(roleId, round, kind)
  ProtoFunc[3587]({roleId = roleId, round = round, type = kind})
end
function commandsSeen(round)
  ProtoFunc[3585]({round = round})
end
function offAck(round)
  actorEvent(101, round, 6)
end

-- Animation-compatible fields for the independent regression suite.
table.nums = function(t) local n=0; for _ in pairs(t) do n=n+1 end; return n end
battleManager.m_RoundCmdList = {}
battleManager.m_CurCmdPlayIndex = 1
battleManager.m_CurCmdIndex = 1
battleManager.m_PauseAnalyze = false
battleManager.m_CurCmdPlayIndex = 1
battleManager.m_PauseAnalyze = false
battleManager.m_AnalyzeFinish = true
fastCalls = 0
verifyCalls = 0
battleManager.fastAnalyzeSurplusCMD = function(self)
  fastCalls = fastCalls + 1
  self.m_AnalyzeFinish = true
end
battleManager.verifyRoundData = function(self, fromLoop)
  verifyCalls = verifyCalls + 1
  lastVerifyFromLoop = fromLoop
  return true
end
naturalPlayoverOnCommands = false
naturalPlayoverOnFastAnalyze = false
naturalPlayoverCount = 0
local inheritedCommandsSeen = commandsSeen
commandsSeen = function(round)
  battleManager.m_CmdRound = round
  battleManager.m_RoundCmdList = {{round=round, cmd='actor'}}
  battleManager.m_CurCmdPlayIndex = 1
  battleManager.m_PauseAnalyze = false
  battleManager.m_AnalyzeFinish = true
  ProtoFunc[3585]({round = round})
end
originalHandlers[3585] = function(d, ...)
  originalCalls[3585] = (originalCalls[3585] or 0) + 1
  if naturalPlayoverOnCommands then
    naturalPlayoverCount = naturalPlayoverCount + 1
    netconn:send(3472, '', 101, d.round)
  end
end
ProtoFunc[3585] = originalHandlers[3585]
local inheritedFastAnalyze = battleManager.fastAnalyzeSurplusCMD
battleManager.fastAnalyzeSurplusCMD = function(self, ...)
  inheritedFastAnalyze(self, ...)
  if naturalPlayoverOnFastAnalyze then
    naturalPlayoverCount = naturalPlayoverCount + 1
    netconn:send(3472, '', 101, self.m_CurRoundNum)
  end
end
function setAnimationState(round, opts)
  opts = opts or {}
  battleManager.m_CmdRound = opts.cmdRound or 0
  battleManager.m_RoundCmdList = opts.commands or {{round=round, cmd='actor'}}
  battleManager.m_CurCmdPlayIndex = opts.playIdx == nil and 1 or opts.playIdx
  battleManager.m_PauseAnalyze = opts.paused == true
  battleManager.m_AnalyzeFinish = opts.finished == true
end
function completeActorActions(round, includePet)
  actorEvent(101, round, 1)
  if includePet ~= false then actorEvent(202, round, 1) end
end
function prepareRound(round, opts)
  opts = opts or {}
  battleManager.m_CurRoundNum = round
  battleManager.m_CmdRound = opts.cmdRound or 0
  battleManager.m_AnalyzeFinish = opts.finished == true
  battleManager.m_RoundCmdList = opts.commands or {{round=round, cmd='actor'}}
  battleManager.m_CurCmdPlayIndex = opts.playIdx == nil and 1 or opts.playIdx
  battleManager.m_PauseAnalyze = opts.paused == true
  battleManager.m_BattleId = opts.battleId or battleManager.m_BattleId
  battleManager.m_PetPos = opts.noPet and 0 or 2
end
function beginAndActRound(round, includePet)
  advance(0.01)
  offAck(round)
  advance(0.01)
  completeActorActions(round, includePet)
  battleManager.m_CmdRound = round
  ProtoFunc[3585]({round=round})
  advance(0.01, 2)
end


