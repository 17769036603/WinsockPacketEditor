# Shared Frida validation

This isolated validation harness does not build, install, or alter the suite. `Validate-SharedFrida.ps1` checks shared-core ELF metadata, the six-module/STOP contract in source and documentation, and optionally compares native-library counts in supplied static/shared APKs.

`Invoke-SharedFridaDevice.ps1` implements the device stage, but fails closed unless passed `-Ready`. It requires `build/shared-frida-validation/static` and `shared` containing the five matching workers named below and `libfrida-core.so` in `shared`. It is restricted to `emulator-5554`, checks shell UID 2000/API 34/x86_64 and readable `smaps_rollup`, uses a new random `/data/local/tmp/piaomiao-shared-...` directory, and sets `LD_LIBRARY_PATH` explicitly for each process. Each group starts five foreground ADB processes concurrently and waits for their JSON `ready` PIDs before sampling; it takes one discarded warm-up frame and two retained frames one second apart. The PID is bound to `/proc/<pid>/cmdline` and `stat` startTicks. The script never uses `su`, an installed APK, or a game process. RSS does not substitute for unavailable PSS. The measurements describe init/version/deinit loader evidence only, not six-feature runtime savings.

## Deferred until ready

- Device ABI/SDK read via ordinary `adb shell getprop`.
- Push five workers and `libfrida-core.so` into one unique `/data/local/tmp/piaomiao-shared-...` directory; no installed APK or application-private directory is touched.
- One warm-up and two samples per worker in static/shared groups, bounded by process timeouts and PID/startTicks/cmdline ownership checks.
- Report bytes, decimal MB, and binary MiB separately; mark inaccessible PSS/PrivateDirty as unconfirmed.

`Test-DevicePreflight.ps1` exercises exact SDK/UID parsing, including SDK 134 and SDK 3 when unrelated text contains `34`.
