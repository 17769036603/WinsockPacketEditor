# Battle loop animation regression

The tests execute the production `WinsockPacketEditor/mobile-suite/app/src/main/assets/battle_loop.lua.in` template in the local darktable Lua 5.4 host. The harness uses fake scheduler, sender, handlers, and memory files; it does not connect to a device or network.

From the repository root, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\.scratch\battle-loop-round3-fix-20261014\tests\run-tests.ps1
```

The copied legacy-compatible scenarios continue to cover live skill selection, retry and acknowledgement behavior, target validation, missing pets, identity changes, lease expiry, and stop cleanup. `verify-status-contract.ps1` retains the Lua -> Rust -> Android status check.

The animation contract is exercised as observable behavior: after actor ACKs and matching command/round state, a completed animation may finalize even if its command list is empty and playback index is `count+1`. An unfinished animation may be fast-completed only when the current command list is nonempty, `m_PauseAnalyze=false`, and `m_CurCmdPlayIndex` is within `1..count`; fast completion must preserve that cursor, pass identity/battle/round/completion checks, call `verifyRoundData(true)`, and pass those checks again before the one playover. Early, during-fast, and late natural 3472 paths keep the original callback and produce at most one playover for the bound role/battle/round. Phase changes, exceptions, and stop restore auto mode and owned hooks safely.

See [baseline-red.md](baseline-red.md) for the repair-free baseline failures and the later exhausted-index boundary regression.
