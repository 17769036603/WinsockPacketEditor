# Offline stability baseline

Run date: 2026-10-18 (Asia/Shanghai)

Environment: Windows PowerShell; `tests/battle-loop-sandbox/run-lua.ps1` found and used the configured darktable Lua 5.4 DLL. `javac`, `java`, `cargo`, and `gradle` are unavailable on PATH. No Gradle or Cargo build was started. No ADB, game, network business request, installation, or device interaction was performed.

| Command | Result | Evidence |
| --- | --- | --- |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\starhunt-run-state-machine.ps1` | PASS | All 11 Lua state-machine scenarios completed, including lease expiry, challenge rebind, round-robin, wait-refresh, scan cadence/filter, and multilevel. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\activation-native\run-battle-skip-lease.ps1` | PASS | Missing/corrupt/expired/cross-boot/send-expiry/upper-bound lease scenarios passed. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\activation\verify-service-gates.ps1` | PASS | Five service gates, STOP bypass, restart/recovery gates, expiry and transport bounds passed. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\activation\verify-license-bypass.ps1` | PASS | License bypass contract passed. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\battle-loop-sandbox\run-tests.ps1` | FAIL | First `behavior` case fails before further cases: Lua status 2, `[string "now = 100..."]:200: attempt to concatenate a nil value`. This result is recorded as observed; root cause was not established in this baseline run. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\verify-starhunt-java-contract.ps1` | FAIL | `StarHunt contract check failed: DHXY-only target gate`. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\verify-starhunt-java-models.ps1` | NOT RUN | Blocked before compilation: `javac` not recognized. |
| `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\activation-integration\Run-ColdStopJavaStubs.ps1` | NOT RUN | Blocked before compilation: `javac` not recognized. |

This baseline does not establish multi-hour real-game stability. Long-duration game behavior, client/server effects, and on-device cleanup remain unconfirmed.
