"""Run split skip/hide Lua scenarios against the production template."""
import importlib.util
import sys
from pathlib import Path

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
HIDDEN_RUNNER = ROOT / "tests" / "hidden-battle-sandbox" / "run_hidden_battle.py"
BASE_FIXTURE = ROOT / "tests" / "battle-loop-sandbox" / "fixture.lua"
HIDE_FIXTURE = ROOT / "tests" / "hidden-battle-sandbox" / "fixture.lua"
TEMPLATE = ROOT / "app" / "src" / "main" / "assets" / "battle_loop.lua.in"
TESTS = Path(__file__).parent

spec = importlib.util.spec_from_file_location("hidden_battle_runner", HIDDEN_RUNNER)
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)

CONFIG = "{resultPath='sandbox/result',bindingPath='binding',leasePath='lease',workerPath='worker',logPath='rounds.log',visualLogPath='visual.log',stateKey='__codex_battle_loop_v1',runId='split-test',expectedPlayer='101',expectedManagerTostring='manager',mode='tick',skipBattle=%s,hideBattle=%s}"
MODES = {
    "skip-only": ("true", "false"),
    "hide-only": ("false", "true"),
    "both-on": ("true", "true"),
    "both-off": ("false", "false"),
    "both-off-fresh": ("false", "false"),
    "legacy": (None, "false"),
    "hide-stop": ("false", "true"),
    "hide-role": ("false", "true"),
    "hide-error": ("false", "true"),
}
PRELUDES = {
    "hide-only": """local inheritedSend=netconn.send
netconn.send=function(self,op,...)
  inheritedSend(self,op,...)
  return nil,'send-result',op,...
end
expectedOriginalSend=netconn.send
""",
    "both-off": """__codex_battle_loop_v1={runId='old-run',timer=function() end,stopRequested=false}
fakeFiles.result='state=running\\nstatus=running\\nrunId=old-run\\n'
""",
    "hide-error": """originalHandlers[3584]=function(...) error('mock_original_handler_failure') end
ProtoFunc[3584]=originalHandlers[3584]
""",
}


def main():
    scenarios = sys.argv[1:] or ["skip-only", "hide-only", "both-on", "both-off-fresh", "both-off", "legacy", "hide-stop", "hide-role", "hide-error"]
    template = TEMPLATE.read_text(encoding="utf-8")
    shared = BASE_FIXTURE.read_text(encoding="utf-8") + "\n" + HIDE_FIXTURE.read_text(encoding="utf-8")
    for scenario in scenarios:
        if scenario not in MODES:
            raise SystemExit(f"unknown scenario: {scenario}")
        skip, hide = MODES[scenario]
        config = CONFIG % (skip or "nil", hide)
        code = template.replace("__CONFIG__", config)
        extra = ""
        if scenario == "hide-stop":
            stop_config = config.replace("mode='tick'", "mode='stop'")
            extra = "\nlocal __battle_modes_stop = function()\n" + template.replace("__CONFIG__", stop_config) + "\nend\n__battle_modes_stop()\n"
        test = (TESTS / (scenario + ".lua")).read_text(encoding="utf-8")
        print(f"=== battle-modes: {scenario} ===", flush=True)
        program = shared + "\n" + PRELUDES.get(scenario, "") + "\nlocal __battle_modes_entry = function()\n" + code + "\nend\n__battle_modes_entry()\n" + extra + test
        runner.run_lua(program, "battle-modes-" + scenario)


if __name__ == "__main__":
    main()
