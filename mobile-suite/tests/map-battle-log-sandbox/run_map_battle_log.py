"""Run map/battle observation logging scenarios through the production Lua template."""
import ctypes.util
import importlib.util
import os
import sys
from pathlib import Path

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
RUNNER_PATH = ROOT / "tests" / "hidden-battle-sandbox" / "run_hidden_battle.py"
spec = importlib.util.spec_from_file_location("hidden_runner", RUNNER_PATH)
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)

DLL = os.environ.get("LUA51_DLL") or ctypes.util.find_library("lua51") or ctypes.util.find_library("lua5.1")
if not DLL:
    raise SystemExit("Lua 5.1 not found; set LUA51_DLL to the existing runtime DLL")
runner.DLL = Path(DLL)
TEMPLATE = ROOT / "app" / "src" / "main" / "assets" / "battle_loop.lua.in"
BASE = ROOT / "tests" / "battle-loop-sandbox" / "fixture.lua"
HIDE = ROOT / "tests" / "hidden-battle-sandbox" / "fixture.lua"
HERE = Path(__file__).parent
CONFIG = "{resultPath='sandbox/result',bindingPath='binding',leasePath='lease',workerPath='worker',logPath='rounds.log',visualLogPath='visual.log',stateKey='__codex_battle_loop_v1',runId='map-log-test',expectedPlayer='101',expectedManagerTostring='manager',mode='tick',skipBattle=false,hideBattle=true}"

def main():
    template = TEMPLATE.read_text(encoding="utf-8")
    shared = BASE.read_text(encoding="utf-8") + "\n" + HIDE.read_text(encoding="utf-8")
    scenarios = sys.argv[1:] or [p.stem for p in HERE.glob("*.lua") if p.stem != "send-setter"] + ["send-setter-fail", "send-setter-after-write"]
    for scenario in scenarios:
        test_path = HERE / (scenario + ".lua")
        if not test_path.is_file() and scenario.startswith("send-setter-"):
            test_path = HERE / "send-setter.lua"
        test = test_path.read_text(encoding="utf-8")
        fixture = """MapMgr={m_CurMapId=10}
ClassIns['MapMgr.Ins']=MapMgr
mapHandlerCalls=0
originalMapHandler=function(data,...)
  mapHandlerCalls=mapHandlerCalls+1
  originalMapArgs={n=select('#',data,...),data,...}
  return nil,'map-return',false,nil
end
ProtoFunc[65505]=originalMapHandler
"""
        if scenario == "error-paths":
            fixture = """MapMgr={m_CurMapId=10}
ClassIns['MapMgr.Ins']=MapMgr
mapHandlerCalls=0
originalMapHandler=function(...) mapHandlerCalls=mapHandlerCalls+1;error('mock_map_handler_error') end
ProtoFunc[65505]=originalMapHandler
netconn.send=function(self,op,...) error('mock_send_error') end
"""
        if scenario == "missing-handler":
            fixture = "MapMgr={m_CurMapId=10}\nClassIns['MapMgr.Ins']=MapMgr\nProtoFunc[65505]=nil\n"
        config = CONFIG
        if scenario == "missing-send":
            fixture += "netconn.send=nil\n"
        elif scenario == "send-getter-error":
            fixture += "netconn=setmetatable({}, {__index=function(_,k) if k=='send' then error('mock_send_getter_error') end end})\n"
        elif scenario == "send-setter-fail":
            fixture += "netconn=setmetatable({}, {__index=function(_,k) if k=='send' then return originalSend end end,__newindex=function(_,k,v) if k=='send' then error('mock_send_setter_error') end end})\n"
        elif scenario == "send-setter-after-write":
            fixture += "netconn=setmetatable({}, {__index=function(_,k) if k=='send' then return originalSend end end,__newindex=function(t,k,v) if k=='send' then rawset(t,k,v);error('mock_send_setter_error_after_write') end end})\n"
        elif scenario == "skip-send-unavailable":
            fixture += "netconn.send=nil\n"
            config = CONFIG.replace("skipBattle=false,hideBattle=true", "skipBattle=true,hideBattle=false")
        program = shared + "\n" + fixture + "\nlocal __entry=function()\n" + template.replace("__CONFIG__", config) + "\nend\n__entry()\n" + test
        print(f"=== map-battle-log: {scenario} ===", flush=True)
        runner.run_lua(program, "map-battle-log-" + scenario)

if __name__ == "__main__":
    main()
