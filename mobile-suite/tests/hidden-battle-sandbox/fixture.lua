-- Load tests/battle-loop-sandbox/fixture.lua first, then this extension, then the template.
local function makeMenuLayout(visible, x, y)
    local layout = {visible = visible, x = x, y = y}
    function layout:isVisible() return self.visible end
    function layout:setVisible(value) self.visible = value end
    function layout:getPosition() return self.x, self.y end
    function layout:setPosition(xValue, yValue) self.x, self.y = xValue, yValue end
    return layout
end

battleManager.visible = false
function battleManager:isVisible() return self.visible end
failRootSetVisible = false
function battleManager:setVisible(value)
    if value and failRootSetVisible then error('mock_root_restore_failed') end
    self.visible = value
end
g_CMainMenuHandler = {
    m_MapLayout = makeMenuLayout(true, 10, 11),
    m_LeftLayout = makeMenuLayout(false, 12, 13),
    m_RightLayout = makeMenuLayout(false, 14, 15),
    m_HeadLayout = makeMenuLayout(true, 16, 17),
    m_ChatLayout = makeMenuLayout(false, 18, 19),
    m_TeamLayout = makeMenuLayout(true, 20, 21),
    m_TempLayout = makeMenuLayout(false, 22, 23),
    m_TaskLayout = makeMenuLayout(true, 24, 25),
}
visualIoFailure = false
visualLogs = {}
local previousIoOpen = io.open
local previousOsRemove, previousOsRename = os.remove, os.rename
os.remove = function(path)
    if path == 'visual.log' or path == 'visual.log.1' then fakeFiles[path] = nil; return true end
    return previousOsRemove(path)
end
os.rename = function(oldPath, newPath)
    if (oldPath == 'visual.log' or oldPath == 'visual.log.1')
        and (newPath == 'visual.log' or newPath == 'visual.log.1') then
        fakeFiles[newPath] = fakeFiles[oldPath]
        fakeFiles[oldPath] = nil
        return true
    end
    return previousOsRename(oldPath, newPath)
end
io.open = function(path, mode)
    if path == 'visual.log' and mode == 'a' then
        if visualIoFailure then return nil end
        return {
            seek = function(_, whence)
                if whence ~= 'end' then error('unsupported mock seek') end
                return #(fakeFiles[path] or '')
            end,
            write = function(_, ...)
                local pieces = {}
                for i = 1, select('#', ...) do pieces[#pieces + 1] = tostring(select(i, ...)) end
                local text = table.concat(pieces)
                visualLogs[#visualLogs + 1] = text
                fakeFiles[path] = (fakeFiles[path] or '') .. text
                return true
            end,
            flush = function() return true end,
            close = function() return true end,
        }
    end
    return previousIoOpen(path, mode)
end

local inWar = false
JudgeIsInWar = function() return inWar end
setInWar = function(value) inWar = value end

local function packValues(...)
    return {n = select('#', ...), ...}
end
original3584Args = nil
original3584Calls = 0
original3585Calls = 0
originalHandlers[3584] = function(data, ...)
    original3584Calls = original3584Calls + 1
    original3584Args = packValues(data, ...)
    setInWar(true)
    battleManager:setVisible(true)
    g_CMainMenuHandler.m_MapLayout:setVisible(false)
    g_CMainMenuHandler.m_MapLayout:setPosition(90, 91)
    g_CMainMenuHandler.m_LeftLayout:setVisible(true)
    g_CMainMenuHandler.m_LeftLayout:setPosition(92, 93)
    g_CMainMenuHandler.m_ChatLayout:setVisible(true)
    g_CMainMenuHandler.m_ChatLayout:setPosition(94, 95)
    return nil, 'original_start', false, nil, 'tail'
end
ProtoFunc[3584] = originalHandlers[3584]
originalHandlers[3585] = function(data, ...)
    original3585Calls = original3585Calls + 1
    setInWar(false)
    battleManager:setVisible(false)
    g_CMainMenuHandler.m_MapLayout:setVisible(true)
    g_CMainMenuHandler.m_MapLayout:setPosition(10, 11)
    g_CMainMenuHandler.m_LeftLayout:setVisible(false)
    g_CMainMenuHandler.m_LeftLayout:setPosition(12, 13)
    g_CMainMenuHandler.m_ChatLayout:setVisible(false)
    g_CMainMenuHandler.m_ChatLayout:setPosition(18, 19)
    return 'original_end', nil, true
end
ProtoFunc[3585] = originalHandlers[3585]

function testPack(...)
    return packValues(...)
end
