initialCommand='sequence=10\nmode=teleport\nlevels=\nmap=1007\n'
now = 1000
fakeFiles = {
  lease = '1000',
  worker = '1000',
  command = initialCommand or 'sequence=10\nmode=run\nlevels=21\nmap=1018\n'
}
failWrites = {}
sendCalls = {}
originalUseCalls = {}
originalUseError = nil
inWar = false
activeRole = 101
fakeTimer = nil
fakeTimerCancelled = false
fakeTimerInterval = nil

local function makeInfo(id, title, name, x, y, map)
  return {roleId = id, mapId = map or 1018, title = title, name = name, posX = x, posY = y}
end
local function makeNpc(id, title, name, x, y, fighting)
  local npc = {m_NpcInfo = makeInfo(id, title, name, x, y), fighting = fighting == true}
  npc.getIsFight = function(self) return self.fighting end
  return npc
end

npcBusy = makeNpc(10, '地煞21星', '地煞二十一星甲', 110, 120, true)
npcReady = makeNpc(20, '地煞21星', '地煞二十一星乙', 200, 300, false)
local npcThree = makeInfo(30, '地煞3星', '地煞三星目标', 310, 320)
local duplicateReady = makeInfo(20, '地煞21星', '重复实例', 999, 999)
local invalidTitle = makeInfo(40, '地煞21星额外', '标题不精确', 400, 410)
local otherMap = makeInfo(50, '地煞21星', '其他地图', 500, 510, 9999)

local localPlayer = {m_PlayerId = activeRole, m_IsLocalPlayer = true, m_Gx = 100, m_Gy = 100}
mapView = {
  m_MapId = 1018,
  m_LocalRole = localPlayer,
  m_MapInfo = {name = '测试地图'},
  m_NPC = {npcBusy, npcReady}
}
mapManager = {
  m_CurMapId = 1018,
  m_IsMapLoading = false,
  m_MapView = mapView,
  m_DynamicNPC = {{m_NpcInfo = npcThree}, {m_NpcInfo = npcBusy.m_NpcInfo}, {m_NpcInfo = invalidTitle}},
  m_CurMapFarNpc = {{m_NpcInfo = duplicateReady}, {m_NpcInfo = otherMap}}
}
LocalPlayerMgr = {getInstance = function()
  return {getPlayerId = function() return activeRole end}
end}
ClassIns = {['MapMgr.Ins'] = mapManager}
JudgeIsInWar = function() return inWar end
C2S_JumpMap = 22568
C2S_TouchNPCBtn = 12344
ProtoFunc = {
  [3584] = function() end,
  [3581] = function() end
}
protoTableBefore = ProtoFunc
proto3584Before = ProtoFunc[3584]
proto3581Before = ProtoFunc[3581]

local function originalUse(self, id, data, ...)
  originalUseCalls[#originalUseCalls + 1] = {id = id, data = data}
  if originalUseError then error(originalUseError) end
  return 'original-first', 'original-second'
end
netconn = {m_ConnectState = 1, useFunc = originalUse}
originalUseBefore = netconn.useFunc
function netconn:send(op, ...)
  sendCalls[#sendCalls + 1] = {op = op, args = {...}}
  return true
end
originalSendBefore = netconn.send

os.time = function() return now end
scheduler = {
  scheduleGlobal = function(fn, interval)
    fakeTimer = fn
    fakeTimerInterval = interval
    return fn
  end,
  unscheduleGlobal = function(fn)
    if fn == fakeTimer then fakeTimerCancelled = true end
  end
}

io.open = function(path, mode)
  if mode == 'w' then
    local chunks = {}
    return {
      write = function(_, ...)
        if failWrites[path] then error('simulated result write failure') end
        for i = 1, select('#', ...) do chunks[#chunks + 1] = tostring(select(i, ...)) end
        return true
      end,
      close = function()
        if not failWrites[path] then fakeFiles[path] = table.concat(chunks) end
        return true
      end
    }
  end
  local data = fakeFiles[path]
  if data == nil then return nil end
  return {
    read = function(_, fmt) return data end,
    close = function() return true end
  }
end

function setLevelsCommand(sequence, mode, levels, map)
  fakeFiles.command = string.format('sequence=%d\nmode=%s\nlevels=%s\nmap=%d\n', sequence, mode, levels, map)
end
function setCommand(sequence, mode, level, map)
  local levels = mode == 'run' and level > 0 and tostring(level) or ''
  setLevelsCommand(sequence, mode, levels, map)
end
function advance(dt, count)
  for _ = 1, count or 1 do
    assert(fakeTimer, 'starhunt scheduler callback was not installed')
    fakeTimer(dt or fakeTimerInterval)
  end
end
function advanceScheduled(count)
  assert(type(fakeTimerInterval) == 'number', 'starhunt scheduler interval was not captured')
  return advance(fakeTimerInterval, count)
end
function readStatus()
  local result = {}
  for line in string.gmatch(fakeFiles.result or '', '[^\n]+') do
    local key, value = line:match('^([^=]+)=(.*)$')
    if key then result[key] = value end
  end
  return result
end
function countSends(op)
  local count = 0
  for _, call in ipairs(sendCalls) do if op == nil or call.op == op then count = count + 1 end end
  return count
end
function lastSend(op)
  for i = #sendCalls, 1, -1 do if op == nil or sendCalls[i].op == op then return sendCalls[i] end end
end
function emit(id, data, ...)
  return netconn.useFunc(netconn, id, data, ...)
end
function changeMap(map)
  mapManager.m_CurMapId = map
  mapView.m_MapId = map
  for _, source in pairs({mapManager.m_DynamicNPC, mapManager.m_CurMapFarNpc, mapView.m_NPC}) do
    if type(source) == 'table' then
      for _, entry in pairs(source) do
        local info = entry.m_NpcInfo or entry
        if type(info) == 'table' then info.mapId = map end
      end
    end
  end
end

data_MapInfo={[1007]={name='TestMap',startPos={x=50,y=60}}}
mapManager.canNotOperationTeam=false
