# Hidden battle Lua sandbox

These scenarios execute `battle_loop.lua.in` against mocked game globals with the machine's existing Lua 5.1 runtime. No package installation or game/device interaction is used. The runner defaults to every scenario, including log rotation; optional scenario names can be passed to run selected cases.

Run from the `mobile-suite` directory:

```powershell
& 'C:\Users\Administrator\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' tests/hidden-battle-sandbox/run_hidden_battle.py
```

The runner uses `C:\Program Files\Blackmagic Design\DaVinci Resolve\lua5.1.dll`. Edit `DLL` in `run_hidden_battle.py` if the existing Lua 5.1 DLL is installed elsewhere.

The Java gate contract uses the installed JDK 17; set `JAVA_HOME` to the local JDK before running:

```powershell
$env:JAVA_HOME = 'C:\Users\Administrator\.codex\tools\temurin17-portable\jdk-17.0.20+8'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/hidden-battle-sandbox/run-gate-contract.ps1
```
