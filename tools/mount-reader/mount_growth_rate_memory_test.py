"""Guarded one-shot writer for the currently bound ride's LuaJIT GROWUP TValue."""
from __future__ import annotations

import argparse
import importlib.util
import json
import math
import os
import struct
import subprocess
import tempfile
from pathlib import Path

PROBE_PATH = Path(__file__).with_name("mount_status_luajit_probe.py")
SPEED_WRITER_PATH = Path(__file__).with_name("mount_speed_memory_test.py")
JOURNAL_SCHEMA = "mount_growth_rate_memory_journal.v1"


def load(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"cannot load {path.name}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def encode_target(target: float, old_raw: bytes, probe) -> bytes:
    if not math.isfinite(target) or target < 0:
        raise ValueError("target must be finite and nonnegative")
    kind = probe.decode_tvalue(old_raw).get("kind")
    if kind == "i32":
        if not target.is_integer() or not -(1 << 31) <= target < (1 << 31):
            raise ValueError("i32 growth TValue requires an in-range integer")
        value = ((probe.LJ_TISNUM & 0x1FFFF) << 47) | (int(target) & 0xFFFFFFFF)
        return struct.pack("<Q", value)
    if kind == "f64":
        raw = struct.pack("<d", target)
        if probe.decode_tvalue(raw).get("kind") != "f64":
            raise ValueError("target is not representable as LuaJIT f64 TValue")
        return raw
    raise ValueError("growth field is not a finite i32 or f64 TValue")


def bound(snapshot: dict, pid: int, ride_id: str, player_id: int, mount_id: int) -> tuple[dict, dict, int]:
    process, player = snapshot.get("process"), snapshot.get("player")
    if (snapshot.get("status") != "ok" or snapshot.get("readOnly") is not True
            or snapshot.get("actionAuthorized") is not False or snapshot.get("candidateCount") != 1
            or not isinstance(process, dict) or not isinstance(player, dict)):
        raise RuntimeError("snapshot is not a unique read-only local-player candidate")
    if process.get("pid") != pid or not process.get("startTicks") or not process.get("exe"):
        raise RuntimeError("process identity could not be established")
    if (player.get("isLocalPlayer") is not True or player.get("isMounted") is not True
            or player.get("playerId") != player_id or player.get("mountId") != mount_id):
        raise RuntimeError("current local player or mounted ride does not match expected binding")
    instances = player.get("rideInstances")
    current = [item for item in instances if isinstance(item, dict) and item.get("isCurrent") is True] if isinstance(instances, list) else []
    matches = [item for item in current if item.get("rideInstanceId") == ride_id]
    if (player.get("rideBindingStatus") != "bound" or player.get("activeRideInstanceId") != ride_id
            or len(current) != 1 or len(matches) != 1 or matches[0].get("isRiding") is not True):
        raise RuntimeError("requested ride is not the unique bound current ride")
    growth = matches[0].get("growthRate")
    if isinstance(growth, bool) or not isinstance(growth, (int, float)) or not math.isfinite(growth):
        raise RuntimeError("bound ride growth rate is invalid")
    try:
        raw_address = matches[0]["growthRateAddress"]
        if not isinstance(raw_address, str) or not raw_address.startswith("0x"):
            raise ValueError("address must be hexadecimal")
        address = int(raw_address, 16)
    except (KeyError, TypeError, ValueError) as error:
        raise RuntimeError("bound ride growth TValue address is unavailable") from error
    if address <= 0:
        raise RuntimeError("bound ride growth TValue address is invalid")
    return process, player, address


def current_ride(player: dict, ride_id: str) -> dict:
    return next(
        item for item in player["rideInstances"]
        if item.get("rideInstanceId") == ride_id and item.get("isCurrent") is True
    )


def stable_growth_binding(args, ride_id, identity, binding_player, expected_value,
                         snapshot_provider, transport, probe):
    """Pick a fresh GC slot that remains bound across a read and re-snapshot."""
    for _ in range(3):
        snap = snapshot_provider()
        process, player, address = bound(
            snap, args.pid, ride_id, args.expected_player_id, args.expected_mount_id
        )
        growth = current_ride(player, ride_id)["growthRate"]
        if ({key: process.get(key) for key in identity} != identity
                or {key: player.get(key) for key in binding_player} != binding_player
                or float(growth) != expected_value):
            raise RuntimeError("process, ride binding, or growth value changed before write")
        raw = transport.read(address, 8)
        decoded = probe.decode_tvalue(raw)
        if (decoded.get("kind") not in ("i32", "f64")
                or float(decoded.get("value")) != expected_value):
            raise RuntimeError("fresh growth TValue does not match the recorded startup value")
        confirmed = snapshot_provider()
        confirmed_process, confirmed_player, confirmed_address = bound(
            confirmed, args.pid, ride_id, args.expected_player_id, args.expected_mount_id
        )
        confirmed_growth = current_ride(confirmed_player, ride_id)["growthRate"]
        if ({key: confirmed_process.get(key) for key in identity} != identity
                or {key: confirmed_player.get(key) for key in binding_player} != binding_player
                or float(confirmed_growth) != expected_value):
            raise RuntimeError("process, ride binding, or growth value changed before write")
        if confirmed_address == address:
            return confirmed_process, confirmed_player, address, raw
    raise RuntimeError("growth TValue address kept moving during bounded rebind")


def _write_journal(path: Path, binding: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, temporary = tempfile.mkstemp(dir=path.parent, prefix=path.name + ".")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump({"schema": JOURNAL_SCHEMA, "binding": binding}, stream, separators=(",", ":"))
            stream.flush()
            os.fsync(stream.fileno())
        try:
            os.link(temporary, path)
        except FileExistsError as error:
            raise RuntimeError("restore journal already exists; choose another --journal") from error
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def restore(args, transport, snapshot_provider, probe) -> dict:
    try:
        journal = json.loads(Path(args.journal).read_text(encoding="utf-8"))
        saved = journal["binding"]
        old_raw, target_raw = bytes.fromhex(saved["oldRaw"]), bytes.fromhex(saved["targetRaw"])
    except (OSError, ValueError, TypeError, KeyError) as error:
        raise RuntimeError("restore journal is missing or invalid") from error
    if (journal.get("schema") != JOURNAL_SCHEMA or len(old_raw) != 8 or len(target_raw) != 8
            or saved.get("rideInstanceId") != str(args.ride_instance_id)
            or saved.get("playerId") != args.expected_player_id
            or saved.get("mountId") != args.expected_mount_id
            or saved.get("address") != args.expected_growth_address
            or saved.get("serial") != args.serial
            or saved.get("runAsPackage") != getattr(args, "run_as_package", "")):
        raise RuntimeError("restore journal does not match the requested ride binding")
    old_value, target = probe.decode_tvalue(old_raw), probe.decode_tvalue(target_raw)
    if (old_value.get("kind") not in ("i32", "f64") or target.get("kind") != old_value.get("kind")
            or isinstance(old_value.get("value"), bool) or isinstance(target.get("value"), bool)):
        raise RuntimeError("restore journal TValue types are invalid")
    try:
        old_number, target_number = float(old_value["value"]), float(target["value"])
    except (KeyError, TypeError, ValueError) as error:
        raise RuntimeError("restore journal TValue values are invalid") from error
    if not math.isfinite(old_number) or not math.isfinite(target_number) or old_number < 0 or target_number < 0:
        raise RuntimeError("restore journal TValue values must be finite and nonnegative")
    expected_growth = getattr(args, "expected_growth_rate", None)
    if (expected_growth is None or not math.isfinite(expected_growth) or expected_growth < 0
            or old_number != float(expected_growth)):
        raise RuntimeError("journaled old growth rate does not match --expected-growth-rate")
    try:
        snap = snapshot_provider()
        process, player, address = bound(snap, args.pid, saved["rideInstanceId"], saved["playerId"], saved["mountId"])
    except Exception as error:
        return {"action": "restore", "written": False, "writeAttempted": False,
                "status": "skipped", "error": str(error) or type(error).__name__}
    expected = {"pid": args.pid, "startTicks": args.expected_start_ticks, "exe": args.expected_exe}
    if any(process.get(key) != value or saved.get(key) != value for key, value in expected.items()):
        return {"action": "restore", "written": False, "writeAttempted": False,
                "status": "skipped", "error": "process identity changed since the write"}
    # ponytail: use the newly bound GC slot on stop; game-thread pinning is the synchronization upgrade.
    if float(current_ride(player, saved["rideInstanceId"])["growthRate"]) != target_number:
        return {"action": "restore", "written": False, "writeAttempted": False,
                "status": "skipped", "error": "current ride growth differs from the journaled target"}
    try:
        current_raw = transport.read(address, 8)
    except Exception as error:
        return {"action": "restore", "written": False, "writeAttempted": False,
                "status": "ambiguous", "error": str(error) or type(error).__name__}
    if current_raw != target_raw:
        return {"action": "restore", "written": False, "writeAttempted": False,
                "status": "skipped", "error": "current raw TValue differs from this tool's written value"}
    if not args.write:
        return {"action": "restore", "written": False, "status": "dry_run", "oldRaw": old_raw.hex()}
    result = {"action": "restore", "written": False, "writeAttempted": True,
              "status": "ambiguous", "address": hex(address)}
    try:
        transport.write(address, old_raw, expected_start_ticks=expected["startTicks"], expected_raw=target_raw)
        if transport.read(address, 8) != old_raw:
            raise RuntimeError("restore write verification failed")
        after = snapshot_provider()
        after_process, after_player, after_address = bound(
            after, args.pid, saved["rideInstanceId"], saved["playerId"], saved["mountId"])
        after_growth = current_ride(after_player, saved["rideInstanceId"])["growthRate"]
        if ({key: after_process.get(key) for key in expected} != expected
                or {key: after_player.get(key) for key in ("playerId", "mountId")} != {
                    "playerId": saved["playerId"], "mountId": saved["mountId"]}
                or current_ride(after_player, saved["rideInstanceId"]).get("rideInstanceId") != saved["rideInstanceId"]
                or float(after_growth) != old_number):
            raise RuntimeError("post-restore bound ride GROWUP did not verify")
    except Exception as error:
        result["error"] = str(error) or type(error).__name__
        return result
    result.update({"written": True, "status": "restored", "oldRaw": old_raw.hex()})
    try:
        Path(args.journal).unlink()
    except OSError as error:
        result["journalCleanupError"] = str(error) or type(error).__name__
        result["journalRetained"] = True
    return result


def run(args, transport, snapshot_provider, probe) -> dict:
    if not math.isfinite(args.target) or args.target < 0:
        raise ValueError("target must be finite and nonnegative")
    if not math.isfinite(args.expected_growth_rate) or args.expected_growth_rate < 0:
        raise ValueError("expected growth rate must be finite and nonnegative")
    ride_id = str(args.ride_instance_id)
    if len(ride_id) != 19 or not ride_id.isascii() or not ride_id.isdigit():
        raise ValueError("ride instance ID must contain 19 ASCII digits")
    if args.expected_growth_address <= 0:
        raise ValueError("expected growth address must be a positive pointer")

    initial = snapshot_provider()
    process, player, _ = bound(
        initial, args.pid, ride_id, args.expected_player_id, args.expected_mount_id
    )
    identity = {
        "pid": args.pid,
        "startTicks": args.expected_start_ticks,
        "exe": args.expected_exe,
    }
    if any(process.get(key) != value for key, value in identity.items()):
        raise RuntimeError("process identity changed since the displayed snapshot")
    binding_player = {"playerId": args.expected_player_id, "mountId": args.expected_mount_id}
    if float(current_ride(player, ride_id)["growthRate"]) != float(args.expected_growth_rate):
        raise RuntimeError("growth value changed since the displayed snapshot")
    process, player, address, old_raw = stable_growth_binding(
        args, ride_id, identity, binding_player, float(args.expected_growth_rate),
        snapshot_provider, transport, probe
    )
    base = {**identity, **binding_player, "rideInstanceId": ride_id, "address": address,
            "serial": getattr(args, "serial", ""),
            "runAsPackage": getattr(args, "run_as_package", "")}
    old_value = probe.decode_tvalue(old_raw)
    try:
        numeric_value = float(old_value["value"])
    except (KeyError, TypeError, ValueError) as error:
        raise RuntimeError("growth field is not a finite numeric TValue") from error
    if (old_value.get("kind") not in ("i32", "f64") or not math.isfinite(numeric_value)
            or numeric_value != float(args.expected_growth_rate)
            or float(current_ride(player, ride_id)["growthRate"]) != numeric_value):
        raise RuntimeError("growth value changed since the displayed snapshot")
    if numeric_value == args.target:
        return {"action": "set", "status": "field_verified", "fieldVerified": True,
                "written": False, "writeAttempted": False, "address": hex(address),
                "oldValue": numeric_value, "newValue": args.target, "binding": base}
    target_raw = encode_target(args.target, old_raw, probe)
    if not args.write:
        return {
            "action": "set", "written": False, "writeAttempted": False, "status": "dry_run",
            "address": hex(address), "oldValue": numeric_value, "newValue": args.target,
            "binding": base,
        }

    journal_path = Path(args.journal)
    _write_journal(journal_path, {**base, "oldRaw": old_raw.hex(), "targetRaw": target_raw.hex()})
    # ponytail: compare-write still races LuaJIT relocation; a game-thread hook is the synchronization upgrade.
    result = {
        "action": "set", "status": "ambiguous", "writeAttempted": True,
        "address": hex(address), "oldValue": numeric_value, "newValue": args.target,
        "binding": base,
    }
    try:
        transport.write(
            address, target_raw,
            expected_start_ticks=identity["startTicks"],
            expected_raw=old_raw,
        )
        actual = transport.read(address, 8)
        actual_value = probe.decode_tvalue(actual)
        if (actual != target_raw or actual_value.get("kind") not in ("i32", "f64")
                or float(actual_value.get("value")) != args.target):
            raise RuntimeError("growth TValue address read-back did not match target")
    except Exception as error:
        result["error"] = str(error) or type(error).__name__
        return result

    try:
        after = snapshot_provider()
        after_process, after_player, after_address = bound(
            after, args.pid, ride_id, args.expected_player_id, args.expected_mount_id
        )
        after_growth = current_ride(after_player, ride_id)["growthRate"]
        if ({key: after_process.get(key) for key in identity} != identity
                or {key: after_player.get(key) for key in binding_player} != binding_player
                or float(after_growth) != args.target):
            raise RuntimeError("post-write bound ride GROWUP did not verify")
    except Exception as error:
        result.update({
            "status": "ambiguous", "fieldVerified": False,
            "addressReadback": args.target,
            "error": str(error) or type(error).__name__,
        })
        return result

    result.update({"written": True, "status": "field_verified", "fieldVerified": True})
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    # Preserve the existing desktop one-shot caller, which predates actions
    # and defaults to set; the standalone CLI can also request restore.
    parser.add_argument("action", nargs="?", choices=("set", "restore"), default="set")
    parser.add_argument("--adb", required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--pid", required=True, type=int)
    parser.add_argument("--expected-start-ticks", required=True, type=int)
    parser.add_argument("--expected-exe", required=True)
    parser.add_argument("--ride-instance-id", required=True)
    parser.add_argument("--expected-player-id", required=True, type=int)
    parser.add_argument("--expected-mount-id", required=True, type=int)
    parser.add_argument("--expected-growth-rate", type=float)
    parser.add_argument("--expected-growth-address", required=True, type=lambda value: int(value, 0))
    parser.add_argument("--target", type=float, default=10.935)
    parser.add_argument("--journal", default="mount_growth_rate_memory_test.json")
    parser.add_argument("--run-as-package", default="", help="debuggable Android package used with run-as")
    parser.add_argument("--root-broker-pipe", default="", help="existing desktop root broker pipe")
    parser.add_argument("--write", action="store_true", help="perform the guarded one-shot memory write")
    args = parser.parse_args()
    if args.action == "restore" and args.expected_growth_rate is None:
        parser.error("--expected-growth-rate is required for restore")
    runtime = None
    try:
        probe = load(PROBE_PATH, "mount_growth_probe")
        package = probe.validate_run_as_package(args.run_as_package) if args.run_as_package else ""
        root_broker_pipe = str(args.root_broker_pipe or "").strip()
        if bool(package) == bool(root_broker_pipe):
            raise ValueError("supply exactly one of --run-as-package or --root-broker-pipe")
        args.run_as_package = package
        writer = load(SPEED_WRITER_PATH, "mount_growth_transport")
        runtime = probe.ProbeRuntime(root_broker_pipe=root_broker_pipe, run_as_package=package)
        transport = writer.AdbTransport(
            args.adb, args.serial, args.pid, run_as_package=package,
            root_broker=runtime._root_broker,
        )
        provider = lambda: writer.capture(
            probe, args.adb, args.serial, args.pid, package, runtime=runtime,
            root_broker_pipe=root_broker_pipe,
        )
        if args.action == "restore":
            result = restore(args, transport, provider, probe)
        else:
            if args.expected_growth_rate is None:
                parser.error("--expected-growth-rate is required for set")
            result = run(args, transport, provider, probe)
        print(json.dumps(result, separators=(",", ":")))
        return 0 if result.get("status") in ("dry_run", "field_verified", "restored") else 2
    except (RuntimeError, ValueError, OSError, subprocess.SubprocessError) as error:
        print(json.dumps({"written": False, "writeAttempted": False, "status": "error", "error": str(error)}, separators=(",", ":")))
        return 2
    finally:
        if runtime is not None:
            runtime.close()


if __name__ == "__main__":
    raise SystemExit(main())
