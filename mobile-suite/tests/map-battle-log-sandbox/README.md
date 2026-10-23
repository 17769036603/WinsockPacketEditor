# Map and battle observation log sandbox

The runner executes the current `app/src/main/assets/battle_loop.lua.in` template with the repository's battle-loop fixture and the existing Lua 5.1 runtime. It does not install dependencies or access a device. Set `LUA51_DLL` to the already installed Lua 5.1 DLL when automatic library discovery is unavailable.

From `mobile-suite`, run:

```powershell
& '<existing-python.exe>' tests/map-battle-log-sandbox/run_map_battle_log.py
```

The scenarios check only the local wrapper contract: original handler/send arguments and return values, error propagation, observed numeric protocol fields, event provenance, local map state, send sampling, missing handler/send behavior, getter and setter exceptions, skip-only hard failure, and conservative cleanup after the connection is replaced. A `send_call` row records a local call before forwarding; it does not show that a server accepted or received it. At most 20 send events and one `send_sample_limited` marker are written per elapsed-second window, so absence of a row does not mean the client sent nothing. `map_receive` records entry to the local 65505 handler and server data supplied in that handler argument; it is not server-side confirmation. No live game, network, or device behavior is covered.

The combined local regression output for this run is saved at the task root as `verification.log`.
