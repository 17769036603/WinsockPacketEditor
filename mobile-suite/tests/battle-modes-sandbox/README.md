# Split battle modes verification

Run from `mobile-suite` with the existing Python runtime:

```powershell
$python = 'C:\Users\Administrator\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
& $python tests/battle-modes-sandbox/run_battle_modes.py
& $python tests/battle-modes-sandbox/verify_native_options.py
& $python tests/battle-modes-sandbox/verify_java_options.py
& $python tests/battle-modes-sandbox/verify_old_task_check.py
```

The Lua runner imports the existing Lua 5.1 ctypes host and combines the existing battle-loop and hidden-battle fixtures. It runs nine cases: skip-only, hide-only, both-on, fresh both-off, both-off with an existing owner/status, legacy missing `skipBattle`, and hide-only stop, role-change, and original-handler-error behavior.

The Rust test compiles the actual `BattleOptions`, `parse_battle_options`, `read_battle_options`, `may_publish_no_hook_terminal`, `cleanup_confirmed`, `terminal_loop_status`, `publish_loop_status`, and `incomplete_cleanup_status` implementations extracted from `native/battleloop/worker-rust/src/main.rs` with `rustc --test`; a tiny status sink captures the actual publish formatter output. Compiler discovery uses `RUSTC` when set, then `PATH`; if neither resolves `rustc`, the test fails. It writes build output only to a temporary directory.

The Java tests host-compile the production `BattleVisualOptions.java` against minimal Android API stubs, exercise defaults and all independent option combinations, then check that service startup and activity auto-start guards precede launch dispatch. A second harness compiles the actual `MainActivity` old-task control and request methods against minimal UI/service stubs to verify active refusal, disabled CHECK button, the existing checking guard, and idle CHECK dispatch. Compiler discovery uses `JAVA_HOME` when it contains both `javac` and `java`, then `PATH`; if no JDK is found, the test fails. For an explicit setup, set `$env:JAVA_HOME` to the JDK root and `$env:RUSTC` to `rustc.exe` before running the commands above.

Existing regressions were also run with the Lua 5.1 hidden-battle runner, the Lua 5.4 battle-loop harness, and `verify-status-contract.ps1`. The historical `unknown-target.lua` expectation is a known pre-existing failure and was excluded from the post-change run as directed; it is not evidence of a regression from split modes.
