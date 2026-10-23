Baseline: production `battle_loop.lua.in` before the animation fix, darktable `lua54.dll` host, 2026-10-14.

Command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\.scratch\battle-loop-round3-fix-20261014\tests\run-tests.ps1
```

The legacy-compatible scenarios passed: behavior, commands-before-ack, rejection, unknown-target, no-pet, identity-change cleanup, lease expiry, missing-round-ack, stop cleanup, and Lua -> Rust -> Android status contract.

The original animation regression scenarios failed on the baseline at these assertions:

- `rounds-1-5`: `animation was not completed in round 1`
- `animation-waits`: `valid unpaused round did not resume and finish`
- `no-pet-animation`: `no-pet round did not complete pending animation`
- `natural-playover-phases`: `original callback was not preserved before/during fast completion`
- `phase-change`: `fast analyzer was not called for pending round`
- `phase-change-after-verify`: `round completion sequence did not reach verify`
- `exception-cleanup`: `exception did not clean owned state`

The first draft of `animation-finished` expected `verifyRoundData(true)` when `m_AnalyzeFinish` was already true. The confirmed contract clarified that verification is required after a fast analysis; the test was corrected to assert no fast call and no fast-path verification for an already-finished round.

A later boundary test, `finished-index-exhausted`, found a separate issue in the first implementation: a completed round with an empty command list and `idx=count+1` waited instead of emitting its single playover. The follow-up fix passes that case.

All checks use a local Lua state and fake scheduler, sender, handlers, and memory files. No device or network connection is made.
