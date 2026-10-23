"""Execute isolated battle-loop hidden-visual scenarios through Lua 5.1."""
import ctypes
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DLL = Path(r"C:\Program Files\Blackmagic Design\DaVinci Resolve\lua5.1.dll")
BASE_FIXTURE = ROOT / "tests" / "battle-loop-sandbox" / "fixture.lua"
FIXTURE = Path(__file__).with_name("fixture.lua")
TEMPLATE = ROOT / "app" / "src" / "main" / "assets" / "battle_loop.lua.in"
SCENARIO_DIR = Path(__file__).parent

CONFIG = "{resultPath='sandbox/result',bindingPath='binding',leasePath='lease',workerPath='worker',logPath='rounds.log',visualLogPath='visual.log',stateKey='__codex_battle_loop_v1',runId='hide-test',expectedPlayer='101',expectedManagerTostring='manager',mode='tick',hideBattle=%s}"

def run_lua(source: str, name: str) -> None:
    lua = ctypes.WinDLL(str(DLL))
    lua.luaL_newstate.restype = ctypes.c_void_p
    lua.luaL_openlibs.argtypes = [ctypes.c_void_p]
    lua.luaL_loadbuffer.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_size_t, ctypes.c_char_p]
    lua.luaL_loadbuffer.restype = ctypes.c_int
    lua.lua_pcall.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int, ctypes.c_int]
    lua.lua_pcall.restype = ctypes.c_int
    lua.lua_tolstring.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.POINTER(ctypes.c_size_t)]
    lua.lua_tolstring.restype = ctypes.c_char_p
    lua.lua_close.argtypes = [ctypes.c_void_p]
    state = lua.luaL_newstate()
    if not state:
        raise RuntimeError("luaL_newstate failed")
    try:
        lua.luaL_openlibs(state)
        payload = source.encode("utf-8")
        status = lua.luaL_loadbuffer(state, payload, len(payload), name.encode("ascii"))
        if status:
            raise RuntimeError("Lua load failed: " + _error(lua, state))
        status = lua.lua_pcall(state, 0, 0, 0)
        if status:
            raise RuntimeError("Lua scenario failed: " + _error(lua, state))
    finally:
        lua.lua_close(state)

def _error(lua, state):
    size = ctypes.c_size_t()
    ptr = lua.lua_tolstring(state, -1, ctypes.byref(size))
    return ptr[:size.value].decode("utf-8", "replace") if ptr else "unknown Lua error"

def main():
    if not DLL.is_file():
        raise SystemExit(f"Lua runtime not found: {DLL}")
    template = TEMPLATE.read_text(encoding="utf-8")
    shared = BASE_FIXTURE.read_text(encoding="utf-8") + "\n" + FIXTURE.read_text(encoding="utf-8")
    scenarios = sys.argv[1:] or ["off", "hidden", "midbattle", "pause", "lease", "restore-fail", "scene-change", "role-change", "handler-error", "io-fail", "log-limit"]
    for scenario in scenarios:
        mode = "false" if scenario == "off" else "true"
        code = template.replace("__CONFIG__", CONFIG % mode)
        test = (SCENARIO_DIR / (scenario + ".lua")).read_text(encoding="utf-8")
        prelude = ""
        if scenario == "handler-error":
            prelude = "originalHandlers[3584] = function(...) error('mock_original_handler_failure') end\nProtoFunc[3584] = originalHandlers[3584]"
        if scenario == "midbattle":
            prelude = """setInWar(true)
battleManager.visible = true
g_CMainMenuHandler.m_MapLayout:setVisible(false)
g_CMainMenuHandler.m_MapLayout:setPosition(90, 91)
g_CMainMenuHandler.m_LeftLayout:setVisible(true)
g_CMainMenuHandler.m_LeftLayout:setPosition(92, 93)
g_CMainMenuHandler.m_ChatLayout:setVisible(true)
g_CMainMenuHandler.m_ChatLayout:setPosition(94, 95)
"""
        print(f"=== hidden-battle: {scenario} ===", flush=True)
        program = (shared + "\n" + prelude + "\nlocal __hidden_battle_entry = function()\n"
                   + code + "\nend\n__hidden_battle_entry()\n" + test)
        run_lua(program, "hidden-battle-" + scenario)

if __name__ == "__main__":
    main()
