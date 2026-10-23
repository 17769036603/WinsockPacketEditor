local stored = {}
local originalOpen = io.open
local mockFiles = {
    lease = 'deadlineElapsedMs=1080000\nbootId=boot-a\n',
    ['/proc/uptime'] = '1000.00 300.00\n',
    ['/proc/sys/kernel/random/boot_id'] = 'boot-a\n'
}
io.open = function(path, mode)
    if path == 'lease' or path == '/proc/uptime'
        or path == '/proc/sys/kernel/random/boot_id' then
        if not mockFiles[path] then return nil, 'missing' end
        local lines = {}
        for line in mockFiles[path]:gmatch('([^\r\n]+)') do lines[#lines + 1] = line end
        local index = 0
        return {
            read = function(_, format)
                if format == '*l' then index = index + 1; return lines[index] end
                return mockFiles[path]
            end,
            close = function() return true end
        }
    end
    if mode == 'w' then
        stored[path] = ''
        return {
            write = function(_, ...)
                for i = 1, select('#', ...) do stored[path] = stored[path] .. tostring(select(i, ...)) end
                return true
            end,
            close = function() return true end
        }
    end
    return originalOpen(path, mode)
end
os.remove = function(path) stored['removed:' .. path] = true; return true end

local role = {m_RoleId=7,m_IsLocalPlayer=true}
local manager = {
    m_PlayerId=7,m_RoleIdPosDict={[7]=1},m_RoleIns={[1]=role},m_BattleId=44,
    m_CmdRound=1,m_CurRoundNum=1,m_AnalyzeFinish=true,m_PauseAnalyze=false,
    m_RoundCmdList={{}},m_CurCmdPlayIndex=1,m_CurCmdIndex=1,
    fastAnalyzeSurplusCMD=function() error('must not fast analyze after expiry') end,
    verifyRoundData=function() error('must not verify after expiry') end,
    startReadNextCMD=function() stored.readCalls=(stored.readCalls or 0)+1; return 'original-read' end
}
role.m_Battlefield=manager
ClassIns={['BattlefieldView.Ins']=manager}
LocalPlayerMgr={getInstance=function() return {getPlayerId=function() return 7 end} end}
netconn={send=function() stored.sendCalls=(stored.sendCalls or 0)+1; return 'original-send' end}
C2S_BattleCmdPlayOver=999
table.nums=function(value) local count=0; for _ in pairs(value) do count=count+1 end; return count end
