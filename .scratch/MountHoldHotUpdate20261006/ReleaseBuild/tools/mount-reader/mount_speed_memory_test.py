"""Explicit client runtime speed memory test with bounded and persistent holds; never sends packets."""
from __future__ import annotations

import argparse
import base64
import contextlib
import importlib.util
import io
import json
import math
import os
import queue
import struct
import subprocess
import sys
import tempfile
import threading
import time
from pathlib import Path

PROBE_PATH = Path(__file__).with_name("mount_status_luajit_probe.py")
JOURNAL_SCHEMA = "mount_speed_memory_journal.v1"
HOLD_LOG_MAX_FILE_BYTES = 5 * 1024 * 1024
HOLD_LOG_ARCHIVE_COUNT = 20


class HoldLogger:
    def __init__(self, path: str, run_id: str, preset_name: str, preset_id: str):
        self.path, self.run_id, self.preset_name, self.preset_id = Path(path), run_id, preset_name, preset_id
        self.failed = False
        self.sync = threading.Lock()

    def log(self, event: str, data: dict) -> None:
        with self.sync:
            if self.failed:
                return
            try:
                self.path.parent.mkdir(parents=True, exist_ok=True)
                record = {"schemaVersion": 1, "timestampUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                    "runId": self.run_id, "presetName": self.preset_name, "presetId": self.preset_id,
                    "source": "reader", "event": event, "data": data}
                line = json.dumps(record, ensure_ascii=False, separators=(",", ":"))
                upcoming_bytes = len((line + "\n").encode("utf-8"))
                if self.path.exists() and self.path.stat().st_size + upcoming_bytes > HOLD_LOG_MAX_FILE_BYTES:
                    oldest = self.path.with_name(self.path.name + f".{HOLD_LOG_ARCHIVE_COUNT}")
                    oldest.unlink(missing_ok=True)
                    for index in range(HOLD_LOG_ARCHIVE_COUNT - 1, 0, -1):
                        source = self.path.with_name(self.path.name + f".{index}")
                        if source.exists():
                            source.replace(self.path.with_name(self.path.name + f".{index + 1}"))
                    self.path.replace(self.path.with_name(self.path.name + ".1"))
                with self.path.open("a", encoding="utf-8", newline="\n") as stream:
                    stream.write(line + "\n")
                    stream.flush()
            except Exception as error:
                self.failed = True
                print(f"speed-hold log unavailable: {error}", file=sys.stderr)


def speed_info(probe, raw: bytes) -> dict:
    decoded = probe.decode_tvalue(raw)
    return {"kind": decoded.get("kind"), "value": decoded.get("value")}


def load_probe():
    spec = importlib.util.spec_from_file_location("mount_speed_probe_for_write", PROBE_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("cannot load the existing mount status probe")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def encode_speed(speed: float, old_raw: bytes, probe=None) -> bytes:
    if not math.isfinite(speed) or speed <= 0:
        raise ValueError("speed must be finite and positive")
    probe = probe or load_probe()
    decoded_kind = probe.decode_tvalue(old_raw).get("kind")
    if decoded_kind == "i32":
        if not speed.is_integer() or not -(1 << 31) <= speed < (1 << 31):
            raise ValueError("i32 speed requires an in-range integer")
        value = ((0xFFFFFFF2 & 0x1FFFF) << 47) | (int(speed) & 0xFFFFFFFF)
        return struct.pack("<Q", value)
    if decoded_kind == "f64":
        raw = struct.pack("<d", speed)
        if probe.decode_tvalue(raw).get("kind") != "f64":
            raise ValueError("speed cannot be represented as LuaJIT f64 TValue")
        return raw
    raise ValueError("speed field is not an i32 or f64 TValue")


class AdbTransport:
    def __init__(self, adb: str, serial: str, pid: int, run_as_package: str = "", root_broker=None, dd_command: str | None = None, binary_base64: bool = False):
        self.adb, self.serial, self.pid = adb, serial, pid
        self.run_as_package = run_as_package.strip()
        self.root_broker = root_broker
        self.dd_command = dd_command or "toybox dd"
        self.binary_base64 = binary_base64

    def shell(self, command: str) -> bytes:
        if self.root_broker is not None:
            return self.root_broker.execute(command, 8.0)
        args = [self.adb]
        if self.serial:
            args += ["-s", self.serial]
        if self.run_as_package:
            args += ["exec-out", "run-as", self.run_as_package, "sh", "-c", command]
        else:
            args += ["exec-out", "su", "-c", command]
        result = subprocess.run(args, capture_output=True, check=True, timeout=8.0)
        return result.stdout

    def read(self, address: int, size: int, phase: str = "read") -> bytes:
        if self.root_broker is not None:
            command = f"{self.dd_command} if=/proc/{self.pid}/mem bs=1 skip={address} count={size} 2>/dev/null | base64 -w 0"
        elif self.binary_base64:
            command = f"{self.dd_command} if=/proc/{self.pid}/mem bs=1 skip={address} count={size} 2>/dev/null | base64 -w 0"
        else:
            command = f"{self.dd_command} if=/proc/{self.pid}/mem bs=1 skip={address} count={size} 2>/dev/null"
        for attempt in range(4):
            result = self.shell(command)
            if self.root_broker is not None:
                try:
                    result = base64.b64decode(result.strip(), validate=True)
                except (ValueError, TypeError) as error:
                    raise RuntimeError("root broker returned invalid base64 memory data") from error
            elif self.binary_base64:
                try:
                    result = base64.b64decode(result.strip(), validate=True)
                except (ValueError, TypeError) as error:
                    raise RuntimeError("ADB returned invalid base64 memory data") from error
            if len(result) == size:
                return result
        raise RuntimeError(
            f"short process memory read pid={self.pid} address=0x{address:x} "
            f"expected={size} actual={len(result)} attempts=4 phase={phase}"
        )

    def write(
        self,
        address: int,
        raw: bytes,
        expected_start_ticks: int | None = None,
        expected_raw: bytes | None = None,
    ) -> None:
        if expected_start_ticks is None or expected_start_ticks <= 0:
            raise ValueError("expected process start ticks are required")
        if expected_raw is not None and len(expected_raw) != len(raw):
            raise ValueError("expected and replacement memory values must have the same length")
        escaped = "".join(f"\\x{byte:02x}" for byte in raw)
        compare = ""
        if expected_raw is not None:
            expected_b64 = base64.b64encode(expected_raw).decode("ascii")
            compare = (
                f"current=$({self.dd_command} if=/proc/$$/fd/3 bs=1 skip={address} count={len(expected_raw)} "
                f"2>/dev/null | base64 -w 0); "
                f"[ \"$current\" = '{expected_b64}' ] || exit 54; "
            )
        command = (
            f"exec 3<>/proc/{self.pid}/mem || exit 51; "
            f"stat=$(cat /proc/{self.pid}/stat) || exit 52; "
            f"rest=${{stat##*) }}; set -- $rest; "
            f"[ \"${{20}}\" = {expected_start_ticks} ] || exit 53; "
            f"{compare}"
            f"printf '{escaped}' | {self.dd_command} "
            f"of=/proc/$$/fd/3 bs=1 seek={address} conv=notrunc 2>/dev/null"
        )
        # ponytail: the bound fd still races LuaJIT table relocation/GC; use a thread-synchronized client test hook if needed.
        self.shell(command)


def identity(probe, adb: str, serial: str, pid: int, run_as_package: str = "", root_broker_pipe: str = "") -> dict:
    if run_as_package or root_broker_pipe:
        broker = probe.RootBrokerClient(root_broker_pipe) if root_broker_pipe else None
        session = probe.AdbPageMemoryReadSession(adb, serial, pid, root_broker=broker, run_as_package=run_as_package)
        try:
            value = probe.process_identity(adb, serial, pid, 8.0, session=session)
        finally:
            session.close()
    else:
        value = probe.process_identity(adb, serial, pid, 8.0)
    if not value or value.get("pid") != pid or not value.get("startTicks") or not value.get("exe"):
        raise RuntimeError("process identity could not be established")
    return value


def capture(probe, adb: str, serial: str, pid: int, run_as_package: str = "", runtime=None, root_broker_pipe: str = "", speed_only: bool = False, hold_rebind: bool = False) -> dict:
    args = argparse.Namespace(adb=adb, serial=serial, pid=pid, chunk_size=0x200000,
        max_ranges=0, min_range=0x10000, max_range=0x10000000, read_timeout=8.0,
        candidate_distance=0x2000, cache_path="", cache_only=False, server=False,
        require_complete_refine_cards=False, preferred_refine_ride_instance_id="",
        root_broker_pipe=root_broker_pipe, run_as_package=run_as_package,
        speed_only=speed_only, hold_rebind=hold_rebind)
    output = io.StringIO()
    with contextlib.redirect_stdout(output):
        code = probe.probe_once(args, runtime=runtime)
    lines = [line for line in output.getvalue().splitlines() if line.startswith("{")]
    if code != 0 or len(lines) != 1:
        raise RuntimeError("probe did not produce one valid snapshot")
    return json.loads(lines[0])


def candidate_from(snapshot: dict, mount_id: int | None) -> tuple[dict, int]:
    player = snapshot.get("player")
    if snapshot.get("status") != "ok" or snapshot.get("candidateCount") != 1 or not isinstance(player, dict):
        raise RuntimeError("snapshot is not a unique local-player candidate")
    current_mount_id = player.get("mountId")
    if player.get("isMounted") is not True or not isinstance(current_mount_id, int) or current_mount_id <= 0:
        raise RuntimeError("local player is not mounted")
    if mount_id is not None and current_mount_id != mount_id:
        raise RuntimeError("mounted mount ID does not match --mount-id")
    refs = player.get("references", {})
    address = int(refs["m_RoleMoveSpeed"]["nodeValueAddress"], 0)
    return player, address


def validate_snapshot(snapshot: dict, expected: dict, mount_id: int, allow_address_change: bool = False) -> tuple[dict, int]:
    proc = snapshot.get("process", {})
    for key in ("pid", "startTicks", "exe"):
        if proc.get(key) != expected[key]:
            raise RuntimeError("process identity changed")
    player, address = candidate_from(snapshot, mount_id)
    if player.get("playerId") != expected["playerId"]:
        raise RuntimeError("player changed")
    if not allow_address_change and address != expected["address"]:
        raise RuntimeError("speed field address changed")
    return player, address


def run_hold(args, transport, snapshot_provider, probe, run_as_package, stop_requested=None) -> dict:
    logger = getattr(args, "_hold_logger", None)
    started = time.monotonic()
    sequence = 0
    prior_binding = None

    def log(event, **data):
        if logger:
            logger.log(event, data)

    def read_speed(address, phase):
        before = time.monotonic()
        try:
            raw = transport.read(address, 8, phase=phase)
            log("speedRead", phase=phase, address=f"0x{address:x}", status="ok",
                elapsedMs=round((time.monotonic() - before) * 1000, 2), speed=speed_info(probe, raw))
            return raw
        except Exception as error:
            log("speedRead", phase=phase, address=f"0x{address:x}", status="error", diagnostic=str(error),
                elapsedMs=round((time.monotonic() - before) * 1000, 2))
            raise

    def write_speed(address, raw, expected_ticks, expected_raw, phase):
        before = time.monotonic()
        try:
            transport.write(address, raw, expected_ticks, expected_raw=expected_raw)
            log("speedWrite", phase=phase, address=f"0x{address:x}", status="ok",
                elapsedMs=round((time.monotonic() - before) * 1000, 2), speed=speed_info(probe, raw))
        except Exception as error:
            log("speedWrite", phase=phase, address=f"0x{address:x}", status="error",
                elapsedMs=round((time.monotonic() - before) * 1000, 2), speed=speed_info(probe, raw),
                diagnostic=str(error))
            raise

    def get_snapshot(phase):
        nonlocal sequence, prior_binding
        sequence += 1
        before = time.monotonic()
        log("probeRequest", phase=phase, sequence=sequence, speedOnly=True)
        try:
            snap = snapshot_provider()
            process = snap.get("process") if isinstance(snap, dict) else None
            player = snap.get("player") if isinstance(snap, dict) else None
            address_value = ((player or {}).get("references") or {}).get("m_RoleMoveSpeed", {}).get("nodeValueAddress")
            try:
                address_value = f"0x{int(address_value, 0) if isinstance(address_value, str) else int(address_value):x}"
            except (TypeError, ValueError):
                address_value = None
            binding = {"pid": (process or {}).get("pid"), "startTicks": (process or {}).get("startTicks"),
                "exe": (process or {}).get("exe"), "playerId": (player or {}).get("playerId"),
                "mountId": (player or {}).get("mountId"), "address": ((player or {}).get("references") or {}).get("m_RoleMoveSpeed", {}).get("nodeValueAddress")}
            binding["address"] = address_value
            if prior_binding is not None and binding != prior_binding:
                log("bindingChange", previous=prior_binding, current=binding, mapChangeSuspected=True)
            prior_binding = binding
            reader = snap.get("reader") or {}
            log("probeResult", phase=phase, sequence=sequence, status=snap.get("status"),
                diagnostic=snap.get("diagnosticCode"), readPath=reader.get("readPath"), timing=reader.get("timingMs"),
                elapsedMs=round((time.monotonic() - before) * 1000, 2), snapshot=snap)
            return snap
        except Exception as error:
            log("probeResult", phase=phase, sequence=sequence, status="error", diagnostic=str(error),
                elapsedMs=round((time.monotonic() - before) * 1000, 2))
            raise

    duration = getattr(args, "duration", 60.0)
    interval = getattr(args, "interval", 1.0)
    persistent = getattr(args, "persistent", False)
    outcome = "running"
    if persistent:
        interval = min(interval, 0.1)
    if not args.write:
        raise ValueError("hold requires --write")
    if not persistent and (not math.isfinite(duration) or duration <= 0 or duration > 120):
        raise ValueError("hold duration must be >0 and <=120 seconds")
    if not math.isfinite(interval) or interval < 0.1 or (not persistent and interval > duration):
        raise ValueError("hold interval must be >=0.1 seconds and no greater than duration")
    if args.speed is None or not math.isfinite(args.speed) or args.speed <= 0:
        raise ValueError("hold speed must be finite and positive")
    preflight_player_id = getattr(args, "preflight_player_id", None)
    preflight_start_ticks = getattr(args, "preflight_start_ticks", None)
    preflight_exe = getattr(args, "preflight_exe", None)
    preflight_speed_address = getattr(args, "preflight_speed_address", None)
    preflight_values = (
        preflight_player_id,
        preflight_start_ticks,
        preflight_exe,
        preflight_speed_address,
    )
    if any(value is not None for value in preflight_values):
        if (not isinstance(preflight_player_id, int) or preflight_player_id <= 0 or
                not isinstance(preflight_start_ticks, int) or preflight_start_ticks <= 0 or
                not isinstance(preflight_exe, str) or not preflight_exe or
                not isinstance(preflight_speed_address, int) or
                not 0 < preflight_speed_address <= 0xFFFFFFFFFFFFFFFF or
                not isinstance(args.mount_id, int) or args.mount_id <= 0):
            raise RuntimeError("preflight binding is incomplete or invalid")
        ident = {
            "pid": args.pid,
            "startTicks": preflight_start_ticks,
            "exe": preflight_exe,
        }
        player = {"playerId": preflight_player_id, "mountId": args.mount_id}
        addr = preflight_speed_address
    else:
        snap = get_snapshot("initial-binding")
        ident = snap.get("process")
        if (not isinstance(ident, dict) or ident.get("pid") != args.pid or
                not isinstance(ident.get("startTicks"), int) or ident["startTicks"] <= 0 or
                not isinstance(ident.get("exe"), str) or not ident["exe"]):
            raise RuntimeError("process identity could not be established from the snapshot")
        player, addr = candidate_from(snap, args.mount_id)
    binding = {"pid": args.pid, "startTicks": ident["startTicks"], "exe": ident["exe"],
        "playerId": player["playerId"], "mountId": player["mountId"], "address": f"0x{addr:x}"}
    prior_binding = dict(binding)
    log("bindingStart", **binding, preflight=all(value is not None for value in preflight_values))
    baseline = read_speed(addr, "hold-baseline")
    decoded = probe.decode_tvalue(baseline)
    if decoded.get("kind") not in {"i32", "f64"} or not math.isfinite(float(decoded.get("value", 0))) or decoded["value"] <= 0:
        raise RuntimeError("current speed TValue is invalid")
    desired = encode_speed(args.speed, baseline, probe)
    if baseline == desired:
        raise RuntimeError(
            "current speed already equals the target; the normal value cannot be inferred safely. "
            "Restore the normal speed in game, then start the hold preset."
        )
    target = Path(args.journal)
    target.parent.mkdir(parents=True, exist_ok=True)
    journal_created = False
    fd, temporary = tempfile.mkstemp(dir=target.parent, prefix=target.name + ".")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump({"schema": JOURNAL_SCHEMA, "binding": {**binding,
                "address": addr, "oldRaw": baseline.hex(), "newRaw": desired.hex()}},
                stream, separators=(",", ":"))
            stream.flush()
            os.fsync(stream.fileno())
        try:
            os.link(temporary, target)
            journal_created = True
            log("journal", action="created", path=str(target))
        except FileExistsError as error:
            raise RuntimeError("restore journal already exists; choose another --journal") from error
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)

    write_attempted = False
    safely_released = False
    retain_journal = False
    try:
        def validate_current_once(allow_unknown=False):
            nonlocal addr, baseline, desired, binding
            fresh = get_snapshot("hold-recheck")
            fresh_player, fresh_addr = validate_snapshot(
                fresh, {**ident, "playerId": player["playerId"], "address": addr},
                player["mountId"], allow_address_change=True)
            if fresh_addr != addr:
                next_baseline = read_speed(fresh_addr, "hold-rebind-baseline")
                decoded = probe.decode_tvalue(next_baseline)
                if (decoded.get("kind") not in {"i32", "f64"} or
                        not math.isfinite(float(decoded.get("value", 0))) or decoded["value"] <= 0):
                    raise RuntimeError("new speed address has an invalid baseline")
                if next_baseline == desired and write_attempted:
                    # The target may have followed a previously written value
                    # through Lua table relocation; keep its journaled baseline.
                    rebound_baseline = baseline
                    next_desired = desired
                else:
                    next_desired = encode_speed(args.speed, next_baseline, probe)
                    if next_baseline == next_desired:
                        raise RuntimeError("new speed address already equals target; normal value cannot be inferred safely")
                    rebound_baseline = next_baseline
                next_binding = {**binding, "address": f"0x{fresh_addr:x}"}
                # Persist the new recovery target before any write can touch it.
                fd, temporary = tempfile.mkstemp(dir=target.parent, prefix=target.name + ".")
                try:
                    with os.fdopen(fd, "w", encoding="utf-8") as stream:
                        json.dump({"schema": JOURNAL_SCHEMA, "binding": {**next_binding,
                            "address": fresh_addr, "oldRaw": rebound_baseline.hex(), "newRaw": next_desired.hex()}},
                            stream, separators=(",", ":"))
                        stream.flush()
                        os.fsync(stream.fileno())
                    os.replace(temporary, target)
                finally:
                    if os.path.exists(temporary):
                        os.unlink(temporary)
                old_addr = addr
                addr, baseline, desired, binding = fresh_addr, rebound_baseline, next_desired, next_binding
                log("bindingRebound", previousAddress=f"0x{old_addr:x}", address=f"0x{addr:x}",
                    playerId=fresh_player["playerId"], mountId=fresh_player["mountId"])
            current = read_speed(addr, "hold-rebind")
            if not allow_unknown and current not in (baseline, desired):
                raise RuntimeError("current TValue changed to an unknown value during hold")
            return current

        def validate_current(allow_unknown=False, retry_unknown=True):
            max_attempts = 3 if retry_unknown and not allow_unknown else 1
            for attempt in range(max_attempts):
                try:
                    return validate_current_once(allow_unknown)
                except RuntimeError as error:
                    if "current TValue changed to an unknown value during hold" not in str(error):
                        raise
                    if attempt + 1 >= max_attempts:
                        raise
                    log("holdUnknownValueRetry", attempt=attempt + 1, maxAttempts=max_attempts)
            raise AssertionError("unreachable")

        writes = 0
        consecutive_write_verification_misses = 0
        max_consecutive_write_verification_misses = 3

        def write_and_verify(current):
            nonlocal write_attempted, writes, consecutive_write_verification_misses
            write_attempted = True
            write_speed(addr, desired, ident["startTicks"], current, "hold")
            observed = read_speed(addr, "hold-write-verification")
            if observed == desired:
                writes += 1
                consecutive_write_verification_misses = 0
                return
            if observed != baseline:
                raise RuntimeError("hold write verification failed")

            consecutive_write_verification_misses += 1
            log("holdWriteVerificationMiss", consecutiveFailures=consecutive_write_verification_misses,
                maxConsecutiveFailures=max_consecutive_write_verification_misses,
                speed=speed_info(probe, observed))
            if consecutive_write_verification_misses >= max_consecutive_write_verification_misses:
                raise RuntimeError(
                    "hold write verification failed after " +
                    str(max_consecutive_write_verification_misses) + " consecutive attempts"
                )
            log("holdWriteVerificationRetry",
                nextAttempt=consecutive_write_verification_misses + 1,
                maxAttempts=max_consecutive_write_verification_misses)

        # Rebind immediately after journal creation, before the first write.
        current = validate_current()
        if baseline != desired and current != desired:
            write_and_verify(current)

        deadline = None if persistent else time.monotonic() + duration
        while deadline is None or time.monotonic() < deadline:
            wait = interval if deadline is None else min(interval, max(0, deadline - time.monotonic()))
            if stop_requested is not None and stop_requested(wait):
                log("stopSignal", source="stdin", elapsedMs=round((time.monotonic() - started) * 1000, 2))
                break
            if stop_requested is None:
                time.sleep(wait)
            if deadline is not None and time.monotonic() >= deadline:
                break
            current = validate_current()
            if current == desired and consecutive_write_verification_misses:
                writes += 1
                log("holdWriteVerificationRecovered",
                    missedAttempts=consecutive_write_verification_misses,
                    speed=speed_info(probe, current))
                consecutive_write_verification_misses = 0
            if baseline != desired and current != desired:
                write_and_verify(current)

        # Rebind once more so stop/final restore always follows the current field.
        current = validate_current(allow_unknown=True)
        if current == baseline:
            safely_released = True
            outcome = "restored" if writes else "baseline-unchanged"
            return {"action": "hold", "written": bool(writes), "writes": writes,
                "restored": bool(writes), "baselineRaw": baseline.hex(),
                "reason": "current TValue is already at baseline; no write needed"}
        if not writes:
            if current == desired and write_attempted:
                writes = 1
                log("holdWriteVerificationRecovered",
                    missedAttempts=consecutive_write_verification_misses,
                    phase="hold-final-check", speed=speed_info(probe, current))
            elif current == baseline:
                safely_released = True
                reason = "current TValue is already at baseline; no write needed"
                outcome = "baseline-unchanged"
                return {"action": "hold", "written": False, "writes": 0, "restored": False,
                    "reason": reason}
            else:
                retain_journal = True
                reason = "hold did not acquire the current value; journal retained"
                outcome = "journal-retained"
                return {"action": "hold", "written": False, "writes": 0, "restored": False,
                    "reason": reason}
        if read_speed(addr, "hold-final-check") != desired:
            outcome = "value-changed-before-restore"
            retain_journal = True
            return {"action": "hold", "written": True, "writes": writes, "restored": False,
                "reason": "current TValue is no longer the desired value; journal retained"}
        write_speed(addr, baseline, ident["startTicks"], desired, "normal-restore")
        if read_speed(addr, "hold-restore-verification") != baseline:
            raise RuntimeError("hold restore verification failed")
        safely_released = True
        outcome = "restored"
        return {"action": "hold", "written": True, "writes": writes, "restored": True,
            "baselineRaw": baseline.hex(), "desiredRaw": desired.hex()}
    except Exception as error:
        log("holdFailure", diagnostic=str(error), elapsedMs=round((time.monotonic() - started) * 1000, 2))
        if not write_attempted:
            outcome = "failed-before-write"
            raise
        try:
            log("recoveryAttempt", reason=str(error))
            current = validate_current(retry_unknown=False)
            if current == desired:
                write_speed(addr, baseline, ident["startTicks"], desired, "failure-recovery")
                if read_speed(addr, "hold-failure-recovery-verification") != baseline:
                    raise RuntimeError("baseline restore verification failed")
                recovery_outcome = "restored and verified"
            elif current != baseline:
                raise RuntimeError("current TValue is neither the desired value nor the baseline")
            else:
                recovery_outcome = "already at baseline; no write needed"
            safely_released = True
            outcome = "failure-recovered"
            log("recoveryResult", status=recovery_outcome)
        except Exception as recovery_error:
            outcome = "recovery-blocked"
            log("recoveryResult", status="blocked", diagnostic=str(recovery_error))
            raise RuntimeError(f"{error}; recovery blocked: {recovery_error}") from error
        raise RuntimeError(f"{error}; recovery {recovery_outcome}") from error
    finally:
        if journal_created and (safely_released or (not write_attempted and not retain_journal)):
            active_error = sys.exc_info()[1]
            try:
                target.unlink(missing_ok=True)
                log("journal", action="deleted", path=str(target))
            except Exception as cleanup_error:
                log("journal", action="retained", path=str(target), diagnostic=str(cleanup_error))
                if active_error is not None:
                    outcome = "journal-cleanup-failed"
                    active_error.args = (f"{active_error}; journal cleanup failed: {cleanup_error}",)
                else:
                    outcome = "journal-cleanup-failed"
                    raise RuntimeError(f"journal cleanup failed: {cleanup_error}") from cleanup_error
        log("runEnd", elapsedMs=round((time.monotonic() - started) * 1000, 2),
            outcome=outcome, journal="retained" if target.exists() else ("deleted" if journal_created else "not-created"),
            status="error" if sys.exc_info()[0] else "completed")


def run_action(args, transport=None, snapshot_provider=None, probe=None) -> dict:
    if args.action == "hold":
        log_path = str(getattr(args, "log_path", "") or "").strip()
        if log_path:
            args._hold_logger = HoldLogger(log_path, str(getattr(args, "run_id", "") or ""),
                str(getattr(args, "preset_name", "") or ""), str(getattr(args, "preset_id", "") or ""))
            args._hold_logger.log("runStart", {"action": "hold", "persistent": bool(getattr(args, "persistent", False)),
                "pid": args.pid, "mountId": args.mount_id, "targetSpeed": args.speed,
                "intervalSeconds": args.interval, "journalPath": args.journal})
    probe = probe or load_probe()
    run_as_package = probe.validate_run_as_package(getattr(args, "run_as_package", ""))
    root_broker_pipe = str(getattr(args, "root_broker_pipe", "") or "").strip()
    if root_broker_pipe and run_as_package:
        raise ValueError("root broker and run-as package cannot be used together")
    supplied_snapshot_provider = snapshot_provider is not None
    runtime = None
    if root_broker_pipe and args.action == "hold" and not supplied_snapshot_provider:
        runtime = probe.ProbeRuntime(root_broker_pipe=root_broker_pipe, run_as_package=run_as_package)
    broker = runtime._root_broker if runtime is not None else (probe.RootBrokerClient(root_broker_pipe) if root_broker_pipe else None)
    transport = transport or AdbTransport(args.adb, args.serial, args.pid, run_as_package, root_broker=broker)
    snapshot_provider = snapshot_provider or (lambda: capture(probe, args.adb, args.serial, args.pid,
        run_as_package, runtime=runtime, root_broker_pipe=root_broker_pipe))
    if args.pid <= 0 or (args.mount_id is not None and args.mount_id <= 0):
        raise ValueError("pid and any supplied mount ID must be positive")
    if args.action == "set" and (args.speed is None or not math.isfinite(args.speed) or args.speed <= 0):
        raise ValueError("set speed must be finite and positive")
    if args.action == "hold":
        try:
            if not supplied_snapshot_provider:
                runtime = runtime or probe.ProbeRuntime(root_broker_pipe=root_broker_pipe, run_as_package=run_as_package)
                snapshot_provider = lambda: capture(probe, args.adb, args.serial, args.pid, run_as_package, runtime, root_broker_pipe, speed_only=True, hold_rebind=True)
            stop_requested = None
            if getattr(args, "persistent", False):
                messages = queue.Queue()
                def read_control():
                    line = sys.stdin.readline()
                    messages.put(line.strip().lower() if line else "eof")
                import threading
                threading.Thread(target=read_control, daemon=True).start()
                def stop_requested(wait):
                    try:
                        message = messages.get(timeout=wait)
                        if message in {"stop", "eof"}:
                            if getattr(args, "_hold_logger", None):
                                args._hold_logger.log("stopSignal", {"source": "stdin", "message": message})
                            return True
                        raise RuntimeError("persistent hold accepts only stop or EOF on stdin")
                    except queue.Empty:
                        return False
            return run_hold(args, transport, snapshot_provider, probe, run_as_package, stop_requested)
        finally:
            if runtime is not None:
                runtime.close()
    snap = snapshot_provider()
    ident = identity(
        probe, args.adb, args.serial, args.pid, run_as_package,
        root_broker_pipe=root_broker_pipe,
    )
    player, addr = candidate_from(snap, args.mount_id)
    mount_id = player["mountId"]
    if snap.get("process") != ident:
        raise RuntimeError("snapshot process identity changed")
    old_raw = transport.read(addr, 8)
    decoded = probe.decode_tvalue(old_raw)
    if decoded.get("kind") not in {"i32", "f64"} or not math.isfinite(float(decoded.get("value", 0))) or decoded["value"] <= 0:
        raise RuntimeError("current speed TValue is invalid")
    binding = {"pid": args.pid, "startTicks": ident["startTicks"], "exe": ident["exe"],
        "playerId": player["playerId"], "mountId": mount_id, "address": addr,
    }
    if args.action == "restore":
        journal = json.loads(Path(args.journal).read_text(encoding="utf-8"))
        saved = journal.get("binding")
        if (journal.get("schema") != JOURNAL_SCHEMA or not isinstance(saved, dict)
                or any(saved.get(key) != binding[key] for key in binding)):
            raise RuntimeError("restore journal does not bind to this process/field/value")
        try:
            old_raw, new_raw = bytes.fromhex(saved["oldRaw"]), bytes.fromhex(saved["newRaw"])
        except (KeyError, TypeError, ValueError) as error:
            raise RuntimeError("restore journal raw TValue is invalid") from error
        if len(old_raw) != 8 or len(new_raw) != 8:
            raise RuntimeError("restore journal raw TValue is invalid")
        old_value, new_value = probe.decode_tvalue(old_raw), probe.decode_tvalue(new_raw)
        if (old_value.get("kind") not in {"i32", "f64"} or new_value.get("kind") != old_value.get("kind")
                or not math.isfinite(float(old_value.get("value", 0))) or old_value["value"] <= 0
                or not math.isfinite(float(new_value.get("value", 0))) or new_value["value"] <= 0):
            raise RuntimeError("restore journal raw TValue is invalid")
        fresh = snapshot_provider()
        validate_snapshot(fresh, {**ident, "playerId": player["playerId"], "address": addr}, mount_id)
        if identity(
            probe, args.adb, args.serial, args.pid, run_as_package,
            root_broker_pipe=root_broker_pipe,
        ) != ident:
            raise RuntimeError("process identity changed before restore")
        if transport.read(addr, 8) != new_raw:
            raise RuntimeError("current TValue differs from journaled written value")
        if args.write:
            transport.write(addr, old_raw, ident["startTicks"], expected_raw=new_raw)
            if transport.read(addr, 8) != old_raw:
                raise RuntimeError("restore write verification failed")
            Path(args.journal).unlink(missing_ok=True)
        return {"action": "restore", "written": bool(args.write), "raw": old_raw.hex()}
    new_raw = encode_speed(args.speed, old_raw, probe)
    bound = {**binding, "oldRaw": old_raw.hex(), "newRaw": new_raw.hex()}
    if args.write:
        fresh = snapshot_provider()
        validate_snapshot(fresh, {**ident, "playerId": player["playerId"], "address": addr}, mount_id)
        if identity(
            probe, args.adb, args.serial, args.pid, run_as_package,
            root_broker_pipe=root_broker_pipe,
        ) != ident:
            raise RuntimeError("process identity changed before set")
        if transport.read(addr, 8) != old_raw:
            raise RuntimeError("current TValue changed before write")
        journal = {"schema": JOURNAL_SCHEMA, "binding": bound}
        target = Path(args.journal)
        target.parent.mkdir(parents=True, exist_ok=True)
        fd, temporary = tempfile.mkstemp(dir=target.parent, prefix=target.name + ".")
        try:
            with os.fdopen(fd, "w", encoding="utf-8") as stream:
                json.dump(journal, stream, separators=(",", ":"))
                stream.flush()
                os.fsync(stream.fileno())
            try:
                os.link(temporary, target)
            except FileExistsError as error:
                raise RuntimeError("restore journal already exists; choose another --journal") from error
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)
        transport.write(addr, new_raw, ident["startTicks"], expected_raw=old_raw)
        if transport.read(addr, 8) != new_raw:
            raise RuntimeError("set write verification failed")
    return {"action": "set", "written": bool(args.write), "oldRaw": old_raw.hex(), "newRaw": new_raw.hex()}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("set", "restore", "hold"))
    parser.add_argument("--adb", required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--run-as-package", default="", help="use run-as for a debuggable Android app instead of su")
    parser.add_argument("--root-broker-pipe", default="", help="execute rooted ADB commands through the desktop root broker pipe")
    parser.add_argument("--mount-id", type=int, help="optional expected mount ID; defaults to the unique mounted local player")
    parser.add_argument("--preflight-player-id", type=int)
    parser.add_argument("--preflight-start-ticks", type=int)
    parser.add_argument("--preflight-exe")
    parser.add_argument("--preflight-speed-address", type=lambda value: int(value, 0))
    parser.add_argument("--speed", type=float, help="required for set; restore uses journaled bytes")
    parser.add_argument("--duration", type=float, default=60.0, help="hold duration in seconds (max 120)")
    parser.add_argument("--persistent", action="store_true", help="hold until stdin receives stop or EOF")
    parser.add_argument("--interval", type=float, default=1.0, help="hold recheck interval in seconds")
    parser.add_argument("--journal", default="mount_speed_memory_test.json")
    parser.add_argument("--log-path", default="", help="optional append-only JSONL speed-hold log")
    parser.add_argument("--run-id", default="")
    parser.add_argument("--preset-name", default="")
    parser.add_argument("--preset-id", default="")
    parser.add_argument("--write", action="store_true", help="required to mutate process memory")
    args = parser.parse_args()
    try:
        if args.action in {"set", "hold"} and args.speed is None:
            parser.error("--speed is required for set/hold")
        print(json.dumps(run_action(args), separators=(",", ":")))
        return 0
    except Exception as error:
        logger = getattr(args, "_hold_logger", None)
        if logger:
            logger.log("runFailure", {"diagnostic": str(error)})
        print(json.dumps({"error": str(error)}), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
