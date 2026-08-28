"""Read-only LuaJIT GC64 mount-status probe for the Android game process.

This is a host-side diagnostic reader.  It uses rooted ADB to read bounded
anonymous writable mappings, resolves the six known MapRole property names,
and returns one JSON snapshot.  It never writes /proc/<pid>/mem, injects
code, pauses the process, invokes game methods, or sends packets.

The probe deliberately discovers LuaJIT table nodes at runtime.  The target
client does not expose a stable native C/C++ structure for these fields, so a
fixed native offset is not a valid production layout for this process.
"""

from __future__ import annotations

import argparse
import base64
import contextlib
import io
import json
import math
import os
import re
import struct
import subprocess
import sys
import threading
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable


PAGE_SIZE = 4096
GC64_MASK = (1 << 47) - 1
GCSTR_SIZE = 24
GCSTR_GCT = 4
GCT_TAB = 11
LJ_TISNUM = 0xFFFFFFF2
LJ_TTRUE = 0xFFFFFFFD
LJ_TFALSE = 0xFFFFFFFE
LJ_TNIL = 0xFFFFFFFF
LJ_TTAB = 0xFFFFFFF4
LJ_TSTR = 0xFFFFFFFB

FIELD_NAMES = (
    "m_PlayerId",
    "m_IsLocalPlayer",
    "m_RideId",
    "m_RoleMoveSpeed",
    "m_Gx",
    "m_Gy",
)
OPTIONAL_RIDE_SKILL_FIELD_NAMES = (
    "m_Rideskills",
    "skillId",
    "exp",
)
OPTIONAL_RIDE_GROWTH_FIELD_NAMES = (
    "GROWUP",
)
OPTIONAL_RIDE_REFINE_FIELD_NAMES = (
    "m_RideIns",
    "m_ResetData",
)
RIDE_BINDING_FIELD_NAMES = (
    "PropertyValueDict",
)
RIDE_BINDING_PROPERTY_NAMES = (
    "RIDEING",
    "SHAPE",
    "GROWUP",
    "RANDOMGROWUP",
)
DISCOVERED_FIELD_NAMES = tuple(dict.fromkeys(
    FIELD_NAMES +
    OPTIONAL_RIDE_SKILL_FIELD_NAMES +
    OPTIONAL_RIDE_GROWTH_FIELD_NAMES +
    OPTIONAL_RIDE_REFINE_FIELD_NAMES +
    RIDE_BINDING_FIELD_NAMES
))
RIDE_SKILL_NAME_MAP_PATH = Path(__file__).with_name("ride_skill_name_map.json")
PROBE_CACHE_SCHEMA = "mount_status_probe_cache.v1"
PROBE_CACHE_VERSION = 5
ROOT_BROKER_BATCH_TIMEOUT_SECONDS = 30.0
ROOT_BROKER_MAX_RAW_BATCH_BYTES = 32 * 1024 * 1024
ROOT_BROKER_CONNECT_RETRY_SECONDS = 2.0
ROOT_BROKER_CONNECT_RETRY_DELAY_SECONDS = 0.01
ROOT_BROKER_PIPE_BUFFER_BYTES = 1024 * 1024
WARM_HINT_SCHEMA = "mount_status_probe_warm_hints.v1"
WARM_HINT_VERSION = 1
WARM_HINT_PRIORITY_RANGE_RADIUS = 8
WARM_HINT_PRIORITY_SMALL_RANGE_BYTES = 4 * 1024 * 1024
COLD_PRIORITY_MAX_RANGES = 16
COLD_PRIORITY_MAX_TOTAL_RATIO = 0.25

NODE_STRIDE = 24
GCTAB_SIZE = 64
MAX_HASH_MASK = 0x1000
MAX_TABLE_ARRAY_SIZE = 4096
MAX_RIDE_INSTANCES = 64
MAX_RIDE_SKILLS_PER_INSTANCE = 64
MAX_RIDE_REFINE_CARDS = 64
MAX_RIDE_REFINE_SKILLS_PER_CARD = 64
EXPECTED_RIDE_REFINE_CARD_COUNT = 21
MAX_WARM_HINT_REFERENCES = 512
MAX_WARM_HINT_PAGES = 256

MAP_RE = re.compile(
    r"^([0-9a-f]+)-([0-9a-f]+)\s+([rwxps-]{4})\s+([0-9a-f]+)\s+\S+\s+\d+\s*(.*)$",
    re.IGNORECASE,
)


def load_ride_skill_name_map() -> dict[int, str]:
    """Load the optional static ride-skill ID/name map fail-closed."""
    try:
        with RIDE_SKILL_NAME_MAP_PATH.open("r", encoding="utf-8") as stream:
            payload = json.load(stream)
    except (OSError, ValueError, TypeError):
        return {}

    if not isinstance(payload, dict) or payload.get("schema") != "mount_ride_skill_name_map.v1":
        return {}
    skills = payload.get("skills")
    if not isinstance(skills, dict):
        return {}

    result: dict[int, str] = {}
    for raw_id, raw_name in skills.items():
        try:
            skill_id = int(raw_id)
        except (TypeError, ValueError):
            continue
        if skill_id <= 0 or not isinstance(raw_name, str) or not raw_name.strip():
            continue
        result[skill_id] = raw_name.strip()
    return result


def run_adb(adb_path: str, serial: str, args: list[str], timeout: float) -> subprocess.CompletedProcess[bytes]:
    command = [adb_path]
    if serial:
        command.extend(["-s", serial])
    command.extend(args)
    return subprocess.run(command, capture_output=True, timeout=timeout)


def parse_maps(raw: bytes) -> list[tuple[int, int]]:
    ranges: list[tuple[int, int]] = []
    for raw_line in raw.decode("utf-8", "replace").splitlines():
        match = MAP_RE.match(raw_line.strip())
        if not match:
            continue
        start, end = int(match.group(1), 16), int(match.group(2), 16)
        permissions, path = match.group(3), match.group(5).strip()
        if (
            "r" in permissions
            and "w" in permissions
            and "p" in permissions
            and not path
            and end > start
        ):
            ranges.append((start, end))
    return ranges


def read_maps(
    adb_path: str,
    serial: str,
    pid: int,
    timeout: float,
    session: "AdbPageMemoryReadSession | None" = None,
) -> list[tuple[int, int]]:
    if session is not None:
        if session.closed:
            return []
        raw = session.read_text(f"cat /proc/{pid}/maps", timeout)
        return parse_maps(raw) if raw is not None else []

    command = f"su -c 'cat /proc/{pid}/maps'"
    result = run_adb(adb_path, serial, ["shell", command], timeout)
    if result.returncode != 0:
        return []
    return parse_maps(result.stdout)


def read_chunks(
    adb_path: str,
    serial: str,
    pid: int,
    chunks: list[tuple[int, int]],
    timeout: float,
    batch_size: int = 32,
    session: "AdbPageMemoryReadSession | None" = None,
) -> list[bytes]:
    """Read page-aligned chunks without changing the target process."""
    if not chunks:
        return []
    if any(
        start < 0
        or size <= 0
        or start % PAGE_SIZE
        or size % PAGE_SIZE
        for start, size in chunks
    ):
        return [b""] * len(chunks)

    if session is not None and not session.closed:
        try:
            session_blobs = session.read_chunks(chunks, timeout, batch_size=batch_size)
            if (
                session_blobs is not None
                and len(session_blobs) == len(chunks)
                and all(len(blob) == size for blob, (_, size) in zip(session_blobs, chunks))
            ):
                return session_blobs
        except (OSError, RuntimeError, subprocess.SubprocessError):
            session.close()

        # A broker-backed session is the process-wide Root boundary. Never
        # fall back to a new per-request `adb exec-out su` process after it
        # fails, otherwise the permission prompt can return and the preset
        # would no longer use the shared session contract.
        if session.uses_root_broker:
            return [b""] * len(chunks)

    if session is not None and session.uses_root_broker:
        return [b""] * len(chunks)

    result_blobs: list[bytes] = []
    for offset in range(0, len(chunks), batch_size):
        batch = chunks[offset : offset + batch_size]
        commands = [
            (
                f"toybox dd if=/proc/{pid}/mem bs=4096 skip={start // PAGE_SIZE} "
                f"count={size // PAGE_SIZE} status=none 2>/dev/null"
            )
            for start, size in batch
        ]
        result = run_adb(
            adb_path,
            serial,
            ["exec-out", "su", "-c", ";".join(commands)],
            max(30.0, timeout),
        )
        if result.returncode != 0:
            result_blobs.extend([b""] * len(batch))
            continue

        expected = sum(size for _, size in batch)
        raw = result.stdout
        if len(raw) != expected:
            normalized = raw.replace(b"\r\n", b"\n")
            raw = normalized if len(normalized) == expected else b""
        if len(raw) != expected:
            result_blobs.extend([b""] * len(batch))
            continue

        cursor = 0
        for _, size in batch:
            result_blobs.append(raw[cursor : cursor + size])
            cursor += size
    return result_blobs


def chunk_ranges(
    ranges: Iterable[tuple[int, int]],
    chunk_size: int,
) -> list[tuple[int, int]]:
    if chunk_size <= 0 or chunk_size % PAGE_SIZE:
        raise ValueError("chunk size must be a positive page-aligned value")
    chunks: list[tuple[int, int]] = []
    for start, end in ranges:
        for chunk_start in range(start, end, chunk_size):
            chunk_end = min(end, chunk_start + chunk_size)
            size = chunk_end - chunk_start
            if size and size % PAGE_SIZE == 0:
                chunks.append((chunk_start, size))
    return chunks


def gc64_tagged_pointer(address: int, lua_tag: int) -> int:
    return ((lua_tag << 47) & 0xFFFFFFFFFFFFFFFF) | (address & GC64_MASK)


def verify_gcstr(blob: bytes, data_offset: int, expected: bytes) -> bool:
    object_offset = data_offset - GCSTR_SIZE
    if object_offset < 0 or data_offset + len(expected) > len(blob):
        return False
    if blob[data_offset : data_offset + len(expected)] != expected:
        return False
    if blob[object_offset + 9] != GCSTR_GCT:
        return False
    return struct.unpack_from("<I", blob, object_offset + 20)[0] == len(expected)


def find_all(blob: bytes, needle: bytes, limit: int = 256) -> list[int]:
    positions: list[int] = []
    cursor = 0
    while len(positions) < limit:
        position = blob.find(needle, cursor)
        if position < 0:
            break
        positions.append(position)
        cursor = position + 1
    return positions


def decode_tvalue(raw: bytes) -> dict:
    if len(raw) != 8:
        return {"kind": "invalid", "raw": ""}

    value_u64 = struct.unpack("<Q", raw)[0]
    value_i64 = struct.unpack("<q", raw)[0]
    itype = (value_i64 >> 47) & 0xFFFFFFFF
    result = {"kind": "tagged", "raw": hex(value_u64), "tag": hex(itype)}

    if itype == LJ_TISNUM:
        return {
            "kind": "i32",
            "value": struct.unpack("<i", raw[:4])[0],
            "raw": hex(value_u64),
        }
    if itype == LJ_TTRUE:
        return {"kind": "boolean", "value": True, "raw": hex(value_u64)}
    if itype == LJ_TFALSE:
        return {"kind": "boolean", "value": False, "raw": hex(value_u64)}
    if itype == LJ_TNIL:
        return {"kind": "nil", "value": None, "raw": hex(value_u64)}
    if (value_u64 >> 51) != 0x1FFF:
        number = struct.unpack("<d", raw)[0]
        if math.isfinite(number):
            return {"kind": "f64", "value": number, "raw": hex(value_u64)}
    return result


def tagged_pointer(raw: bytes, expected_tag: int) -> int | None:
    if len(raw) != 8:
        return None
    value_u64 = struct.unpack("<Q", raw)[0]
    value_i64 = struct.unpack("<q", raw)[0]
    itype = (value_i64 >> 47) & 0xFFFFFFFF
    if itype != expected_tag:
        return None
    return value_u64 & GC64_MASK


def tagged_pointer_from_value(value: dict | None, expected_tag: int) -> int | None:
    if not isinstance(value, dict) or value.get("kind") != "tagged":
        return None
    try:
        raw = struct.pack("<Q", int(value["raw"], 0))
    except (KeyError, TypeError, ValueError, struct.error):
        return None
    return tagged_pointer(raw, expected_tag)


def address_in_ranges(address: int, size: int, ranges: list[tuple[int, int]]) -> bool:
    return size > 0 and any(start <= address and address + size <= end for start, end in ranges)


class RootBrokerClient:
    """Execute Root commands through the desktop process-wide session."""

    def __init__(self, pipe_path: str) -> None:
        normalized = str(pipe_path or "").strip()
        if not normalized:
            raise ValueError("root broker pipe is empty")
        self.pipe_path = normalized
        self._pipe = None
        self._sync = threading.Lock()

    def _close_pipe(self) -> None:
        pipe = self._pipe
        self._pipe = None
        if pipe is not None:
            try:
                pipe.close()
            except OSError:
                pass

    def close(self) -> None:
        with self._sync:
            self._close_pipe()

    def _connect(self, timeout: float):
        if self._pipe is not None:
            return self._pipe
        connect_deadline = time.monotonic() + min(
            ROOT_BROKER_CONNECT_RETRY_SECONDS,
            max(0.1, float(timeout)),
        )
        while self._pipe is None:
            try:
                raw_pipe = open(self.pipe_path, "r+b", buffering=0)
                try:
                    self._pipe = io.BufferedRWPair(
                        raw_pipe,
                        raw_pipe,
                        ROOT_BROKER_PIPE_BUFFER_BYTES,
                    )
                except Exception:
                    raw_pipe.close()
                    raise
            except OSError as error:
                if time.monotonic() >= connect_deadline:
                    raise RuntimeError("Root broker pipe failed") from error
                time.sleep(ROOT_BROKER_CONNECT_RETRY_DELAY_SECONDS)
        return self._pipe

    def execute(self, command: str, timeout: float) -> bytes:
        request = json.dumps(
            {
                "command": command,
                "timeoutMs": max(1000, int(max(1.0, float(timeout)) * 1000)),
            },
            ensure_ascii=False,
            separators=(",", ":"),
        ).encode("utf-8") + b"\n"
        with self._sync:
            pipe = self._connect(timeout)
            try:
                pipe.write(request)
                pipe.flush()
                response_line = pipe.readline()
            except OSError as error:
                self._close_pipe()
                raise RuntimeError("Root broker pipe failed") from error
        if not response_line:
            self.close()
            raise RuntimeError("Root broker closed without a response")
        try:
            response = json.loads(response_line.decode("utf-8", "replace"))
        except (TypeError, ValueError) as error:
            raise RuntimeError("Root broker returned invalid JSON") from error
        if not isinstance(response, dict) or not bool(response.get("ok")):
            detail = str(response.get("error") or "Root broker command failed") if isinstance(response, dict) else "Root broker command failed"
            raise RuntimeError(detail)
        return str(response.get("output") or "").encode("utf-8")


class AdbPageMemoryReadSession:
    """Keep one rooted ADB shell for many small read-only page requests."""

    READY_MARKER = "WPE_MOUNT_READ_READY"

    def __init__(
        self,
        adb_path: str,
        serial: str,
        pid: int,
        root_broker: RootBrokerClient | None = None,
    ) -> None:
        self.root_broker = root_broker
        self._closed = False
        self._timing: dict[str, float] | None = None
        self.pid = pid
        if self.root_broker is not None:
            self.process = None
            return

        command = [adb_path]
        if serial:
            command.extend(["-s", serial])
        command.extend(["shell", "-t", "su", "-c", "sh"])
        self.process = subprocess.Popen(
            command,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
        )
        self._write_command(f"stty -echo; echo {self.READY_MARKER}\n")
        ready = self._read_lines(1, 10.0)
        if not ready or ready[0].decode("utf-8", "replace").strip() != self.READY_MARKER:
            self.close()
            raise RuntimeError("ADB read shell did not become ready")
        self.pid = pid

    @property
    def closed(self) -> bool:
        return self._closed or (
            self.process is not None and self.process.poll() is not None
        )

    @property
    def uses_root_broker(self) -> bool:
        return self.root_broker is not None

    def set_timing(self, timing: dict[str, float] | None) -> None:
        """Attach the current request's bounded transport timing accumulator."""
        self._timing = timing

    def _record_root_adb(self, started: float) -> None:
        timing = getattr(self, "_timing", None)
        if timing is not None:
            timing["rootAdbMs"] = timing.get("rootAdbMs", 0.0) + (
                time.perf_counter() - started
            ) * 1000.0

    def _write_command(self, command: str) -> None:
        if self.root_broker is not None:
            raise RuntimeError("Root broker sessions do not expose shell stdin")
        if self.closed or self.process.stdin is None:
            raise RuntimeError("ADB read shell is closed")
        try:
            self.process.stdin.write(command.encode("utf-8"))
            self.process.stdin.flush()
        except (BrokenPipeError, OSError) as error:
            self.close()
            raise RuntimeError("ADB read shell write failed") from error

    def _read_lines(self, count: int, timeout: float) -> list[bytes] | None:
        if self.closed or self.process.stdout is None:
            return None
        result: list[bytes] = []
        failure: list[BaseException] = []

        def read_all() -> None:
            try:
                result.extend(self.process.stdout.readline() for _ in range(count))
            except BaseException as error:
                failure.append(error)

        reader_thread = threading.Thread(target=read_all, daemon=True)
        reader_thread.start()
        reader_thread.join(max(0.1, timeout))
        if reader_thread.is_alive() or failure or len(result) != count:
            self.close()
            return None
        return result

    @staticmethod
    def _decode_line(line: bytes) -> bytes:
        encoded = line.strip()
        if not encoded:
            return b""
        try:
            return base64.b64decode(encoded, validate=True)
        except (ValueError, TypeError):
            return b""

    def read_text(self, command: str, timeout: float) -> bytes | None:
        """Read command output through this already-rooted shell."""
        started = time.perf_counter()
        if self.root_broker is not None:
            try:
                result = self.root_broker.execute(command, timeout)
            except RuntimeError:
                self._closed = True
                self._record_root_adb(started)
                return None
            self._record_root_adb(started)
            return result
        self._write_command(f"{command} | base64 -w 0; echo\n")
        lines = self._read_lines(1, max(30.0, timeout))
        if lines is None:
            self._record_root_adb(started)
            return None
        result = self._decode_line(lines[0])
        self._record_root_adb(started)
        return result

    def read_chunks(
        self,
        chunks: list[tuple[int, int]],
        timeout: float,
        batch_size: int = 32,
    ) -> list[bytes] | None:
        """Read larger spans without starting another `su` process."""
        if not chunks:
            return []
        if any(
            start < 0
            or size <= 0
            or start % PAGE_SIZE
            or size % PAGE_SIZE
            for start, size in chunks
        ):
            return [b""] * len(chunks)

        if self.root_broker is not None:
            result_blobs: list[bytes] = []
            effective_batch_size = max(1, batch_size)
            batches: list[list[tuple[int, int]]] = []
            batch: list[tuple[int, int]] = []
            batch_bytes = 0
            for chunk in chunks:
                _, size = chunk
                if batch and (
                    len(batch) >= effective_batch_size
                    or batch_bytes + size > ROOT_BROKER_MAX_RAW_BATCH_BYTES
                ):
                    batches.append(batch)
                    batch = []
                    batch_bytes = 0
                batch.append(chunk)
                batch_bytes += size
            if batch:
                batches.append(batch)

            for batch in batches:
                commands = [
                    (
                        f"toybox dd if=/proc/{self.pid}/mem bs=4096 "
                        f"skip={start // PAGE_SIZE} count={size // PAGE_SIZE} "
                        "status=none 2>/dev/null | base64 -w 0; echo"
                    )
                    for start, size in batch
                ]
                started = time.perf_counter()
                try:
                    raw = self.root_broker.execute(
                        "\n".join(commands),
                        max(ROOT_BROKER_BATCH_TIMEOUT_SECONDS, timeout),
                    )
                except RuntimeError:
                    self._closed = True
                    self._record_root_adb(started)
                    return None
                self._record_root_adb(started)
                lines = raw.splitlines()
                if len(lines) != len(batch):
                    return None
                result_blobs.extend(self._decode_line(line) for line in lines)
            return result_blobs

        result_blobs: list[bytes] = []
        for offset in range(0, len(chunks), max(1, batch_size)):
            batch = chunks[offset : offset + max(1, batch_size)]
            for start, size in batch:
                self._write_command(
                    f"toybox dd if=/proc/{self.pid}/mem bs=4096 "
                    f"skip={start // PAGE_SIZE} count={size // PAGE_SIZE} "
                    "status=none 2>/dev/null | base64 -w 0; echo\n"
                )
            started = time.perf_counter()
            lines = self._read_lines(len(batch), max(30.0, timeout))
            if lines is None:
                self._record_root_adb(started)
                return None
            self._record_root_adb(started)
            result_blobs.extend(self._decode_line(line) for line in lines)
        return result_blobs

    def read_pages(self, pages: list[int], timeout: float) -> list[bytes] | None:
        if not pages:
            return []
        if self.root_broker is not None:
            result_blobs: list[bytes] = []
            for offset in range(0, len(pages), 32):
                batch = pages[offset : offset + 32]
                commands = [
                    (
                        f"toybox dd if=/proc/{self.pid}/mem bs=4096 "
                        f"skip={page // PAGE_SIZE} count=1 status=none 2>/dev/null "
                        "| base64 -w 0; echo"
                    )
                    for page in batch
                ]
                started = time.perf_counter()
                try:
                    raw = self.root_broker.execute(
                        "\n".join(commands),
                        max(ROOT_BROKER_BATCH_TIMEOUT_SECONDS, timeout),
                    )
                except RuntimeError:
                    self._closed = True
                    self._record_root_adb(started)
                    return None
                self._record_root_adb(started)
                lines = raw.splitlines()
                if len(lines) != len(batch):
                    return None
                result_blobs.extend(self._decode_line(line) for line in lines)
            return result_blobs
        for page in pages:
            self._write_command(
                f"toybox dd if=/proc/{self.pid}/mem bs=4096 "
                f"skip={page // PAGE_SIZE} count=1 status=none 2>/dev/null "
                "| base64 -w 0; echo\n"
            )
        started = time.perf_counter()
        lines = self._read_lines(len(pages), max(30.0, timeout))
        if lines is None:
            self._record_root_adb(started)
            return None
        self._record_root_adb(started)

        blobs: list[bytes] = []
        for line in lines:
            blobs.append(self._decode_line(line))
        return blobs

    def close(self) -> None:
        if self._closed:
            return
        self._closed = True
        if self.root_broker is not None:
            self.root_broker.close()
            return
        try:
            if self.process.stdin is not None:
                self.process.stdin.close()
        except OSError:
            pass
        try:
            if self.process.stdout is not None:
                self.process.stdout.close()
        except OSError:
            pass
        try:
            if self.process.poll() is None:
                self.process.kill()
        except OSError:
            pass
        try:
            self.process.wait(timeout=1.0)
        except (OSError, subprocess.TimeoutExpired):
            pass


class ProbeRuntime:
    """Own one rooted ADB shell for repeated probe requests."""

    def __init__(self, root_broker_pipe: str = "") -> None:
        self._root_broker = (
            RootBrokerClient(root_broker_pipe)
            if str(root_broker_pipe or "").strip()
            else None
        )
        self._session: AdbPageMemoryReadSession | None = None
        self._session_key: tuple[str, str, int] | None = None
        self._point_identity: tuple[int, int, str] | None = None
        self._point_plan: dict | None = None
        self._request_sequence = 0
        self._request_started = 0.0
        self._timing: dict[str, float] = {}

    @staticmethod
    def _identity_key(identity: dict | None) -> tuple[int, int, str] | None:
        if not isinstance(identity, dict):
            return None
        pid = identity.get("pid")
        start_ticks = identity.get("startTicks")
        executable = identity.get("exe")
        if (
            not isinstance(pid, int)
            or pid <= 0
            or not isinstance(start_ticks, int)
            or start_ticks <= 0
            or not isinstance(executable, str)
            or not executable.strip()
        ):
            return None
        return pid, start_ticks, executable.strip()

    def begin_request(self) -> int:
        """Start one bounded request and increment its in-process sequence."""
        self._request_sequence += 1
        self._request_started = time.perf_counter()
        self._timing = {
            "rootAdbMs": 0.0,
            "probeMs": 0.0,
            "cardParseMs": 0.0,
        }
        if self._session is not None:
            self._session.set_timing(self._timing)
        return self._request_sequence

    def add_card_parse_time(self, started: float) -> None:
        self._timing["cardParseMs"] = self._timing.get("cardParseMs", 0.0) + (
            time.perf_counter() - started
        ) * 1000.0

    def timing_snapshot(self) -> dict[str, float]:
        timing = dict(self._timing)
        if self._request_started:
            timing["probeMs"] = max(
                timing.get("probeMs", 0.0),
                (time.perf_counter() - self._request_started) * 1000.0,
            )
        return {
            key: round(max(0.0, float(value)), 3)
            for key, value in timing.items()
        }

    @property
    def request_sequence(self) -> int:
        return self._request_sequence

    @property
    def timing(self) -> dict[str, float]:
        return self._timing

    def set_point_plan(
        self,
        identity: dict,
        cache_payload: dict,
        point_hint: dict,
    ) -> None:
        """Keep absolute point addresses only for this verified process identity.

        This plan intentionally never reaches the disk cache or the cross-process
        warm sidecar. A new PID/startTicks must use the relative-hint/discovery
        path and prove every live field again before a point plan is created.
        """
        identity_key = self._identity_key(identity)
        if identity_key is None or not isinstance(cache_payload, dict) or not isinstance(point_hint, dict):
            self.clear_point_plan()
            return
        self._point_identity = identity_key
        self._point_plan = {
            "identity": dict(identity),
            "cache": cache_payload,
            "point": dict(point_hint),
        }

    def get_point_plan(self, identity: dict) -> dict | None:
        identity_key = self._identity_key(identity)
        if identity_key is None or identity_key != self._point_identity:
            return None
        return self._point_plan

    def clear_point_plan(self) -> None:
        self._point_identity = None
        self._point_plan = None

    def get_session(
        self,
        adb_path: str,
        serial: str,
        pid: int,
    ) -> AdbPageMemoryReadSession:
        key = (os.path.abspath(adb_path), serial, pid)
        if (
            self._session is None
            or self._session.closed
            or self._session_key != key
        ):
            self.close()
            self._session = AdbPageMemoryReadSession(
                adb_path,
                serial,
                pid,
                root_broker=self._root_broker,
            )
            self._session_key = key
        self._session.set_timing(self._timing)
        return self._session

    def close(self) -> None:
        if self._session is not None:
            self._session.close()
            self._session = None
        self._session_key = None
        self.clear_point_plan()
        self._timing = {}
        self._request_started = 0.0


class BoundedPageMemoryReader:
    """Small cached reader for validated, page-aligned process-memory spans."""

    def __init__(
        self,
        adb_path: str,
        serial: str,
        pid: int,
        ranges: list[tuple[int, int]],
        timeout: float,
        shared_session: AdbPageMemoryReadSession | None = None,
    ) -> None:
        self.adb_path = adb_path
        self.serial = serial
        self.pid = pid
        self.ranges = ranges
        self.timeout = timeout
        self.pages: dict[int, bytes] = {}
        self.accessed_pages: set[int] = set()
        self._adb_session = shared_session
        self._owns_adb_session = shared_session is None
        self._root_broker_mode = bool(
            shared_session is not None and shared_session.uses_root_broker
        )
        self._adb_session_failed = False

    def close(self) -> None:
        if self._owns_adb_session and self._adb_session is not None:
            self._adb_session.close()
        self._adb_session = None

    def __del__(self) -> None:
        try:
            self.close()
        except Exception:
            pass

    def prefetch(self, spans: Iterable[tuple[int, int]]) -> bool:
        """Read missing pages for a set of validated spans in small batches."""
        pages: set[int] = set()
        for address, size in spans:
            if size <= 0 or not address_in_ranges(address, size, self.ranges):
                return False
            first = address & ~(PAGE_SIZE - 1)
            last = (address + size - 1) & ~(PAGE_SIZE - 1)
            pages.update(range(first, last + PAGE_SIZE, PAGE_SIZE))

        missing = sorted(page for page in pages if page not in self.pages)
        if not missing:
            return True
        blobs: list[bytes] | None = None
        if not self._adb_session_failed:
            try:
                if self._adb_session is None:
                    if not self._owns_adb_session:
                        self._adb_session_failed = True
                    else:
                        self._adb_session = AdbPageMemoryReadSession(
                            self.adb_path,
                            self.serial,
                            self.pid,
                        )
                if self._adb_session is not None and not self._adb_session.closed:
                    blobs = self._adb_session.read_pages(missing, self.timeout)
            except (OSError, RuntimeError, subprocess.SubprocessError):
                if self._owns_adb_session:
                    self.close()
                else:
                    self._adb_session = None
                self._adb_session_failed = True
        if blobs is None:
            if self._root_broker_mode:
                return False
            blobs = read_chunks(
                self.adb_path,
                self.serial,
                self.pid,
                [(page, PAGE_SIZE) for page in missing],
                self.timeout,
                batch_size=16,
                session=(
                    self._adb_session
                    if self._adb_session is not None and not self._adb_session.closed
                    else None
                ),
            )
        if len(blobs) != len(missing) or any(len(blob) != PAGE_SIZE for blob in blobs):
            return False
        self.pages.update({page: blob for page, blob in zip(missing, blobs)})
        return True

    def read(self, address: int, size: int) -> bytes:
        if size <= 0 or not address_in_ranges(address, size, self.ranges):
            return b""

        first = address & ~(PAGE_SIZE - 1)
        last = (address + size - 1) & ~(PAGE_SIZE - 1)
        page_addresses = list(range(first, last + PAGE_SIZE, PAGE_SIZE))
        missing = [page for page in page_addresses if page not in self.pages]
        if missing:
            if not self.prefetch([(address, size)]):
                return b""

        joined = b"".join(self.pages.get(page, b"") for page in page_addresses)
        expected = len(page_addresses) * PAGE_SIZE
        if len(joined) != expected:
            return b""
        self.accessed_pages.update(page_addresses)
        offset = address - first
        return joined[offset : offset + size]


def read_gcstr(
    reader: BoundedPageMemoryReader,
    address: int,
) -> str | None:
    if not address_in_ranges(address, GCSTR_SIZE, reader.ranges):
        return None
    header = reader.read(address, GCSTR_SIZE)
    if len(header) != GCSTR_SIZE or header[9] != GCSTR_GCT:
        return None
    length = struct.unpack_from("<I", header, 20)[0]
    if length > 4096 or not address_in_ranges(address + GCSTR_SIZE, length, reader.ranges):
        return None
    value = reader.read(address + GCSTR_SIZE, length)
    return value.decode("utf-8", "replace") if len(value) == length else None


def read_gc_table(
    reader: BoundedPageMemoryReader,
    address: int,
) -> dict | None:
    if not address_in_ranges(address, GCTAB_SIZE, reader.ranges):
        return None
    header = reader.read(address, GCTAB_SIZE)
    if len(header) != GCTAB_SIZE or header[9] != GCT_TAB:
        return None

    array_address = struct.unpack_from("<Q", header, 16)[0]
    node_address = struct.unpack_from("<Q", header, 40)[0]
    array_size = struct.unpack_from("<I", header, 48)[0]
    hash_mask = struct.unpack_from("<I", header, 52)[0]
    if node_address == 0 or hash_mask > MAX_HASH_MASK or array_size > MAX_TABLE_ARRAY_SIZE:
        return None
    if not address_in_ranges(node_address, (hash_mask + 1) * NODE_STRIDE, reader.ranges):
        return None
    if array_size > 0 and (
        array_address == 0
        or not address_in_ranges(array_address, array_size * 8, reader.ranges)
    ):
        return None
    return {
        "address": address,
        "array": array_address,
        "node": node_address,
        "arraySize": array_size,
        "hashMask": hash_mask,
    }


def read_gc_table_key(
    reader: BoundedPageMemoryReader,
    raw: bytes,
) -> str | int | float | None:
    value = decode_tvalue(raw)
    if value.get("kind") in {"i32", "f64"}:
        return value.get("value")
    key_address = tagged_pointer_from_value(value, LJ_TSTR)
    return read_gcstr(reader, key_address) if key_address is not None else None


def read_gc_table_nodes(
    reader: BoundedPageMemoryReader,
    table: dict,
) -> list[dict] | None:
    nodes: list[dict] = []
    seen: set[int] = set()
    for bucket in range(int(table["hashMask"]) + 1):
        node_address = int(table["node"]) + bucket * NODE_STRIDE
        hops = 0
        while node_address and node_address not in seen and hops <= int(table["hashMask"]) + 1:
            blob = reader.read(node_address, NODE_STRIDE)
            if len(blob) != NODE_STRIDE:
                return None
            seen.add(node_address)
            value_raw, key_raw, next_raw = blob[:8], blob[8:16], blob[16:24]
            key = read_gc_table_key(reader, key_raw)
            next_address = struct.unpack("<Q", next_raw)[0]
            if key is not None:
                nodes.append({
                    "key": key,
                    "value": decode_tvalue(value_raw),
                    "nodeAddress": node_address,
                })
            if next_address and not address_in_ranges(next_address, NODE_STRIDE, reader.ranges):
                return None
            node_address = next_address
            hops += 1
    return nodes


def read_gc_table_entries(
    reader: BoundedPageMemoryReader,
    table: dict,
) -> list[dict] | None:
    nodes = read_gc_table_nodes(reader, table)
    if nodes is None:
        return None
    return [entry for entry in nodes if isinstance(entry.get("key"), str)]


def read_gc_table_array(
    reader: BoundedPageMemoryReader,
    table: dict,
) -> list[dict] | None:
    array_size = int(table["arraySize"])
    if array_size == 0:
        return []
    blob = reader.read(int(table["array"]), array_size * 8)
    if len(blob) != array_size * 8:
        return None
    return [decode_tvalue(blob[index * 8 : index * 8 + 8]) for index in range(array_size)]


def iter_local_luajit_tables(
    reader: BoundedPageMemoryReader,
    center_address: int,
    radius: int = 0x10000,
) -> Iterable[tuple[int, dict]]:
    """Find validated table headers near one discovered table/node."""
    for range_start, range_end in reader.ranges:
        if not range_start <= center_address < range_end:
            continue
        window_start = max(
            range_start,
            (center_address - radius) & ~(PAGE_SIZE - 1),
        )
        window_end = min(
            range_end,
            (center_address + radius + GCTAB_SIZE + PAGE_SIZE - 1) & ~(PAGE_SIZE - 1),
        )
        blob = reader.read(window_start, window_end - window_start)
        if len(blob) != window_end - window_start:
            return

        seen: set[int] = set()
        for offset in range(0, len(blob) - GCTAB_SIZE + 1, 8):
            header = blob[offset : offset + GCTAB_SIZE]
            if len(header) != GCTAB_SIZE or header[9] != GCT_TAB:
                continue
            table_address = window_start + offset
            if table_address in seen:
                continue
            table = read_gc_table(reader, table_address)
            if table is None:
                continue
            seen.add(table_address)
            yield table_address, table
        return


def find_luajit_table_for_node(
    reader: BoundedPageMemoryReader,
    node_address: int,
) -> tuple[int, dict] | None:
    for table_address, table in iter_local_luajit_tables(reader, node_address):
        nodes = read_gc_table_nodes(reader, table)
        if nodes is not None and any(
            int(node.get("nodeAddress", 0)) == node_address
            for node in nodes
        ):
            return table_address, table
    return None


def numeric_table_key(value: object) -> int | None:
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return value
    if isinstance(value, float) and math.isfinite(value) and value.is_integer():
        return int(value)
    return None


def find_numeric_parent_key(
    reader: BoundedPageMemoryReader,
    value_table_address: int,
) -> int | None:
    """Find a nearby numeric-key table that owns one record table."""
    for _, table in iter_local_luajit_tables(reader, value_table_address):
        nodes = read_gc_table_nodes(reader, table)
        if nodes is None:
            continue
        for node in nodes:
            value_table = tagged_pointer_from_value(node.get("value"), LJ_TTAB)
            if value_table != value_table_address:
                continue
            key = numeric_table_key(node.get("key"))
            if key is not None and key > 0:
                return key
    return None


def unwrap_property_value(
    reader: BoundedPageMemoryReader,
    value: dict,
) -> dict | None:
    """Unwrap the three-cell PropertyValueDict value container."""
    if not isinstance(value, dict):
        return None
    if value.get("kind") != "tagged":
        return value

    value_table_address = tagged_pointer_from_value(value, LJ_TTAB)
    if value_table_address is None:
        return None
    value_table = read_gc_table(reader, value_table_address)
    value_array = read_gc_table_array(reader, value_table) if value_table else None
    if value_array is None or len(value_array) != 3:
        return None
    if value_array[0].get("kind") != "nil" or value_array[2].get("kind") != "nil":
        return None
    return value_array[1]


def read_ride_binding_properties(
    reader: BoundedPageMemoryReader,
    property_reference: dict | None,
) -> dict[str, dict]:
    if property_reference is None:
        return {}
    property_table_address = tagged_pointer_from_value(
        property_reference.get("nodeValue"),
        LJ_TTAB,
    )
    if property_table_address is None:
        return {}
    property_table = read_gc_table(reader, property_table_address)
    entries = read_gc_table_entries(reader, property_table) if property_table else None
    if entries is None:
        return {}

    result: dict[str, dict] = {}
    for entry in entries:
        key = entry.get("key")
        if key in RIDE_BINDING_PROPERTY_NAMES:
            value = unwrap_property_value(reader, entry.get("value"))
            if value is not None:
                result[key] = value
    return result


def read_numeric_field(value: dict | None) -> int | float | None:
    if not isinstance(value, dict):
        return None
    if value.get("kind") == "i32":
        try:
            return int(value.get("value"))
        except (TypeError, ValueError):
            return None
    if value.get("kind") == "f64":
        try:
            number = float(value.get("value"))
        except (TypeError, ValueError):
            return None
        return number if math.isfinite(number) else None
    return None


def read_property_int(value: dict | None) -> int | None:
    if not isinstance(value, dict):
        return None
    if value.get("kind") == "i32":
        try:
            return int(value.get("value"))
        except (TypeError, ValueError):
            return None
    if value.get("kind") == "f64":
        try:
            number = float(value.get("value"))
        except (TypeError, ValueError):
            return None
        if math.isfinite(number) and number.is_integer():
            return int(number)
    return None


def read_property_bool(value: dict | None) -> bool | None:
    if not isinstance(value, dict):
        return None
    if value.get("kind") == "boolean":
        return bool(value.get("value"))
    integer = read_property_int(value)
    if integer in (0, 1):
        return integer == 1
    return None


def read_ride_skill_instances(
    adb_path: str,
    serial: str,
    pid: int,
    ranges: list[tuple[int, int]],
    references: list[dict],
    timeout: float,
    skill_name_map: dict[int, str] | None = None,
    max_distance: int = 0x200,
    reader: BoundedPageMemoryReader | None = None,
) -> list[dict]:
    """Read LocalRide.m_Rideskills and optional binding evidence."""
    ride_skill_references = [
        item for item in references
        if item.get("name") == "m_Rideskills"
        and tagged_pointer_from_value(item.get("nodeValue"), LJ_TTAB) is not None
    ]
    ride_id_references = [
        item for item in references
        if item.get("name") == "m_RideId"
        and tagged_pointer_from_value(item.get("nodeValue"), LJ_TSTR) is not None
    ]
    property_references = [
        item for item in references
        if item.get("name") == "PropertyValueDict"
        and tagged_pointer_from_value(item.get("nodeValue"), LJ_TTAB) is not None
    ]
    if not ride_skill_references or not ride_id_references:
        return []

    reader = reader or BoundedPageMemoryReader(adb_path, serial, pid, ranges, timeout)
    instances: list[dict] = []
    used_ride_id_references: set[int] = set()
    for skill_reference in ride_skill_references:
        skill_reference_address = int(skill_reference["reference"], 0)
        matches = [
            item for item in ride_id_references
            if int(item["reference"], 0) not in used_ride_id_references
            and abs(int(item["reference"], 0) - skill_reference_address) <= max_distance
        ]
        if not matches:
            continue
        matches.sort(key=lambda item: abs(int(item["reference"], 0) - skill_reference_address))
        if len(matches) > 1 and abs(int(matches[0]["reference"], 0) - skill_reference_address) == abs(
            int(matches[1]["reference"], 0) - skill_reference_address
        ):
            continue
        ride_id_reference = matches[0]
        property_matches = [
            item for item in property_references
            if abs(int(item["reference"], 0) - skill_reference_address) <= max_distance
        ]
        property_matches.sort(
            key=lambda item: abs(int(item["reference"], 0) - skill_reference_address)
        )
        property_reference = property_matches[0] if property_matches else None
        if (
            len(property_matches) > 1
            and abs(int(property_matches[0]["reference"], 0) - skill_reference_address)
            == abs(int(property_matches[1]["reference"], 0) - skill_reference_address)
        ):
            property_reference = None
        ride_id_address = tagged_pointer_from_value(ride_id_reference.get("nodeValue"), LJ_TSTR)
        skills_table_address = tagged_pointer_from_value(skill_reference.get("nodeValue"), LJ_TTAB)
        if ride_id_address is None or skills_table_address is None:
            continue
        ride_instance_id = read_gcstr(reader, ride_id_address)
        skills_table = read_gc_table(reader, skills_table_address)
        if not ride_instance_id or skills_table is None:
            continue
        array_values = read_gc_table_array(reader, skills_table)
        if array_values is None or len(array_values) > MAX_RIDE_SKILLS_PER_INSTANCE:
            continue

        skills: list[dict] = []
        invalid_skill = False
        for slot_index, value in enumerate(array_values, start=1):
            if value.get("kind") == "nil":
                continue
            skill_table_address = tagged_pointer_from_value(value, LJ_TTAB)
            if skill_table_address is None:
                invalid_skill = True
                break
            skill_table = read_gc_table(reader, skill_table_address)
            skill_entries = read_gc_table_entries(reader, skill_table) if skill_table else None
            if skill_entries is None:
                invalid_skill = True
                break
            by_key = {entry["key"]: entry["value"] for entry in skill_entries}
            skill_id = by_key.get("skillId")
            exp = by_key.get("exp")
            if (
                not isinstance(skill_id, dict)
                or skill_id.get("kind") != "i32"
                or int(skill_id.get("value", 0)) <= 0
                or not isinstance(exp, dict)
                or exp.get("kind") != "i32"
                or int(exp.get("value", -1)) < 0
            ):
                invalid_skill = True
                break
            skill_record = {
                "slotIndex": slot_index,
                "skillId": int(skill_id["value"]),
                "exp": int(exp["value"]),
            }
            skill_name = (skill_name_map or {}).get(skill_record["skillId"])
            if skill_name is not None:
                skill_record["skillName"] = skill_name
            skills.append(skill_record)
        if invalid_skill or not skills:
            continue

        binding_properties = read_ride_binding_properties(reader, property_reference)
        ride_shape_id = read_property_int(binding_properties.get("SHAPE"))
        is_riding = read_property_bool(binding_properties.get("RIDEING"))
        growth_rate = read_numeric_field(binding_properties.get("GROWUP"))
        if growth_rate is not None and growth_rate < 0:
            growth_rate = None

        instance = {
            "rideInstanceId": ride_instance_id,
            "skills": skills,
            "skillCount": len(skills),
            "source": "LocalRide.m_Rideskills",
        }
        if ride_shape_id is not None and ride_shape_id > 0:
            instance["rideShapeId"] = ride_shape_id
        if growth_rate is not None:
            instance["growthRate"] = growth_rate
            instance["growthRateSource"] = "LocalRide.PropertyValueDict.GROWUP"
        if is_riding is not None:
            instance["isRiding"] = is_riding
        if binding_properties:
            instance["bindingSource"] = "LocalRide.PropertyValueDict"
        identity = (
            ride_instance_id,
            tuple((item["slotIndex"], item["skillId"], item["exp"]) for item in skills),
            ride_shape_id,
            growth_rate,
            is_riding,
        )
        if not any(item.get("_identity") == identity for item in instances):
            instance["_identity"] = identity
            instances.append(instance)
            used_ride_id_references.add(int(ride_id_reference["reference"], 0))
        if len(instances) >= MAX_RIDE_INSTANCES:
            break

    for instance in instances:
        instance.pop("_identity", None)
    return instances


def read_gc_string_value(
    reader: BoundedPageMemoryReader,
    value: dict | None,
) -> str | None:
    address = tagged_pointer_from_value(value, LJ_TSTR)
    return read_gcstr(reader, address) if address is not None else None


def read_string_keyed_table(
    reader: BoundedPageMemoryReader,
    table_address: int | None,
) -> dict[str, dict] | None:
    if table_address is None:
        return None
    table = read_gc_table(reader, table_address)
    entries = read_gc_table_entries(reader, table) if table else None
    if entries is None:
        return None
    result: dict[str, dict] = {}
    for entry in entries:
        key = entry.get("key")
        if isinstance(key, str):
            result[key] = entry.get("value")
    return result


def read_ride_refine_skill_records(
    reader: BoundedPageMemoryReader,
    skill_table_address: int | None,
    skill_name_map: dict[int, str],
) -> list[dict] | None:
    skill_table = read_gc_table(reader, skill_table_address) if skill_table_address else None
    skill_values = read_gc_table_array(reader, skill_table) if skill_table else None
    if skill_values is None or len(skill_values) > MAX_RIDE_REFINE_SKILLS_PER_CARD:
        return None

    skills: list[dict] = []
    for slot_index, value in enumerate(skill_values, start=1):
        if value.get("kind") == "nil":
            continue
        record_address = tagged_pointer_from_value(value, LJ_TTAB)
        record = read_string_keyed_table(reader, record_address)
        if record is None:
            return None

        skill_id_text = read_gc_string_value(reader, record.get("name"))
        if skill_id_text is None:
            return None
        try:
            skill_id = int(skill_id_text, 10)
        except (TypeError, ValueError):
            return None
        if skill_id <= 0:
            return None

        skill_record = {
            "slotIndex": slot_index,
            "skillId": skill_id,
        }
        skill_name = skill_name_map.get(skill_id)
        if skill_name is not None:
            skill_record["skillName"] = skill_name
        raw_value = read_gc_string_value(reader, record.get("val"))
        if raw_value is not None:
            skill_record["value"] = raw_value
        skills.append(skill_record)

    return skills if len(skills) == 3 else None


def read_ride_refine_cards(
    adb_path: str,
    serial: str,
    pid: int,
    ranges: list[tuple[int, int]],
    references: list[dict],
    candidate: dict,
    timeout: float,
    skill_name_map: dict[int, str] | None = None,
    reader: BoundedPageMemoryReader | None = None,
    point_hint: dict | None = None,
    point_capture: dict | None = None,
) -> list[dict]:
    """Read the current card and the 20 temporary cards from LocalRide.m_ResetData.

    The selected LocalRide is reached through the runtime m_RideIns reference.
    The reader only accepts the verified 20-card candidate shape and returns an
    empty list when the page is not active or the structure is incomplete.
    """
    active_ride_instance_id = candidate.get("activeRideInstanceId")
    if not isinstance(active_ride_instance_id, str) or not active_ride_instance_id:
        return []

    current_instance = next(
        (
            instance
            for instance in candidate.get("rideInstances", [])
            if isinstance(instance, dict)
            and instance.get("rideInstanceId") == active_ride_instance_id
            and instance.get("isCurrent") is True
        ),
        None,
    )
    if not isinstance(current_instance, dict):
        return []
    current_skills = current_instance.get("skills")
    if not isinstance(current_skills, list) or len(current_skills) != 3:
        return []

    ride_ins_references = [
        item
        for item in references
        if item.get("name") == "m_RideIns"
        and tagged_pointer_from_value(item.get("nodeValue"), LJ_TTAB) is not None
    ]
    if not ride_ins_references:
        return []

    point_ride_ins_address = None
    point_outer_index = None
    point_container_address = None
    if point_hint is not None:
        point_ride_ins_address = parse_address(point_hint.get("rideInsAddress"))
        point_outer_index = parse_address(point_hint.get("resetOuterIndex"))
        point_container_address = parse_address(point_hint.get("containerAddress"))
        if (
            point_ride_ins_address is None
            or point_outer_index is None
            or point_outer_index <= 0
            or not address_in_ranges(point_ride_ins_address, GCTAB_SIZE, ranges)
        ):
            return []

    reader = reader or BoundedPageMemoryReader(adb_path, serial, pid, ranges, timeout)
    seen_ride_ins: set[int] = set()
    selected_cards: list[dict] | None = None
    for reference in ride_ins_references:
        ride_ins_address = tagged_pointer_from_value(reference.get("nodeValue"), LJ_TTAB)
        if ride_ins_address is None or ride_ins_address in seen_ride_ins:
            continue
        if point_ride_ins_address is not None and ride_ins_address != point_ride_ins_address:
            continue
        seen_ride_ins.add(ride_ins_address)
        ride_ins = read_string_keyed_table(reader, ride_ins_address)
        if ride_ins is None:
            continue

        ride_id = read_gc_string_value(reader, ride_ins.get("m_RideId"))
        if ride_id != active_ride_instance_id:
            continue
        reset_address = tagged_pointer_from_value(ride_ins.get("m_ResetData"), LJ_TTAB)
        reset_table = read_gc_table(reader, reset_address) if reset_address else None
        reset_values = read_gc_table_array(reader, reset_table) if reset_table else None
        if reset_values is None or len(reset_values) > MAX_RIDE_REFINE_CARDS:
            continue

        candidate_container: tuple[int, int, dict, list[dict]] | None = None
        if point_outer_index is not None:
            outer_values = (
                [(point_outer_index, reset_values[point_outer_index - 1])]
                if point_outer_index <= len(reset_values)
                else []
            )
        else:
            outer_values = list(enumerate(reset_values, start=1))
        for outer_index, value in outer_values:
            container_address = tagged_pointer_from_value(value, LJ_TTAB)
            if (
                point_container_address is not None
                and container_address != point_container_address
            ):
                continue
            container = read_gc_table(reader, container_address) if container_address else None
            container_values = read_gc_table_array(reader, container) if container else None
            if container_values is None or len(container_values) > MAX_RIDE_REFINE_CARDS:
                continue

            non_empty = [
                item for item in container_values
                if item.get("kind") != "nil"
            ]
            if len(non_empty) != 20:
                continue
            if not all(
                tagged_pointer_from_value(item, LJ_TTAB) is not None
                for item in non_empty
            ):
                continue
            candidate_container = (
                outer_index,
                container_address or 0,
                container,
                container_values,
            )
            break

        if candidate_container is None:
            continue
        outer_index, container_address, _, container_values = candidate_container
        card_source = f"LocalRide.m_ResetData[{outer_index}]"
        parsed_cards: list[dict] = []
        invalid = False
        for card_index, value in enumerate(container_values, start=1):
            if value.get("kind") == "nil":
                continue
            card_address = tagged_pointer_from_value(value, LJ_TTAB)
            card = read_string_keyed_table(reader, card_address)
            if card is None:
                invalid = True
                break
            growth_raw = read_property_int(card.get("growty"))
            speed = read_property_int(card.get("speed"))
            score = read_property_int(card.get("score"))
            skill_address = tagged_pointer_from_value(card.get("skill"), LJ_TTAB)
            if (
                growth_raw is None
                or growth_raw < 0
                or speed is None
                or speed < 0
                or score is None
                or score < 0
                or skill_address is None
            ):
                invalid = True
                break
            skills = read_ride_refine_skill_records(
                reader,
                skill_address,
                skill_name_map or {},
            )
            if skills is None:
                invalid = True
                break
            parsed_cards.append(
                {
                    "cardIndex": card_index,
                    "isCurrent": False,
                    "growthRate": growth_raw / 1000.0,
                    "growthRateSource": f"{card_source}.growty/1000",
                    "speed": speed,
                    "score": score,
                    "source": card_source,
                    "skills": skills,
                }
            )

        if invalid or [item["cardIndex"] for item in parsed_cards] != list(range(2, 22)):
            continue
        selected_cards = parsed_cards
        if point_capture is not None:
            point_capture.clear()
            point_capture.update(
                {
                    "rideInsAddress": hex(ride_ins_address),
                    "resetOuterIndex": outer_index,
                    "containerAddress": hex(container_address),
                }
            )
        break

    if selected_cards is None:
        return []

    current_card = {
        "cardIndex": 1,
        "isCurrent": True,
        "source": "LocalRide.m_Rideskills",
        "skills": [
            {
                "slotIndex": skill.get("slotIndex"),
                "skillId": skill.get("skillId"),
                **(
                    {"skillName": skill["skillName"]}
                    if skill.get("skillName") is not None
                    else {}
                ),
            }
            for skill in current_skills
        ],
    }
    if current_instance.get("growthRate") is not None:
        current_card["growthRate"] = current_instance["growthRate"]
        current_card["growthRateSource"] = current_instance.get(
            "growthRateSource",
            "LocalRide.PropertyValueDict.GROWUP",
        )
    return [current_card] + selected_cards


def has_complete_ride_refine_cards(candidate: dict | None) -> bool:
    """Return whether a candidate contains the complete 21-card page."""
    cards = candidate.get("rideRefineCards") if isinstance(candidate, dict) else None
    return isinstance(cards, list) and len(cards) == EXPECTED_RIDE_REFINE_CARD_COUNT


def bind_current_ride_instance(candidate: dict) -> None:
    """Bind the role mount ID only when one LocalRide proves both keys."""
    instances = candidate.get("rideInstances")
    target_mount_id = candidate.get("mountId")
    candidate["rideBindingStatus"] = "unresolved"
    if candidate.get("isMounted") is False:
        candidate["rideBindingStatus"] = "not_mounted"
        return
    if candidate.get("isMounted") is not True:
        return

    if not isinstance(target_mount_id, int) or target_mount_id <= 0 or not isinstance(instances, list):
        return

    shape_matches = [
        instance for instance in instances
        if instance.get("rideShapeId") == target_mount_id
    ]
    active_matches = [
        instance for instance in shape_matches
        if instance.get("isRiding") is True
    ]
    if len(shape_matches) != 1 or len(active_matches) != 1:
        if len(shape_matches) > 1 or len(active_matches) > 1:
            candidate["rideBindingStatus"] = "ambiguous"
        return

    bound_instance = active_matches[0]
    bound_instance["isCurrent"] = True
    candidate["activeRideInstanceId"] = bound_instance["rideInstanceId"]
    candidate["rideBindingStatus"] = "bound"
    candidate["rideBindingSource"] = "LocalRide.PropertyValueDict.SHAPE+RIDEING"


def copy_current_ride_growth_rate(candidate: dict) -> None:
    """Expose the bound instance GROWUP as the current player growth rate."""
    instances = candidate.get("rideInstances")
    active_id = candidate.get("activeRideInstanceId")
    if not isinstance(instances, list) or not isinstance(active_id, str):
        return
    current = next(
        (
            instance for instance in instances
            if instance.get("rideInstanceId") == active_id
            and instance.get("isCurrent") is True
        ),
        None,
    )
    if current is None or "growthRate" not in current:
        return
    candidate["growthRate"] = current["growthRate"]
    candidate["growthRateSource"] = current.get(
        "growthRateSource",
        "LocalRide.PropertyValueDict.GROWUP",
    )


def discover_key_objects(
    adb_path: str,
    serial: str,
    pid: int,
    ranges: list[tuple[int, int]],
    chunk_size: int,
    timeout: float,
    session: AdbPageMemoryReadSession | None = None,
) -> dict[str, list[int]]:
    key_objects: dict[str, set[int]] = {name: set() for name in DISCOVERED_FIELD_NAMES}
    names = [(name, name.encode("utf-8")) for name in DISCOVERED_FIELD_NAMES]
    chunks = chunk_ranges(ranges, chunk_size)
    for (chunk_start, chunk_size_value), blob in zip(
        chunks,
        read_chunks(adb_path, serial, pid, chunks, timeout, session=session),
    ):
        if len(blob) != chunk_size_value:
            continue
        for name, needle in names:
            for offset in find_all(blob, needle, 128):
                if verify_gcstr(blob, offset, needle):
                    key_objects[name].add(chunk_start + offset - GCSTR_SIZE)
    return {name: sorted(values) for name, values in key_objects.items() if values}


def scan_references(
    adb_path: str,
    serial: str,
    pid: int,
    ranges: list[tuple[int, int]],
    key_objects: dict[str, list[int]],
    chunk_size: int,
    timeout: float,
    session: AdbPageMemoryReadSession | None = None,
) -> list[dict]:
    needles = [
        (name, address, struct.pack("<Q", gc64_tagged_pointer(address, 0xFFFFFFFB)))
        for name, addresses in key_objects.items()
        for address in addresses
    ]
    references: list[dict] = []
    chunks = chunk_ranges(ranges, chunk_size)
    for (chunk_start, chunk_size_value), blob in zip(
        chunks,
        read_chunks(adb_path, serial, pid, chunks, timeout, session=session),
    ):
        if len(blob) != chunk_size_value:
            continue
        for name, object_address, needle in needles:
            for offset in find_all(blob, needle, 128):
                if offset < 8 or chunk_start + offset == object_address:
                    continue
                reference = chunk_start + offset
                if reference % 8 != 0:
                    continue
                references.append(
                    {
                        "name": name,
                        "stringObject": hex(object_address),
                        "reference": hex(reference),
                        "nodeValueAddress": hex(reference - 8),
                        "nodeValue": decode_tvalue(blob[offset - 8 : offset]),
                    }
                )
                if len(references) >= 4000:
                    return references
    return references


def nearest_field(references: list[dict], name: str, anchor: int, max_distance: int) -> dict | None:
    candidates = [
        item
        for item in references
        if item["name"] == name
        and abs(int(item["reference"], 0) - anchor) <= max_distance
    ]
    if not candidates:
        return None
    return min(candidates, key=lambda item: abs(int(item["reference"], 0) - anchor))


def normalize_coordinate(value: dict | None) -> int | None:
    """Normalize integer or LuaJIT boxed-floating map coordinates to Int32."""
    if not isinstance(value, dict):
        return None

    kind = value.get("kind")
    if kind == "i32":
        try:
            coordinate = int(value.get("value"))
        except (TypeError, ValueError, OverflowError):
            return None
    elif kind == "f64":
        try:
            number = float(value.get("value"))
        except (TypeError, ValueError, OverflowError):
            return None
        if not math.isfinite(number):
            return None
        if number < -2147483648 or number > 2147483647:
            return None
        coordinate = int(round(number))
    else:
        return None

    return coordinate if -2147483648 <= coordinate <= 2147483647 else None


def build_local_candidates(references: list[dict], max_distance: int) -> list[dict]:
    candidates: list[dict] = []
    ride_references = [item for item in references if item["name"] == "m_RideId"]
    for ride in ride_references:
        anchor = int(ride["reference"], 0)
        group = {"m_RideId": ride}
        for name in FIELD_NAMES:
            if name == "m_RideId":
                continue
            field = nearest_field(references, name, anchor, max_distance)
            if field is None:
                group = {}
                break
            group[name] = field
        if len(group) != len(FIELD_NAMES):
            continue

        values = {name: group[name]["nodeValue"] for name in FIELD_NAMES}
        if values["m_IsLocalPlayer"].get("kind") != "boolean" or not values["m_IsLocalPlayer"].get("value"):
            continue
        if values["m_PlayerId"].get("kind") != "i32" or int(values["m_PlayerId"].get("value", 0)) <= 0:
            continue
        speed = values["m_RoleMoveSpeed"]
        if speed.get("kind") not in {"i32", "f64"} or not math.isfinite(float(speed.get("value", 0))):
            continue
        gx = normalize_coordinate(values["m_Gx"])
        gy = normalize_coordinate(values["m_Gy"])
        if gx is None or gy is None:
            continue

        ride_value = values["m_RideId"]
        if ride_value.get("kind") == "nil":
            mount_id = None
            mounted = False
        elif ride_value.get("kind") in {"i32", "f64"}:
            mount_id = int(ride_value["value"])
            mounted = True
        else:
            mount_id = None
            mounted = None

        references_by_name = {
            name: {
                "reference": group[name]["reference"],
                "nodeValueAddress": group[name]["nodeValueAddress"],
            }
            for name in FIELD_NAMES
        }
        candidate = {
            "playerId": int(values["m_PlayerId"]["value"]),
            "isLocalPlayer": True,
            "mountId": mount_id,
            "isMounted": mounted,
            "roleMoveSpeed": float(speed["value"]),
            "gx": gx,
            "gy": gy,
            "rawValues": values,
            "references": references_by_name,
            "span": max(
                int(item["reference"], 0) for item in group.values()
            )
            - min(int(item["reference"], 0) for item in group.values()),
        }
        identity = (
            candidate["playerId"],
            candidate["mountId"],
            candidate["gx"],
            candidate["gy"],
            candidate["roleMoveSpeed"],
        )
        if not any(item["identity"] == identity for item in candidates):
            candidate["identity"] = identity
            candidates.append(candidate)

    for candidate in candidates:
        candidate.pop("identity", None)
    return candidates


def parse_address(value: object) -> int | None:
    """Parse one bounded process-memory address from cache metadata."""
    if isinstance(value, bool):
        return None
    if isinstance(value, int):
        return value if value >= 0 else None
    if isinstance(value, str):
        try:
            parsed = int(value, 0)
        except (TypeError, ValueError, OverflowError):
            return None
        return parsed if parsed >= 0 else None
    return None


def reference_descriptor(reference: dict) -> dict | None:
    """Keep only the address metadata needed by a fast-read plan."""
    if not isinstance(reference, dict):
        return None
    name = reference.get("name")
    if not isinstance(name, str) or name not in DISCOVERED_FIELD_NAMES:
        return None
    reference_address = parse_address(reference.get("reference"))
    node_value_address = parse_address(reference.get("nodeValueAddress"))
    if reference_address is None:
        return None
    if node_value_address is None:
        node_value_address = reference_address - 8
    if node_value_address < 0:
        return None
    return {
        "name": name,
        "reference": hex(reference_address),
        "nodeValueAddress": hex(node_value_address),
    }


def normalize_cached_ranges(value: object) -> list[tuple[int, int]] | None:
    if not isinstance(value, list):
        return None
    ranges: list[tuple[int, int]] = []
    for item in value:
        if not isinstance(item, (list, tuple)) or len(item) != 2:
            return None
        start = parse_address(item[0])
        end = parse_address(item[1])
        if (
            start is None
            or end is None
            or start % PAGE_SIZE
            or end % PAGE_SIZE
            or end <= start
        ):
            return None
        ranges.append((start, end))
    return ranges if ranges else None


def normalize_cached_pages(
    value: object,
    ranges: list[tuple[int, int]],
) -> list[int] | None:
    if value is None:
        return []
    if not isinstance(value, list):
        return None
    pages: set[int] = set()
    for item in value:
        page = parse_address(item)
        if (
            page is None
            or page % PAGE_SIZE
            or not address_in_ranges(page, PAGE_SIZE, ranges)
        ):
            return None
        pages.add(page)
    return sorted(pages)


def select_cache_references(
    references: list[dict],
    candidate: dict,
    max_distance: int,
) -> list[dict]:
    """Select the base fields and only references used by rich ride reads."""
    candidate_references = candidate.get("references") if isinstance(candidate, dict) else None
    selected: dict[tuple[str, int], dict] = {}

    def add(item: dict) -> None:
        descriptor = reference_descriptor(item)
        if descriptor is None:
            return
        reference_address = parse_address(descriptor["reference"])
        if reference_address is None:
            return
        selected[(descriptor["name"], reference_address)] = descriptor

    if isinstance(candidate_references, dict):
        for name, item in candidate_references.items():
            if isinstance(item, dict):
                named_item = dict(item)
                named_item["name"] = name
                add(named_item)

    relation_distance = 0x200
    ride_skill_references = [
        item for item in references
        if item.get("name") == "m_Rideskills"
        and tagged_pointer_from_value(item.get("nodeValue"), LJ_TTAB) is not None
    ]
    ride_id_references = [
        item for item in references
        if item.get("name") == "m_RideId"
        and tagged_pointer_from_value(item.get("nodeValue"), LJ_TSTR) is not None
    ]
    property_references = [
        item for item in references
        if item.get("name") == "PropertyValueDict"
        and tagged_pointer_from_value(item.get("nodeValue"), LJ_TTAB) is not None
    ]
    for skill_reference in ride_skill_references:
        skill_address = parse_address(skill_reference.get("reference"))
        if skill_address is None:
            continue
        add(skill_reference)
        for item in ride_id_references:
            item_address = parse_address(item.get("reference"))
            if item_address is not None and abs(item_address - skill_address) <= relation_distance:
                add(item)
        for item in property_references:
            item_address = parse_address(item.get("reference"))
            if item_address is not None and abs(item_address - skill_address) <= relation_distance:
                add(item)

    for item in references:
        if (
            item.get("name") == "m_RideIns"
            and tagged_pointer_from_value(item.get("nodeValue"), LJ_TTAB) is not None
        ):
            add(item)

    return list(selected.values())


def build_probe_cache(
    identity: dict,
    ranges: list[tuple[int, int]],
    references: list[dict],
    candidate: dict,
    max_distance: int,
    warm_pages: Iterable[int] | None = None,
) -> dict | None:
    selected = select_cache_references(references, candidate, max_distance)
    candidate_references = candidate.get("references") if isinstance(candidate, dict) else None
    base_references: dict[str, dict] = {}
    if isinstance(candidate_references, dict):
        for name in FIELD_NAMES:
            raw_reference = candidate_references.get(name)
            if isinstance(raw_reference, dict):
                raw_reference = dict(raw_reference)
                raw_reference["name"] = name
            descriptor = reference_descriptor(raw_reference)
            if descriptor is None:
                return None
            base_references[name] = descriptor
    if len(base_references) != len(FIELD_NAMES):
        return None

    normalized_warm_pages = sorted({
        page
        for item in (warm_pages or [])
        for page in [parse_address(item)]
        if page is not None
        and page % PAGE_SIZE == 0
        and address_in_ranges(page, PAGE_SIZE, ranges)
    })

    return {
        "schema": PROBE_CACHE_SCHEMA,
        "schemaVersion": PROBE_CACHE_VERSION,
        "process": {
            "pid": int(identity.get("pid", 0)),
            "startTicks": int(identity.get("startTicks", 0)),
            "exe": str(identity.get("exe", "")),
        },
        "ranges": [[start, end] for start, end in ranges],
        "candidate": {"references": base_references},
        "references": selected,
        "warmPages": normalized_warm_pages,
    }


def _write_json_payload(path: Path | None, payload: dict | None) -> None:
    if path is None or not isinstance(payload, dict):
        return
    temporary_path = path.with_name(
        path.name + ".tmp-" + str(os.getpid())
    )
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        temporary_path.write_text(
            json.dumps(payload, ensure_ascii=False, separators=(",", ":")),
            encoding="utf-8",
        )
        os.replace(temporary_path, path)
    except (OSError, TypeError, ValueError):
        try:
            temporary_path.unlink()
        except OSError:
            pass


def write_probe_cache(cache_path: Path | None, payload: dict | None) -> None:
    _write_json_payload(cache_path, payload)


def write_probe_warm_hints(hint_path: Path | None, payload: dict | None) -> None:
    _write_json_payload(hint_path, payload)


def load_probe_cache(
    cache_path: Path | None,
    identity: dict,
) -> dict | None:
    if cache_path is None:
        return None
    try:
        payload = json.loads(cache_path.read_text(encoding="utf-8"))
    except (OSError, ValueError, TypeError):
        return None
    if not isinstance(payload, dict) or payload.get("schema") != PROBE_CACHE_SCHEMA:
        return None
    if payload.get("schemaVersion") != PROBE_CACHE_VERSION:
        return None

    cached_process = payload.get("process")
    if not isinstance(cached_process, dict) or (
        cached_process.get("pid") != identity.get("pid")
        or cached_process.get("startTicks") != identity.get("startTicks")
        or cached_process.get("exe") != identity.get("exe")
    ):
        return None

    ranges = normalize_cached_ranges(payload.get("ranges"))
    cached_candidate = payload.get("candidate")
    cached_references = payload.get("references")
    warm_pages = (
        normalize_cached_pages(payload.get("warmPages", []), ranges)
        if ranges is not None
        else None
    )
    if (
        ranges is None
        or not isinstance(cached_candidate, dict)
        or not isinstance(cached_candidate.get("references"), dict)
        or not isinstance(cached_references, list)
        or warm_pages is None
    ):
        return None

    base_references: dict[str, dict] = {}
    for name in FIELD_NAMES:
        descriptor = reference_descriptor(cached_candidate["references"].get(name))
        if descriptor is None:
            return None
        base_references[name] = descriptor

    references: list[dict] = []
    seen: set[tuple[str, int]] = set()
    for item in cached_references:
        descriptor = reference_descriptor(item)
        if descriptor is None:
            return None
        reference_address = parse_address(descriptor["reference"])
        if reference_address is None:
            return None
        key = (descriptor["name"], reference_address)
        if key not in seen:
            seen.add(key)
            references.append(descriptor)

    return {
        "ranges": ranges,
        "candidate": {"references": base_references},
        "references": references,
        "warmPages": warm_pages,
    }


def probe_warm_hints_path(cache_path: Path | None) -> Path | None:
    """Return the non-authoritative cross-process warm-start sidecar path."""
    if cache_path is None:
        return None
    return cache_path.with_name(cache_path.name + ".warm.json")


def _warm_hint_location(
    address: int,
    size: int,
    ranges: list[tuple[int, int]],
) -> dict | None:
    for range_index, (start, end) in enumerate(ranges):
        if start <= address and address + size <= end:
            return {
                "rangeIndex": range_index,
                "offset": address - start,
            }
    return None


def _warm_hint_descriptor(
    reference: dict,
    ranges: list[tuple[int, int]],
) -> dict | None:
    descriptor = reference_descriptor(reference)
    if descriptor is None:
        return None
    reference_address = parse_address(descriptor["reference"])
    node_value_address = parse_address(descriptor["nodeValueAddress"])
    if reference_address is None or node_value_address is None:
        return None
    reference_location = _warm_hint_location(reference_address, 8, ranges)
    node_value_location = _warm_hint_location(node_value_address, 8, ranges)
    if reference_location is None or node_value_location is None:
        return None
    if (
        reference_location["offset"] % 8
        or node_value_location["offset"] % 8
    ):
        return None
    return {
        "name": descriptor["name"],
        "reference": reference_location,
        "nodeValue": node_value_location,
    }


def build_probe_warm_hints_from_cache(cache_payload: dict | None) -> dict | None:
    """Convert an authoritative cache to offsets safe to try after restart.

    The sidecar deliberately stores no process-memory address.  It is only a
    layout hint; every translated address must still pass the normal candidate
    and complete-card validation before it can be used.
    """
    if not isinstance(cache_payload, dict):
        return None
    ranges = normalize_cached_ranges(cache_payload.get("ranges"))
    cached_candidate = cache_payload.get("candidate")
    cached_references = cache_payload.get("references")
    if (
        ranges is None
        or not isinstance(cached_candidate, dict)
        or not isinstance(cached_candidate.get("references"), dict)
        or not isinstance(cached_references, list)
        or len(cached_references) > MAX_WARM_HINT_REFERENCES
    ):
        return None

    base_references: dict[str, dict] = {}
    raw_base_references = cached_candidate["references"]
    for name in FIELD_NAMES:
        raw_reference = raw_base_references.get(name)
        if not isinstance(raw_reference, dict):
            return None
        named_reference = dict(raw_reference)
        named_reference["name"] = name
        descriptor = _warm_hint_descriptor(named_reference, ranges)
        if descriptor is None:
            return None
        base_references[name] = descriptor

    references: list[dict] = []
    seen_references: set[tuple[str, int, int]] = set()
    for raw_reference in cached_references:
        descriptor = _warm_hint_descriptor(raw_reference, ranges)
        if descriptor is None:
            return None
        key = (
            descriptor["name"],
            descriptor["reference"]["rangeIndex"],
            descriptor["reference"]["offset"],
        )
        if key not in seen_references:
            seen_references.add(key)
            references.append(descriptor)

    return {
        "schema": WARM_HINT_SCHEMA,
        "schemaVersion": WARM_HINT_VERSION,
        "ranges": [{"size": end - start} for start, end in ranges],
        "candidate": {"references": base_references},
        "references": references,
        # Pages touched while expanding one process are temporary allocator
        # details. Carrying them across a restart can make an otherwise valid
        # reference plan impossible to translate when an arena is resized or
        # split. The translated references are sufficient; missing pages are
        # fetched on demand and still pass the complete snapshot validation.
        "warmPages": [],
    }


def _normalize_warm_hint_ranges(value: object) -> list[int] | None:
    if not isinstance(value, list) or not value or len(value) > 4096:
        return None
    sizes: list[int] = []
    for item in value:
        if not isinstance(item, dict):
            return None
        size = parse_address(item.get("size"))
        if size is None or size < PAGE_SIZE or size % PAGE_SIZE:
            return None
        sizes.append(size)
    return sizes


def _normalize_warm_hint_location(
    value: object,
    range_sizes: list[int],
    size: int,
    alignment: int,
) -> dict | None:
    if not isinstance(value, dict):
        return None
    range_index = parse_address(value.get("rangeIndex"))
    offset = parse_address(value.get("offset"))
    if (
        range_index is None
        or offset is None
        or range_index >= len(range_sizes)
        or offset % alignment
        or offset + size > range_sizes[range_index]
    ):
        return None
    return {"rangeIndex": range_index, "offset": offset}


def _normalize_warm_hint_descriptor(
    value: object,
    range_sizes: list[int],
) -> dict | None:
    if not isinstance(value, dict) or value.get("name") not in DISCOVERED_FIELD_NAMES:
        return None
    reference = _normalize_warm_hint_location(
        value.get("reference"),
        range_sizes,
        8,
        8,
    )
    node_value = _normalize_warm_hint_location(
        value.get("nodeValue"),
        range_sizes,
        8,
        8,
    )
    if reference is None or node_value is None:
        return None
    return {
        "name": value["name"],
        "reference": reference,
        "nodeValue": node_value,
    }


def load_probe_warm_hints(hint_path: Path | None) -> dict | None:
    """Load only relative layout metadata; never treat it as an address plan."""
    if hint_path is None:
        return None
    try:
        payload = json.loads(hint_path.read_text(encoding="utf-8"))
    except (OSError, ValueError, TypeError):
        return None
    if not isinstance(payload, dict) or payload.get("schema") != WARM_HINT_SCHEMA:
        return None
    if payload.get("schemaVersion") != WARM_HINT_VERSION:
        return None

    range_sizes = _normalize_warm_hint_ranges(payload.get("ranges"))
    candidate = payload.get("candidate")
    raw_references = payload.get("references")
    raw_warm_pages = payload.get("warmPages", [])
    if (
        range_sizes is None
        or not isinstance(candidate, dict)
        or not isinstance(candidate.get("references"), dict)
        or not isinstance(raw_references, list)
        or len(raw_references) > MAX_WARM_HINT_REFERENCES
        or not isinstance(raw_warm_pages, list)
        or len(raw_warm_pages) > MAX_WARM_HINT_PAGES
    ):
        return None

    base_references: dict[str, dict] = {}
    for name in FIELD_NAMES:
        raw_reference = candidate["references"].get(name)
        descriptor = _normalize_warm_hint_descriptor(raw_reference, range_sizes)
        if descriptor is None or descriptor["name"] != name:
            return None
        base_references[name] = descriptor

    references: list[dict] = []
    seen_references: set[tuple[str, int, int]] = set()
    for raw_reference in raw_references:
        descriptor = _normalize_warm_hint_descriptor(raw_reference, range_sizes)
        if descriptor is None:
            return None
        key = (
            descriptor["name"],
            descriptor["reference"]["rangeIndex"],
            descriptor["reference"]["offset"],
        )
        if key not in seen_references:
            seen_references.add(key)
            references.append(descriptor)

    warm_pages: list[dict] = []
    seen_pages: set[tuple[int, int]] = set()
    for raw_page in raw_warm_pages:
        location = _normalize_warm_hint_location(
            raw_page,
            range_sizes,
            PAGE_SIZE,
            PAGE_SIZE,
        )
        if location is None:
            return None
        key = (location["rangeIndex"], location["offset"])
        if key not in seen_pages:
            seen_pages.add(key)
            warm_pages.append(location)

    return {
        "rangeSizes": range_sizes,
        "candidate": {"references": base_references},
        "references": references,
        "warmPages": warm_pages,
    }


def _match_warm_hint_ranges(
    range_sizes: list[int],
    ranges: list[tuple[int, int]],
    locations: Iterable[tuple[int, int, int]],
) -> dict[int, int] | None:
    required: dict[int, int] = {}
    expected_sizes = range_sizes
    for range_index, offset, size in locations:
        if range_index < 0 or range_index >= len(expected_sizes):
            return None
        required[range_index] = max(required.get(range_index, 0), offset + size)

    mapping: dict[int, int] = {}
    used: set[int] = set()
    for old_index in sorted(required):
        expected_size = expected_sizes[old_index]
        minimum_size = required[old_index]
        compatible = [
            (
                0 if end - start == expected_size else 1,
                abs((end - start) - expected_size),
                abs(new_index - old_index),
                new_index,
            )
            for new_index, (start, end) in enumerate(ranges)
            if new_index not in used and end - start >= minimum_size
        ]
        if not compatible:
            return None
        new_index = min(compatible)[-1]
        mapping[old_index] = new_index
        used.add(new_index)
    return mapping


def build_warm_cache_from_hints(
    hints: dict | None,
    ranges: list[tuple[int, int]],
) -> dict | None:
    """Translate relative hints to this process and keep normal validation."""
    if not isinstance(hints, dict):
        return None
    range_sizes = hints.get("rangeSizes")
    candidate = hints.get("candidate")
    raw_references = hints.get("references")
    warm_pages = hints.get("warmPages", [])
    if (
        not isinstance(range_sizes, list)
        or not isinstance(candidate, dict)
        or not isinstance(candidate.get("references"), dict)
        or not isinstance(raw_references, list)
        or not isinstance(warm_pages, list)
    ):
        return None

    locations: list[tuple[int, int, int]] = []
    def collect_location(location: object, size: int) -> bool:
        if not isinstance(location, dict):
            return False
        range_index = parse_address(location.get("rangeIndex"))
        offset = parse_address(location.get("offset"))
        if range_index is None or offset is None:
            return False
        locations.append((range_index, offset, size))
        return True

    base_references = candidate["references"]
    for name in FIELD_NAMES:
        descriptor = base_references.get(name)
        if (
            not isinstance(descriptor, dict)
            or descriptor.get("name") != name
            or not collect_location(descriptor.get("reference"), 8)
            or not collect_location(descriptor.get("nodeValue"), 8)
        ):
            return None
    for descriptor in raw_references:
        if (
            not isinstance(descriptor, dict)
            or descriptor.get("name") not in DISCOVERED_FIELD_NAMES
            or not collect_location(descriptor.get("reference"), 8)
            or not collect_location(descriptor.get("nodeValue"), 8)
        ):
            return None
    mapping = _match_warm_hint_ranges(range_sizes, ranges, locations)
    if mapping is None:
        return None

    def translate_location(location: dict, size: int) -> int | None:
        range_index = parse_address(location.get("rangeIndex"))
        offset = parse_address(location.get("offset"))
        if range_index is None or offset is None:
            return None
        new_index = mapping.get(range_index)
        if new_index is None:
            return None
        address = ranges[new_index][0] + offset
        return address if address_in_ranges(address, size, ranges) else None

    def materialize_descriptor(descriptor: dict) -> dict | None:
        reference_address = translate_location(descriptor["reference"], 8)
        node_value_address = translate_location(descriptor["nodeValue"], 8)
        if reference_address is None or node_value_address is None:
            return None
        return {
            "name": descriptor["name"],
            "reference": hex(reference_address),
            "nodeValueAddress": hex(node_value_address),
        }

    translated_base: dict[str, dict] = {}
    for name in FIELD_NAMES:
        descriptor = base_references.get(name)
        translated = materialize_descriptor(descriptor)
        if translated is None or translated["name"] != name:
            return None
        translated_base[name] = translated

    translated_references: list[dict] = []
    seen_references: set[tuple[str, int]] = set()
    for descriptor in raw_references:
        translated = materialize_descriptor(descriptor)
        if translated is None:
            return None
        key = (translated["name"], parse_address(translated["reference"]))
        if key not in seen_references:
            seen_references.add(key)
            translated_references.append(translated)
    for descriptor in translated_base.values():
        key = (descriptor["name"], parse_address(descriptor["reference"]))
        if key not in seen_references:
            seen_references.add(key)
            translated_references.append(descriptor)

    return {
        "ranges": ranges,
        "candidate": {"references": translated_base},
        "references": translated_references,
        "warmPages": [],
    }


def select_warm_hint_priority_ranges(
    hints: dict | None,
    ranges: list[tuple[int, int]],
    radius: int = WARM_HINT_PRIORITY_RANGE_RADIUS,
) -> list[tuple[int, int]]:
    """Use old base-field locations only to prioritize a validated scan.

    Lua table nodes can move and reorder after a process restart, so relative
    addresses are not authoritative. The anonymous-range order is stable
    enough to define a bounded first pass, while the normal field, player and
    complete-card validation still decides whether that pass is usable.
    """
    if (
        not isinstance(hints, dict)
        or not isinstance(ranges, list)
        or not ranges
        or radius < 0
    ):
        return []
    range_sizes = hints.get("rangeSizes")
    candidate = hints.get("candidate")
    if (
        not isinstance(range_sizes, list)
        or not range_sizes
        or not isinstance(candidate, dict)
        or not isinstance(candidate.get("references"), dict)
    ):
        return []

    old_count = len(range_sizes)
    new_count = len(ranges)
    old_indices: set[int] = set()
    for name in FIELD_NAMES:
        descriptor = candidate["references"].get(name)
        reference = descriptor.get("reference") if isinstance(descriptor, dict) else None
        old_index = parse_address(reference.get("rangeIndex")) if isinstance(reference, dict) else None
        if old_index is None or old_index >= old_count:
            return []
        old_indices.add(old_index)

    selected_indices: set[int] = set()
    for old_index in old_indices:
        center = (
            0
            if old_count <= 1 or new_count <= 1
            else int(round(old_index * (new_count - 1) / (old_count - 1)))
        )
        first = max(0, center - radius)
        last = min(new_count - 1, center + radius)
        selected_indices.update(range(first, last + 1))

    # Interned Lua field strings frequently live in small allocator ranges far
    # away from the table nodes that reference them. Include those cheap ranges
    # so the priority pass can discover every required key before scanning the
    # much larger heaps for references.
    selected_indices.update(
        index
        for index, (start, end) in enumerate(ranges)
        if end - start <= WARM_HINT_PRIORITY_SMALL_RANGE_BYTES
    )

    if not selected_indices or len(selected_indices) >= new_count:
        return []
    selected = [ranges[index] for index in sorted(selected_indices)]
    selected_bytes = sum(end - start for start, end in selected)
    total_bytes = sum(end - start for start, end in ranges)
    if total_bytes <= 0 or selected_bytes * 5 >= total_bytes * 4:
        return []
    return selected


def select_cold_priority_ranges(
    ranges: list[tuple[int, int]],
) -> list[tuple[int, int]]:
    """Select a small, safe first pass when no cross-process hint exists.

    This is only a speculative priority pass.  It never becomes authoritative:
    the complete field, identity, binding and 21-card checks below must pass,
    otherwise discovery continues over every eligible mapping.
    """
    if not ranges:
        return []
    selected = [
        (start, end)
        for start, end in ranges
        if end - start <= WARM_HINT_PRIORITY_SMALL_RANGE_BYTES
    ]
    if (
        not selected
        or len(selected) >= len(ranges)
        or len(selected) > COLD_PRIORITY_MAX_RANGES
    ):
        return []
    selected_bytes = sum(end - start for start, end in selected)
    total_bytes = sum(end - start for start, end in ranges)
    if total_bytes <= 0 or selected_bytes > total_bytes * COLD_PRIORITY_MAX_TOTAL_RATIO:
        return []
    return selected


def refresh_cached_references(
    reader: BoundedPageMemoryReader,
    descriptors: list[dict],
    warm_pages: Iterable[int] | None = None,
) -> list[dict] | None:
    spans: list[tuple[int, int]] = []
    normalized: list[dict] = []
    for descriptor in descriptors:
        item = reference_descriptor(descriptor)
        if item is None:
            return None
        node_value_address = parse_address(item["nodeValueAddress"])
        if node_value_address is None:
            return None
        spans.append((node_value_address, 8))
        normalized.append(item)
    spans.extend((page, PAGE_SIZE) for page in (warm_pages or []))
    if not reader.prefetch(spans):
        return None

    refreshed: list[dict] = []
    for item in normalized:
        node_value_address = parse_address(item["nodeValueAddress"])
        raw = reader.read(node_value_address, 8) if node_value_address is not None else b""
        if len(raw) != 8:
            return None
        refreshed_item = dict(item)
        refreshed_item["nodeValue"] = decode_tvalue(raw)
        refreshed.append(refreshed_item)
    return refreshed


def build_cached_candidate(
    cached_candidate: dict,
    refreshed_references: list[dict],
) -> dict | None:
    candidate_references = cached_candidate.get("references")
    if not isinstance(candidate_references, dict):
        return None
    refreshed_by_key: dict[tuple[str, int], dict] = {}
    for item in refreshed_references:
        reference_address = parse_address(item.get("reference"))
        if reference_address is None:
            continue
        refreshed_by_key[(item.get("name"), reference_address)] = item

    values: dict[str, dict] = {}
    base_references: dict[str, dict] = {}
    for name in FIELD_NAMES:
        descriptor = reference_descriptor(candidate_references.get(name))
        reference_address = parse_address(descriptor.get("reference")) if descriptor else None
        if descriptor is None or reference_address is None:
            return None
        refreshed = refreshed_by_key.get((name, reference_address))
        if refreshed is None or not isinstance(refreshed.get("nodeValue"), dict):
            return None
        values[name] = refreshed["nodeValue"]
        base_references[name] = descriptor

    if values["m_IsLocalPlayer"].get("kind") != "boolean" or not values["m_IsLocalPlayer"].get("value"):
        return None
    if values["m_PlayerId"].get("kind") != "i32" or int(values["m_PlayerId"].get("value", 0)) <= 0:
        return None
    speed = values["m_RoleMoveSpeed"]
    if speed.get("kind") not in {"i32", "f64"} or not math.isfinite(float(speed.get("value", 0))):
        return None
    gx = normalize_coordinate(values["m_Gx"])
    gy = normalize_coordinate(values["m_Gy"])
    if gx is None or gy is None:
        return None

    ride_value = values["m_RideId"]
    if ride_value.get("kind") == "nil":
        mount_id = None
        mounted = False
    elif ride_value.get("kind") in {"i32", "f64"}:
        try:
            mount_id = int(ride_value["value"])
        except (TypeError, ValueError, OverflowError):
            return None
        mounted = True
    else:
        mount_id = None
        mounted = None
    if mounted is None:
        return None

    return {
        "playerId": int(values["m_PlayerId"]["value"]),
        "isLocalPlayer": True,
        "mountId": mount_id,
        "isMounted": mounted,
        "roleMoveSpeed": float(speed["value"]),
        "gx": gx,
        "gy": gy,
        "rawValues": values,
        "references": base_references,
        "span": max(
            parse_address(item["reference"]) for item in base_references.values()
        ) - min(
            parse_address(item["reference"]) for item in base_references.values()
        ),
    }


def read_cached_snapshot(
    adb_path: str,
    serial: str,
    pid: int,
    cache: dict,
    timeout: float,
    skill_name_map: dict[int, str],
    session: AdbPageMemoryReadSession | None = None,
    point_hint: dict | None = None,
    point_capture: dict | None = None,
    card_timing: dict[str, float] | None = None,
) -> tuple[dict, int, int, list[int]] | None:
    ranges = cache.get("ranges")
    cached_candidate = cache.get("candidate")
    cached_references = cache.get("references")
    if not isinstance(ranges, list) or not isinstance(cached_candidate, dict) or not isinstance(cached_references, list):
        return None

    reader = BoundedPageMemoryReader(
        adb_path,
        serial,
        pid,
        ranges,
        timeout,
        shared_session=session,
    )
    refreshed_references = refresh_cached_references(
        reader,
        cached_references,
        warm_pages=cache.get("warmPages"),
    )
    if refreshed_references is None:
        return None
    candidate = build_cached_candidate(cached_candidate, refreshed_references)
    if candidate is None:
        return None

    card_started = time.perf_counter()
    candidate["rideInstances"] = read_ride_skill_instances(
        adb_path,
        serial,
        pid,
        ranges,
        refreshed_references,
        timeout,
        skill_name_map=skill_name_map,
        reader=reader,
    )
    bind_current_ride_instance(candidate)
    copy_current_ride_growth_rate(candidate)
    candidate["rideRefineCards"] = read_ride_refine_cards(
        adb_path,
        serial,
        pid,
        ranges,
        refreshed_references,
        candidate,
        timeout,
        skill_name_map=skill_name_map,
        reader=reader,
        point_hint=point_hint,
        point_capture=point_capture,
    )
    if card_timing is not None:
        card_timing["cardParseMs"] = card_timing.get("cardParseMs", 0.0) + (
            time.perf_counter() - card_started
        ) * 1000.0
    return candidate, len(ranges), len(reader.pages), sorted(reader.accessed_pages)


def process_identity(
    adb_path: str,
    serial: str,
    pid: int,
    timeout: float,
    session: AdbPageMemoryReadSession | None = None,
) -> dict | None:
    if session is not None:
        if session.closed:
            return None
        stat_raw = session.read_text(f"cat /proc/{pid}/stat", timeout)
        if stat_raw is None:
            return None
        stat_text = stat_raw.decode("utf-8", "replace").strip()
    else:
        stat_command = f"su -c 'cat /proc/{pid}/stat'"
        stat_result = run_adb(adb_path, serial, ["shell", stat_command], timeout)
        if stat_result.returncode != 0:
            return None
        stat_text = stat_result.stdout.decode("utf-8", "replace").strip()
    close_paren = stat_text.rfind(")")
    if close_paren < 0:
        return None
    fields_after_comm = stat_text[close_paren + 2 :].split()
    if len(fields_after_comm) <= 19:
        return None
    try:
        start_ticks = int(fields_after_comm[19])
    except ValueError:
        return None

    if session is not None:
        if session.closed:
            return None
        exe_raw = session.read_text(f"readlink /proc/{pid}/exe", timeout)
        executable = (
            exe_raw.decode("utf-8", "replace").strip()
            if exe_raw is not None
            else ""
        ) or "/system/bin/app_process64"
    else:
        exe_command = f"su -c 'readlink /proc/{pid}/exe'"
        exe_result = run_adb(adb_path, serial, ["shell", exe_command], timeout)
        executable = exe_result.stdout.decode("utf-8", "replace").strip() or "/system/bin/app_process64"
    return {"pid": pid, "startTicks": start_ticks, "exe": executable}


def stable_snapshot_session_id(pid: int, identity: dict | None) -> str:
    """Return one session identity for the lifetime of an Android process.

    The desktop state machine compares this value across successive snapshots.
    A fresh UUID per probe invocation would make every refresh look like a new
    session even when PID and process start ticks are unchanged.
    """
    if isinstance(identity, dict):
        identity_pid = identity.get("pid")
        start_ticks = identity.get("startTicks")
        executable = identity.get("exe")
        if (
            isinstance(identity_pid, int)
            and identity_pid > 0
            and isinstance(start_ticks, int)
            and start_ticks > 0
            and isinstance(executable, str)
            and executable.strip()
        ):
            seed = f"{identity_pid}:{start_ticks}:{executable.strip()}"
            return str(uuid.uuid5(uuid.NAMESPACE_URL, "mount-status-snapshot:" + seed))

    # Error snapshots are rejected before session continuity is evaluated, but
    # keep the error payload schema valid when process identity is unavailable.
    return str(uuid.uuid4())


def snapshot_result(
    pid: int,
    identity: dict | None,
    status: str,
    diagnostic_code: str,
    candidates: list[dict],
    ranges_count: int,
    chunks_read: int,
    strategy: str = "luajit_gc64_table_nodes",
    cache_hit: bool = False,
    warm_start: bool = False,
    missing_fields: list[str] | None = None,
    sequence: int = 1,
    read_path: str = "",
    timing: dict[str, float] | None = None,
) -> dict:
    resolved_read_path = read_path or (
        "fast" if cache_hit else "warm" if warm_start else "discover"
    )
    result = {
        "event": "mount_status_snapshot",
        "schema": "mount_status_snapshot.v1",
        "schemaVersion": 1,
        "readOnly": True,
        "actionAuthorized": False,
        "sessionId": stable_snapshot_session_id(pid, identity),
        "sequence": max(1, int(sequence)),
        "readAtUtc": datetime.now(timezone.utc).isoformat(),
        "status": status,
        "diagnosticCode": diagnostic_code,
        "process": identity or {"pid": pid, "startTicks": 0, "exe": ""},
        "candidateCount": len(candidates),
        "reader": {
            "ranges": ranges_count,
            "chunksRead": chunks_read,
            "strategy": strategy,
            "readPath": resolved_read_path,
            "cacheHit": cache_hit,
            "warmStart": warm_start,
            "writesMemory": False,
            "sendsPackets": False,
        },
    }
    if timing:
        result["reader"]["timingMs"] = {
            key: round(max(0.0, float(value)), 3)
            for key, value in timing.items()
            if key in {"rootAdbMs", "probeMs", "cardParseMs"}
        }
    if status == "ok" and len(candidates) == 1:
        result["player"] = candidates[0]
    if missing_fields:
        result["missingFields"] = list(missing_fields)
    return result


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    parser.add_argument("--adb", required=True)
    parser.add_argument("--serial", default="")
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--chunk-size", type=lambda value: int(value, 0), default=0x200000)
    parser.add_argument("--max-ranges", type=int, default=0)
    parser.add_argument("--min-range", type=lambda value: int(value, 0), default=0x10000)
    parser.add_argument("--max-range", type=lambda value: int(value, 0), default=0x10000000)
    parser.add_argument("--read-timeout", type=float, default=8.0)
    parser.add_argument("--candidate-distance", type=lambda value: int(value, 0), default=0x2000)
    parser.add_argument("--cache-path", default="")
    parser.add_argument(
        "--cache-only",
        action="store_true",
        help="Use only the validated same-process read plan; never rediscover it.",
    )
    parser.add_argument(
        "--require-complete-refine-cards",
        action="store_true",
        help="Require the complete 21-card refine page before returning a snapshot.",
    )
    parser.add_argument(
        "--server",
        action="store_true",
        help="Keep one read-only probe process and rooted ADB shell for JSONL requests.",
    )
    parser.add_argument(
        "--root-broker-pipe",
        default="",
        help="Use the desktop process-wide Root shell broker instead of starting adb su.",
    )
    return parser


def probe_once(args: argparse.Namespace, runtime: ProbeRuntime | None = None) -> int:
    owns_runtime = runtime is None
    probe_runtime = runtime or ProbeRuntime(
        getattr(args, "root_broker_pipe", "")
    )
    request_sequence = probe_runtime.begin_request()
    read_path_override = ""

    def emit_snapshot(
        pid: int,
        identity: dict | None,
        status: str,
        diagnostic_code: str,
        candidates: list[dict],
        ranges_count: int,
        chunks_read: int,
        **options: object,
    ) -> dict:
        result = snapshot_result(
            pid,
            identity,
            status,
            diagnostic_code,
            candidates,
            ranges_count,
            chunks_read,
            sequence=request_sequence,
            timing=probe_runtime.timing_snapshot(),
            **options,
        )
        print(json.dumps(result, separators=(",", ":")))
        return result

    def filtered_ranges(session: AdbPageMemoryReadSession) -> list[tuple[int, int]]:
        result = read_maps(
            args.adb,
            args.serial,
            args.pid,
            args.read_timeout,
            session=session,
        )
        result = [
            (start, end)
            for start, end in result
            if args.min_range <= end - start <= args.max_range
        ]
        if args.max_ranges > 0:
            result = result[: args.max_ranges]
        return result

    def process_cache_payload(
        source: dict,
        ranges: list[tuple[int, int]],
        accessed_pages: list[int],
    ) -> dict | None:
        candidate = source.get("candidate")
        references = source.get("references")
        if not isinstance(candidate, dict) or not isinstance(references, list):
            return None
        return {
            "schema": PROBE_CACHE_SCHEMA,
            "schemaVersion": PROBE_CACHE_VERSION,
            "process": {
                "pid": int(identity.get("pid", 0)),
                "startTicks": int(identity.get("startTicks", 0)),
                "exe": str(identity.get("exe", "")),
            },
            "ranges": [[start, end] for start, end in ranges],
            "candidate": candidate,
            "references": references,
            "warmPages": accessed_pages,
        }

    try:
        session = probe_runtime.get_session(args.adb, args.serial, args.pid)
        ride_skill_name_map = load_ride_skill_name_map()

        identity = process_identity(
            args.adb,
            args.serial,
            args.pid,
            args.read_timeout,
            session=session,
        )
        if identity is None:
            emit_snapshot(
                args.pid,
                None,
                "error",
                "process_identity_unreadable",
                [],
                0,
                0,
                read_path="fallback",
            )
            return 2

        cache_path = Path(args.cache_path) if args.cache_path else None
        warm_hint_path = probe_warm_hints_path(cache_path)
        # Absolute point addresses are process-local only.  A map, identity,
        # binding or complete-card mismatch discards the plan immediately.
        point_plan = probe_runtime.get_point_plan(identity)
        ranges: list[tuple[int, int]] | None = None
        if isinstance(point_plan, dict):
            ranges = filtered_ranges(session)
            point_cache = point_plan.get("cache")
            point_hint = point_plan.get("point")
            point_snapshot = None
            point_capture: dict = {}
            if ranges and isinstance(point_cache, dict) and isinstance(point_hint, dict):
                point_cache = dict(point_cache)
                point_cache["ranges"] = [[start, end] for start, end in ranges]
                point_snapshot = read_cached_snapshot(
                    args.adb,
                    args.serial,
                    args.pid,
                    point_cache,
                    args.read_timeout,
                    ride_skill_name_map,
                    session=session,
                    point_hint=point_hint,
                    point_capture=point_capture,
                    card_timing=probe_runtime.timing,
                )
            if point_snapshot is not None:
                candidate, ranges_count, chunks_read, accessed_pages = point_snapshot
                complete_cards = has_complete_ride_refine_cards(candidate)
                if not args.require_complete_refine_cards or complete_cards:
                    if complete_cards and point_capture:
                        point_cache["warmPages"] = accessed_pages
                        probe_runtime.set_point_plan(
                            identity,
                            point_cache,
                            point_capture,
                        )
                    emit_snapshot(
                        args.pid,
                        identity,
                        "ok",
                        "ok",
                        [candidate],
                        ranges_count,
                        chunks_read,
                        strategy="luajit_gc64_point",
                        read_path="point",
                    )
                    return 0
            probe_runtime.clear_point_plan()
            read_path_override = "fallback"

        cached = load_probe_cache(cache_path, identity)
        if cached is not None:
            cached_capture: dict = {}
            cached_snapshot = read_cached_snapshot(
                args.adb,
                args.serial,
                args.pid,
                cached,
                args.read_timeout,
                ride_skill_name_map,
                session=session,
                point_capture=cached_capture,
                card_timing=probe_runtime.timing,
            )
            if cached_snapshot is not None:
                candidate, ranges_count, chunks_read, accessed_pages = cached_snapshot
                if args.require_complete_refine_cards and not has_complete_ride_refine_cards(candidate):
                    cached_snapshot = None
                    if args.cache_only:
                        emit_snapshot(
                            args.pid,
                            identity,
                            "needs_discover",
                            "cached_refine_cards_incomplete",
                            [],
                            ranges_count,
                            chunks_read,
                            strategy="luajit_gc64_cached_references",
                            cache_hit=True,
                            read_path="fallback",
                        )
                        return 3
                else:
                    cache_payload = process_cache_payload(
                        cached,
                        [(start, end) for start, end in cached["ranges"]],
                        accessed_pages,
                    )
                    if cache_payload is not None and has_complete_ride_refine_cards(candidate) and cached_capture:
                        probe_runtime.set_point_plan(identity, cache_payload, cached_capture)
                    write_probe_cache(cache_path, cache_payload)
                    write_probe_warm_hints(
                        warm_hint_path,
                        build_probe_warm_hints_from_cache(cache_payload),
                    )
                    emit_snapshot(
                        args.pid,
                        identity,
                        "ok",
                        "ok",
                        [candidate],
                        ranges_count,
                        chunks_read,
                        strategy="luajit_gc64_cached_references",
                        cache_hit=True,
                        read_path=read_path_override or "fast",
                    )
                    return 0
            read_path_override = "fallback"
            if args.cache_only:
                emit_snapshot(
                    args.pid,
                    identity,
                    "needs_discover",
                    "cached_plan_invalid",
                    [],
                    0,
                    0,
                    strategy="luajit_gc64_cached_references",
                    read_path="fallback",
                )
                return 3
        elif args.cache_only:
            emit_snapshot(
                args.pid,
                identity,
                "needs_discover",
                "cached_plan_missing",
                [],
                0,
                0,
                strategy="luajit_gc64_cached_references",
                read_path="fallback",
            )
            return 3

        if ranges is None:
            ranges = filtered_ranges(session)
        chunks = chunk_ranges(ranges, args.chunk_size)
        if not ranges or not chunks:
            emit_snapshot(
                args.pid,
                identity,
                "error",
                "anonymous_ranges_missing",
                [],
                len(ranges),
                0,
                read_path=read_path_override or "cold",
            )
            return 2

        warm_hints = load_probe_warm_hints(warm_hint_path)
        # Cross-process hints are relative only.  Materialize them into the
        # current mappings and require the same live validation as discovery.
        warm_cache = build_warm_cache_from_hints(warm_hints, ranges)
        if warm_cache is not None:
            warm_capture: dict = {}
            warm_snapshot = read_cached_snapshot(
                args.adb,
                args.serial,
                args.pid,
                warm_cache,
                args.read_timeout,
                ride_skill_name_map,
                session=session,
                point_capture=warm_capture,
                card_timing=probe_runtime.timing,
            )
            if warm_snapshot is not None:
                candidate, ranges_count, chunks_read, accessed_pages = warm_snapshot
                complete_cards = has_complete_ride_refine_cards(candidate)
                if not args.require_complete_refine_cards or complete_cards:
                    cache_payload = process_cache_payload(
                        warm_cache,
                        ranges,
                        accessed_pages,
                    )
                    if cache_payload is not None and complete_cards and warm_capture:
                        probe_runtime.set_point_plan(identity, cache_payload, warm_capture)
                    if cache_payload is not None:
                        write_probe_cache(cache_path, cache_payload)
                        write_probe_warm_hints(
                            warm_hint_path,
                            build_probe_warm_hints_from_cache(cache_payload),
                        )
                    emit_snapshot(
                        args.pid,
                        identity,
                        "ok",
                        "ok",
                        [candidate],
                        ranges_count,
                        chunks_read,
                        strategy="luajit_gc64_warm_cache",
                        warm_start=True,
                        read_path="warm",
                    )
                    return 0
            read_path_override = "fallback"

        priority_ranges = select_warm_hint_priority_ranges(warm_hints, ranges)
        cold_priority_ranges = (
            [] if priority_ranges else select_cold_priority_ranges(ranges)
        )
        discovery_plans: list[tuple[list[tuple[int, int]], bool, str]] = []
        if priority_ranges:
            discovery_plans.append((priority_ranges, True, "warm"))
        elif cold_priority_ranges:
            discovery_plans.append((cold_priority_ranges, False, "cold"))
        full_discover_path = (
            "fallback"
            if priority_ranges or cold_priority_ranges or read_path_override
            else "cold"
        )
        discovery_plans.append((ranges, False, full_discover_path))

        for plan_index, (discovery_ranges, is_warm_priority, plan_path) in enumerate(discovery_plans):
            has_fallback = plan_index + 1 < len(discovery_plans)
            discovery_chunks = chunk_ranges(discovery_ranges, args.chunk_size)
            key_objects = discover_key_objects(
                args.adb,
                args.serial,
                args.pid,
                discovery_ranges,
                args.chunk_size,
                args.read_timeout,
                session=session,
            )
            missing_fields = [name for name in FIELD_NAMES if name not in key_objects]
            if missing_fields:
                if has_fallback:
                    continue
                emit_snapshot(
                    args.pid,
                    identity,
                    "needs_discover",
                    "field_string_missing",
                    [],
                    len(ranges),
                    0,
                    missing_fields=missing_fields,
                    read_path=read_path_override or plan_path,
                )
                return 3

            references = scan_references(
                args.adb,
                args.serial,
                args.pid,
                discovery_ranges,
                key_objects,
                args.chunk_size,
                args.read_timeout,
                session=session,
            )
            candidates = build_local_candidates(references, args.candidate_distance)
            if len(candidates) == 1:
                rich_reader = BoundedPageMemoryReader(
                    args.adb,
                    args.serial,
                    args.pid,
                    ranges,
                    args.read_timeout,
                    shared_session=session,
                )
                point_capture: dict = {}
                card_parse_started = time.perf_counter()
                if all(name in key_objects for name in OPTIONAL_RIDE_SKILL_FIELD_NAMES):
                    ride_instances = read_ride_skill_instances(
                        args.adb,
                        args.serial,
                        args.pid,
                        ranges,
                        references,
                        args.read_timeout,
                        skill_name_map=ride_skill_name_map,
                        reader=rich_reader,
                    )
                    candidates[0]["rideInstances"] = ride_instances
                else:
                    candidates[0]["rideInstances"] = []
                bind_current_ride_instance(candidates[0])
                copy_current_ride_growth_rate(candidates[0])
                candidates[0]["rideRefineCards"] = read_ride_refine_cards(
                    args.adb,
                    args.serial,
                    args.pid,
                    ranges,
                    references,
                    candidates[0],
                    args.read_timeout,
                    skill_name_map=ride_skill_name_map,
                    reader=rich_reader,
                    point_capture=point_capture,
                )
                probe_runtime.add_card_parse_time(card_parse_started)
                complete_cards = has_complete_ride_refine_cards(candidates[0])
                if args.require_complete_refine_cards and not complete_cards:
                    if has_fallback:
                        continue
                    emit_snapshot(
                        args.pid,
                        identity,
                        "needs_discover",
                        "refine_cards_incomplete",
                        candidates,
                        len(ranges),
                        len(discovery_chunks),
                        strategy="luajit_gc64_table_nodes",
                        read_path=read_path_override or plan_path,
                    )
                    return 3
                cache_payload = (
                    build_probe_cache(
                        identity,
                        ranges,
                        references,
                        candidates[0],
                        args.candidate_distance,
                        warm_pages=rich_reader.accessed_pages,
                    )
                    if not args.require_complete_refine_cards or complete_cards
                    else None
                )
                if cache_payload is not None and complete_cards and point_capture:
                    probe_runtime.set_point_plan(identity, cache_payload, point_capture)
                if cache_path and cache_payload is not None:
                    write_probe_cache(cache_path, cache_payload)
                    write_probe_warm_hints(
                        warm_hint_path,
                        build_probe_warm_hints_from_cache(cache_payload),
                    )
                emit_snapshot(
                    args.pid,
                    identity,
                    "ok",
                    "ok",
                    candidates,
                    len(ranges),
                    len(discovery_chunks),
                    strategy=(
                        "luajit_gc64_warm_priority"
                        if is_warm_priority
                        else "luajit_gc64_table_nodes"
                    ),
                    warm_start=is_warm_priority,
                    read_path=read_path_override or plan_path,
                )
                return 0
            if has_fallback:
                continue
            diagnostic = "local_player_missing" if not candidates else "ambiguous_local_player"
            emit_snapshot(
                args.pid,
                identity,
                "needs_discover",
                diagnostic,
                candidates,
                len(ranges),
                len(discovery_chunks),
                read_path=read_path_override or plan_path,
            )
            return 3
    finally:
        if owns_runtime:
            probe_runtime.close()


def server_error_snapshot(pid: int, diagnostic_code: str, message: str) -> dict:
    result = snapshot_result(pid, None, "error", diagnostic_code, [], 0, 0)
    result["error"] = message
    return result


def run_server(args: argparse.Namespace) -> int:
    runtime = ProbeRuntime(getattr(args, "root_broker_pipe", ""))
    try:
        print(
            json.dumps(
                {
                    "event": "mount_status_probe_ready",
                    "schema": "mount_status_probe_server.v1",
                    "readOnly": True,
                    "actionAuthorized": False,
                    "writesMemory": False,
                    "sendsPackets": False,
                },
                separators=(",", ":"),
            ),
            flush=True,
        )
        for raw_line in sys.stdin:
            line = raw_line.strip()
            if not line:
                continue

            request_id = str(uuid.uuid4())
            request_pid = args.pid
            try:
                payload = json.loads(line)
                if not isinstance(payload, dict):
                    raise ValueError("request must be a JSON object")
                request_id = str(payload.get("id") or request_id)
                raw_pid = payload.get("pid", args.pid)
                request_pid = int(raw_pid)
                if request_pid <= 0:
                    raise ValueError("pid must be positive")

                request_args = argparse.Namespace(**vars(args))
                request_args.pid = request_pid
                request_args.cache_path = str(
                    payload.get("cachePath", args.cache_path) or ""
                )
                request_args.cache_only = bool(
                    payload.get("cacheOnly", args.cache_only)
                )
                request_args.require_complete_refine_cards = bool(
                    payload.get(
                        "requireCompleteRefineCards",
                        args.require_complete_refine_cards,
                    )
                )
                raw_timeout = payload.get("readTimeout", args.read_timeout)
                request_timeout = float(raw_timeout)
                if not math.isfinite(request_timeout) or request_timeout <= 0:
                    raise ValueError("readTimeout must be positive")
                request_args.read_timeout = min(request_timeout, 120.0)

                captured = io.StringIO()
                with contextlib.redirect_stdout(captured):
                    probe_once(request_args, runtime=runtime)
                json_lines = [
                    item.strip()
                    for item in captured.getvalue().splitlines()
                    if item.strip().startswith("{") and item.strip().endswith("}")
                ]
                if len(json_lines) != 1:
                    raise RuntimeError("probe did not return one JSON snapshot")
                result = json.loads(json_lines[0])
                if not isinstance(result, dict):
                    raise RuntimeError("probe snapshot is not a JSON object")
                result["requestId"] = request_id
            except subprocess.TimeoutExpired:
                result = server_error_snapshot(request_pid, "adb_timeout", "ADB read timed out")
                result["requestId"] = request_id
            except Exception as error:
                result = server_error_snapshot(request_pid, "probe_server_request_failed", str(error))
                result["requestId"] = request_id

            print(json.dumps(result, separators=(",", ":")), flush=True)
        return 0
    finally:
        runtime.close()


def main() -> int:
    args = build_parser().parse_args()
    if args.server:
        return run_server(args)
    return probe_once(args)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except subprocess.TimeoutExpired:
        print(json.dumps(snapshot_result(0, None, "error", "adb_timeout", [], 0, 0)))
        raise SystemExit(2)
    except Exception as error:
        print(json.dumps({
            "event": "mount_status_snapshot",
            "schema": "mount_status_snapshot.v1",
            "schemaVersion": 1,
            "readOnly": True,
            "actionAuthorized": False,
            "status": "error",
            "diagnosticCode": "probe_exception",
            "error": str(error),
        }))
        raise SystemExit(2)
