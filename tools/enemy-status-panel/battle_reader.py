"""Read-only BattleRole snapshots for the standalone status panel.

Only generic ADB, mapping, GC64 TValue/table and bounded-page reader helpers
are imported from the existing mount probe. Its mount schema is not used.
"""
from __future__ import annotations

import bisect
import base64
import copy
import importlib.util
import json
import math
import os
import queue
import re
import shlex
import shutil
import struct
import subprocess
import tempfile
import threading
from concurrent.futures import FIRST_EXCEPTION, ThreadPoolExecutor, wait
from array import array
from pathlib import Path


class _WindowlessSubprocess:
    """Keep this panel's console children hidden, including private probe calls."""

    @staticmethod
    def _options(options: dict) -> dict:
        if os.name == "nt":
            options["creationflags"] = options.get("creationflags", 0) | subprocess.CREATE_NO_WINDOW
        return options

    def run(self, *args, **kwargs):
        return subprocess.run(*args, **self._options(kwargs))

    def Popen(self, *args, **kwargs):
        return subprocess.Popen(*args, **self._options(kwargs))

    def __getattr__(self, name):
        return getattr(subprocess, name)


_panel_processes = _WindowlessSubprocess()
PROBE = Path(__file__).resolve().parents[1] / "mount-reader" / "mount_status_luajit_probe.py"
spec = importlib.util.spec_from_file_location("wpe_luajit_primitives", PROBE)
if spec is None or spec.loader is None:
    raise RuntimeError("generic LuaJIT probe helpers are unavailable")
lua = importlib.util.module_from_spec(spec)
spec.loader.exec_module(lua)
# The probe is loaded as a private module; do not change the shared source or
# the process-wide subprocess module used by other tools.
lua.subprocess = _panel_processes

MANAGER_FIELDS = ("m_BattleId", "m_CurRoundNum", "m_PlayerId", "m_RoleIns", "m_SpareRoleIns", "m_RoleIdPosDict")
ROLE_FIELDS = ("m_RoleId", "m_IsLocalPlayer", "m_IsAlive", "m_Hp", "m_Mp", "m_Speed", "m_Pos", "m_Battlefield")
ALL_FIELDS = tuple(dict.fromkeys(MANAGER_FIELDS + ROLE_FIELDS))
LJ_TUDATA = 0xFFFFFFF3  # complement of LuaJIT GC object type 12 (userdata)
GCT_UDATA = 12
CHUNK_SIZE = 0x1000000
DISCOVERY_WORKERS = 4
SCAN_PAGE_SIZE = 4096

_binding: dict | None = None
_MANAGER_CACHE_VERSION = 1
_MANAGER_CACHE_LOCK = threading.Lock()


def _manager_cache_path() -> Path:
    if os.name == "nt":
        root = Path(os.environ.get("LOCALAPPDATA") or (Path.home() / "AppData" / "Local"))
    else:
        root = Path(os.environ.get("XDG_CACHE_HOME") or (Path.home() / ".cache"))
    return root / "WPEEnemyStatusPanel" / "manager-cache-v1.json"


def _session_key_fields(session_key: tuple) -> dict:
    serial, package, pid, start_ticks, executable = session_key
    return {"serial": serial, "package": package, "pid": pid,
            "startTicks": start_ticks, "exe": executable}


def _valid_manager_cache_entry(entry: object) -> bool:
    return (
        isinstance(entry, dict)
        and isinstance(entry.get("serial"), str)
        and isinstance(entry.get("package"), str)
        and isinstance(entry.get("pid"), int) and not isinstance(entry.get("pid"), bool)
        and entry["pid"] > 0
        and isinstance(entry.get("startTicks"), int) and not isinstance(entry.get("startTicks"), bool)
        and entry["startTicks"] > 0
        and isinstance(entry.get("exe"), str) and bool(entry["exe"])
        and isinstance(entry.get("manager"), int) and not isinstance(entry.get("manager"), bool)
        and entry["manager"] > 0
    )


def _read_manager_cache_entries(path: Path) -> list[dict]:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError):
        return []
    if (not isinstance(data, dict) or type(data.get("version")) is not int
            or data.get("version") != _MANAGER_CACHE_VERSION):
        return []
    entries = data.get("entries")
    if not isinstance(entries, list):
        return []
    return [entry for entry in entries if _valid_manager_cache_entry(entry)]


def _write_manager_cache_entries(path: Path, entries: list[dict]) -> None:
    temporary_path: Path | None = None
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(
            mode="w", encoding="utf-8", newline="\n", prefix=".manager-cache-",
            suffix=".tmp", dir=path.parent, delete=False,
        ) as output:
            temporary_path = Path(output.name)
            json.dump({"version": _MANAGER_CACHE_VERSION, "entries": entries[-32:]},
                      output, ensure_ascii=True, separators=(",", ":"))
            output.flush()
            os.fsync(output.fileno())
        os.replace(temporary_path, path)
    except (OSError, TypeError, ValueError):
        pass
    finally:
        try:
            exists = temporary_path is not None and temporary_path.exists()
        except OSError:
            exists = False
        if exists and temporary_path is not None:
            try:
                temporary_path.unlink()
            except OSError:
                pass


def _load_manager_cache(session_key: tuple) -> int | None:
    try:
        path = _manager_cache_path()
        with _MANAGER_CACHE_LOCK:
            for entry in _read_manager_cache_entries(path):
                if all(entry.get(key) == value for key, value in _session_key_fields(session_key).items()):
                    return entry["manager"]
    except Exception:
        pass
    return None


def _update_manager_cache(session_key: tuple, manager_address: int | None) -> None:
    try:
        path = _manager_cache_path()
        key_fields = _session_key_fields(session_key)
        with _MANAGER_CACHE_LOCK:
            entries = [entry for entry in _read_manager_cache_entries(path)
                       if not all(entry.get(key) == value for key, value in key_fields.items())]
            if manager_address is not None:
                entry = dict(key_fields)
                entry["manager"] = manager_address
                if _valid_manager_cache_entry(entry):
                    entries.append(entry)
            _write_manager_cache_entries(path, entries)
    except Exception:
        pass


class ReadFailure(RuntimeError):
    pass


class ReadCancelled(ReadFailure):
    """A panel poll was cancelled between bounded read operations."""


class ColdDiscoveryFailure(ReadFailure):
    """A cold full-map discovery could not complete safely."""


class _ScanReadSession:
    """Persistent non-PTY ADB shell for large, read-only memory chunks."""

    READY_MARKER = "WPE_BATTLE_READ_READY"

    def __init__(self, adb: str, serial: str, pid: int) -> None:
        self.adb = adb
        self.serial = serial
        self.pid = pid
        self._closed = False
        self._request_number = 0
        command = [adb]
        if serial:
            command.extend(["-s", serial])
        command.extend(["shell", "su -c sh"])
        self.process = _panel_processes.Popen(
            command,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
        )
        self._lines: queue.Queue[bytes | None] = queue.Queue()
        self._reader = threading.Thread(target=self._pump_output, daemon=True)
        self._reader.start()
        try:
            self._write("echo " + self.READY_MARKER + "\n")
            if self._read_line(10.0).decode("ascii", "replace").strip() != self.READY_MARKER:
                raise ReadFailure("ADB 只读扫描通道未就绪")
        except BaseException:
            self.close()
            raise

    @property
    def closed(self) -> bool:
        return self._closed or self.process.poll() is not None

    def _pump_output(self) -> None:
        try:
            if self.process.stdout is None:
                return
            for line in iter(self.process.stdout.readline, b""):
                self._lines.put(line)
        finally:
            self._lines.put(None)

    def _write(self, command: str) -> None:
        if self.closed or self.process.stdin is None:
            raise ReadFailure("ADB 只读扫描通道已关闭")
        try:
            self.process.stdin.write(command.encode("ascii"))
            self.process.stdin.flush()
        except (BrokenPipeError, OSError) as error:
            self.close()
            raise ReadFailure("ADB 只读扫描通道写入失败") from error

    def _read_line(self, timeout: float) -> bytes:
        try:
            line = self._lines.get(timeout=max(0.1, timeout))
        except queue.Empty as error:
            self.close()
            raise ReadFailure("ADB 只读扫描通道读取超时") from error
        if line is None:
            self.close()
            raise ReadFailure("ADB 只读扫描通道意外关闭")
        return line

    def read(self, start: int, size: int, timeout: float) -> bytes:
        if start < 0 or size <= 0 or start % 4096 or size % 4096:
            raise ReadFailure("扫描区块未按 4 KB 对齐")
        self._request_number += 1
        marker = f"WPE_BATTLE_END_{self.pid}_{self._request_number}"
        command = (
            f"toybox dd if=/proc/{self.pid}/mem bs=4096 skip={start // 4096} "
            f"count={size // 4096} status=none 2>/dev/null | base64 -w 0; "
            f"printf '\\n{marker}\\n'\n"
        )
        self._write(command)
        encoded_parts = []
        encoded_length = 0
        response_line_count = 0
        max_encoded_length = 4 * ((size + 2) // 3)
        max_response_lines = max_encoded_length + 1
        while True:
            line = self._read_line(max(30.0, timeout)).rstrip(b"\r\n")
            if line.decode("ascii", "replace") == marker:
                break
            response_line_count += 1
            if response_line_count > max_response_lines:
                self.close()
                raise ReadFailure("ADB 只读扫描通道响应边界失配")
            encoded_length += len(line)
            if encoded_length > max_encoded_length:
                self.close()
                raise ReadFailure(
                    f"ADB 只读扫描通道响应长度失配：编码长度超过预期上限 {max_encoded_length} 字节"
                )
            encoded_parts.append(line)
        if encoded_length != max_encoded_length:
            self.close()
            raise ReadFailure(
                f"ADB 只读扫描通道响应长度失配：编码长度 {encoded_length}，预期 {max_encoded_length} 字节"
            )
        encoded = b"".join(encoded_parts)
        try:
            blob = base64.b64decode(encoded, validate=True)
        except (ValueError, TypeError) as error:
            self.close()
            raise ReadFailure("ADB 只读扫描通道返回了无效数据") from error
        if len(blob) != size:
            self.close()
            raise ReadFailure(
                f"ADB 只读扫描通道返回了短读：解码长度 {len(blob)}，预期 {size} 字节"
            )
        return blob

    def close(self) -> None:
        if self._closed:
            return
        self._closed = True
        if self.process.poll() is None:
            try:
                if self.process.stdin is not None:
                    self.process.stdin.close()
                self.process.wait(timeout=1.0)
            except (OSError, subprocess.TimeoutExpired):
                self.process.terminate()
                try:
                    self.process.wait(timeout=1.0)
                except subprocess.TimeoutExpired:
                    self.process.kill()


def find_adb() -> str:
    found = shutil.which("adb")
    if found:
        return found
    roots = [os.environ.get("ANDROID_SDK_ROOT", ""), os.environ.get("ANDROID_HOME", ""),
             str(Path.home() / "AppData/Local/Android/Sdk"), r"C:\Program Files\ldplayer\LDPlayer9"]
    for root in roots:
        if root:
            for path in (Path(root) / "platform-tools/adb.exe", Path(root) / "adb.exe"):
                if path.is_file():
                    return str(path)
    raise ReadFailure("找不到 adb；请加入 PATH 或配置 Android SDK")


def _adb(adb: str, serial: str, *args: str, timeout: float = 10) -> bytes:
    result = _panel_processes.run([adb] + (["-s", serial] if serial else []) + list(args),
                            capture_output=True, timeout=timeout, check=False)
    if result.returncode:
        raise ReadFailure("ADB 只读请求失败")
    return result.stdout


def _resolve_serial(adb: str, serial: str = "") -> str:
    if serial:
        return serial
    devices = [line.split()[0] for line in _adb(adb, "", "devices").decode("ascii", "replace").splitlines()[1:]
               if len(line.split()) >= 2 and line.split()[1] == "device"]
    if len(devices) != 1:
        raise ReadFailure(f"ADB 在线设备数为 {len(devices)}，必须唯一才能自动连接")
    return devices[0]


def _root_adb(adb: str, serial: str, command: str, timeout: float = 10) -> bytes:
    # adb shell joins arguments into a remote command string. Quote the full
    # su -c payload so Android su receives it as one command argument.
    return _adb(adb, serial, "shell", f"su -c {shlex.quote(command)}", timeout=timeout)


def _pid(adb: str, serial: str, package: str) -> int:
    output = _adb(adb, serial, "shell", "pidof", package).decode("ascii", "replace").split()
    pids = sorted({int(x) for x in output if x.isdigit() and int(x) > 0})
    if len(pids) != 1:
        raise ReadFailure("未运行或 PID 不唯一")
    return pids[0]


def _identity(adb: str, serial: str, pid: int) -> dict:
    stat = _root_adb(adb, serial, f"cat /proc/{pid}/stat").decode("utf-8", "replace").strip()
    close = stat.rfind(")")
    if close < 0:
        raise ReadFailure("无法读取进程身份")
    fields = stat[close + 2:].split()
    if len(fields) <= 19 or not fields[19].isdigit():
        raise ReadFailure("无法读取进程启动时间")
    exe = _root_adb(adb, serial, f"readlink /proc/{pid}/exe").decode("utf-8", "replace").strip()
    if not exe:
        raise ReadFailure("无法读取进程 executable")
    return {"pid": pid, "startTicks": int(fields[19]), "exe": exe}


def _read(reader, address: int, size: int) -> bytes:
    blob = reader.read(address, size)
    if len(blob) != size:
        raise ReadFailure("当前采样发生短读，整帧已丢弃")
    return blob


def _unwrap(reader, value: dict | None, depth: int = 0):
    if not isinstance(value, dict) or depth > 2:
        return None
    if value.get("kind") in ("i32", "f64", "boolean"):
        return value.get("value")
    if value.get("kind") != "tagged":
        return None
    addr = lua.tagged_pointer_from_value(value, lua.LJ_TTAB)
    if not addr:
        return None
    table = lua.read_gc_table(reader, addr)
    cells = lua.read_gc_table_array(reader, table) if table else None
    if cells and len(cells) == 3 and cells[0].get("kind") == "nil" and cells[2].get("kind") == "nil":
        return _unwrap(reader, cells[1], depth + 1)
    return None


def _nodes(reader, address: int) -> list[dict]:
    table = lua.read_gc_table(reader, address)
    result = lua.read_gc_table_nodes(reader, table) if table else None
    if result is None:
        raise ReadFailure("LuaJIT 表结构不完整")
    return result


def _field_map(reader, address: int) -> dict:
    return {entry["key"]: entry["value"] for entry in _nodes(reader, address)
            if isinstance(entry.get("key"), str)}


def _numeric(value) -> int | float | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
        return None
    return value


def _battle_id(reader, value) -> int | float | str | None:
    unwrapped = _unwrap(reader, value)
    numeric = _numeric(unwrapped)
    if numeric is not None:
        return numeric
    if isinstance(value, dict) and value.get("kind") == "tagged":
        address = lua.tagged_pointer_from_value(value, lua.LJ_TSTR)
        if address:
            text = lua.read_gcstr(reader, address)
            if text:
                return int(text) if text.isascii() and text.isdigit() else text
    return None


def _release_scan_chunk(reader) -> None:
    # Full-map discovery can cover gigabytes. Keep only the current chunk's
    # pages instead of retaining every page and address visited by the pass.
    reader.pages.clear()
    reader.accessed_pages.clear()


def _raise_if_cancelled(stop_event: threading.Event | None) -> None:
    if stop_event is not None and stop_event.is_set():
        raise ReadCancelled("读取已取消")


def _validate_cold_snapshot(reader) -> None:
    """Fail closed if process identity or anonymous maps changed mid-discovery."""
    snapshot_identity = getattr(reader, "_snapshot_identity", None)
    snapshot_ranges = getattr(reader, "_snapshot_ranges", None)
    if snapshot_identity is None or snapshot_ranges is None:
        raise ColdDiscoveryFailure("冷扫描快照无法验证")
    try:
        pid = _pid(reader.adb_path, reader.serial, reader.package)
        identity = _identity(reader.adb_path, reader.serial, pid)
        maps = _root_adb(reader.adb_path, reader.serial, f"cat /proc/{pid}/maps")
        ranges = lua.parse_maps(maps)
    except Exception as error:
        raise ColdDiscoveryFailure("冷扫描快照无法验证") from error
    if identity != snapshot_identity:
        raise ColdDiscoveryFailure("冷扫描进程身份已变化")
    if ranges != snapshot_ranges:
        raise ColdDiscoveryFailure("冷扫描匿名映射已变化")


def _read_scan_chunk(reader, start: int, size: int, stage: str, chunk_ordinal: int) -> bytes:
    _raise_if_cancelled(getattr(reader, "_stop_event", None))
    if start < 0 or size <= 0 or start % 4096 or size % 4096:
        raise ReadFailure("扫描区块未按 4 KB 对齐")
    # Discovery reads aligned 16 MB spans over a persistent non-PTY shell.
    session = getattr(reader, "_scan_session", None)
    if session is None or session.closed:
        try:
            session = _ScanReadSession(reader.adb_path, reader.serial, reader.pid)
        except ReadFailure:
            if getattr(reader, "_cold_discovery", False):
                _validate_cold_snapshot(reader)
            raise
        reader._scan_session = session
    try:
        blob = session.read(start, size, reader.timeout)
        if len(blob) != size:
            raise ReadFailure("ADB 只读扫描通道返回了短读")
        return blob
    except ReadCancelled:
        session.close()
        reader._scan_session = None
        raise
    except ReadFailure as full_read_error:
        full_read_category = str(full_read_error)
        session.close()
        reader._scan_session = None
        if getattr(reader, "_cold_discovery", False):
            _validate_cold_snapshot(reader)

    # Rebuild once, then retry the complete range as aligned 2 MB reads.
    try:
        session = _ScanReadSession(reader.adb_path, reader.serial, reader.pid)
    except ReadFailure:
        if getattr(reader, "_cold_discovery", False):
            _validate_cold_snapshot(reader)
        raise
    reader._scan_session = session
    piece_size = 0x200000
    pieces = []
    piece_ordinal = 0
    try:
        for piece_ordinal, piece_start in enumerate(range(start, start + size, piece_size), 1):
            _raise_if_cancelled(getattr(reader, "_stop_event", None))
            piece_length = min(piece_size, start + size - piece_start)
            if piece_start % 4096 or piece_length % 4096:
                raise ReadFailure("扫描子区块未按 4 KB 对齐")
            piece = session.read(piece_start, piece_length, reader.timeout)
            if len(piece) != piece_length:
                raise ReadFailure("ADB 只读扫描子区块返回了短读")
            pieces.append(piece)
        return b"".join(pieces)
    except ReadCancelled:
        session.close()
        reader._scan_session = None
        raise
    except ReadFailure as piece_error:
        session.close()
        reader._scan_session = None
        if getattr(reader, "_cold_discovery", False):
            _validate_cold_snapshot(reader)
        raise ReadFailure(
            f"扫描失败 stage={stage} chunk={chunk_ordinal} "
            f"full_read={full_read_category} piece={piece_ordinal} piece_read={piece_error}"
        ) from piece_error


def _index_table_headers(
    reader,
    blob: bytes,
    blob_start: int,
    minimum_address: int,
    index: array,
    owner_end: int | None = None,
) -> None:
    marker = bytes((lua.GCT_TAB,))
    # The marker is byte 9 of the header, so only one residue class modulo
    # eight can produce an aligned header address. Search that strided view
    # and translate each match back to its original blob offset.
    residue = (9 - blob_start) % 8
    strided_blob = blob[residue::8]
    cursor = 0
    while True:
        strided_offset = strided_blob.find(marker, cursor)
        if strided_offset < 0:
            return
        cursor = strided_offset + 1
        marker_offset = residue + strided_offset * 8
        header_offset = marker_offset - 9
        header_address = blob_start + header_offset
        if (
            header_offset < 0
            or header_address < minimum_address
            or (owner_end is not None and header_address >= owner_end)
            or header_address % 8
        ):
            continue
        header = blob[header_offset : header_offset + lua.GCTAB_SIZE]
        if len(header) != lua.GCTAB_SIZE or header[9] != lua.GCT_TAB:
            continue
        node_address = struct.unpack_from("<Q", header, 40)[0]
        array_address = struct.unpack_from("<Q", header, 16)[0]
        array_size = struct.unpack_from("<I", header, 48)[0]
        hash_mask = struct.unpack_from("<I", header, 52)[0]
        if (
            node_address == 0
            or node_address % 8
            or hash_mask > lua.MAX_HASH_MASK
            or ((hash_mask + 1) & hash_mask) != 0
            or array_size > lua.MAX_TABLE_ARRAY_SIZE
        ):
            continue
        node_bytes = (hash_mask + 1) * lua.NODE_STRIDE
        array_bytes = array_size * 8
        if not lua.address_in_ranges(node_address, node_bytes, reader.ranges):
            continue
        if array_bytes and (
            array_address == 0
            or not lua.address_in_ranges(array_address, array_bytes, reader.ranges)
        ):
            continue
        index.extend((header_address, node_address, node_address + node_bytes))


def _discovery_chunks(reader, needles: dict[str, bytes]) -> list[tuple[int, int, int, int, int]]:
    """Describe core chunks with a page-aligned overlap confined to each map."""
    max_string_span = lua.GCSTR_SIZE + max(map(len, needles.values()))
    overlap = max(lua.GCTAB_SIZE, max_string_span)
    descriptors: list[tuple[int, int, int, int, int]] = []
    ordinal = 0
    for map_start, map_end in reader.ranges:
        if map_start % SCAN_PAGE_SIZE or map_end % SCAN_PAGE_SIZE:
            raise ColdDiscoveryFailure("匿名映射边界未按 4 KB 对齐，拒绝进行冷扫描")
        for core_start, core_size in lua.chunk_ranges([(map_start, map_end)], CHUNK_SIZE):
            core_end = core_start + core_size
            window_end = min(
                map_end,
                (core_end + overlap + SCAN_PAGE_SIZE - 1) // SCAN_PAGE_SIZE * SCAN_PAGE_SIZE,
            )
            read_size = window_end - core_start
            if read_size <= 0 or read_size % SCAN_PAGE_SIZE:
                raise ColdDiscoveryFailure("冷扫描重叠区间未按 4 KB 对齐，拒绝读取")
            ordinal += 1
            descriptors.append((ordinal, map_end, core_start, core_end, read_size))
    return descriptors


def _scan_discovery_bucket(reader, descriptors, needles: dict[str, bytes], stop_event: threading.Event,
                           cancel_event: threading.Event | None = None):
    """Scan one bounded worker bucket using an independent reader/session/cache."""
    worker = copy.copy(reader)
    worker.pages = {}
    worker.accessed_pages = set()
    worker._scan_session = None
    worker._cold_discovery = True
    worker._stop_event = cancel_event
    local_keys = {name: [] for name in ALL_FIELDS}
    local_headers = array("Q")
    try:
        for chunk_ordinal, map_end, core_start, core_end, read_size in descriptors:
            if stop_event.is_set() or (cancel_event is not None and cancel_event.is_set()):
                break
            try:
                blob = _read_scan_chunk(worker, core_start, read_size, "key-discovery", chunk_ordinal)
                _index_table_headers(
                    worker, blob, core_start, core_start, local_headers, owner_end=core_end
                )
                for name, needle in needles.items():
                    for offset in lua.find_all(blob, needle, 256):
                        object_address = core_start + offset - lua.GCSTR_SIZE
                        if not core_start <= object_address < core_end:
                            continue
                        if lua.verify_gcstr(blob, offset, needle):
                            local_keys[name].append(object_address)
            finally:
                _release_scan_chunk(worker)
        return local_keys, local_headers
    finally:
        session = getattr(worker, "_scan_session", None)
        if session is not None:
            session.close()
            worker._scan_session = None


def _find_discovery_indexes(reader, cancel_event: threading.Event | None = None) -> tuple[dict[str, list[int]], array]:
    """Scan every anonymous map with bounded independent readers and overlap."""
    result = {name: [] for name in ALL_FIELDS}
    needles = {name: name.encode("ascii") for name in ALL_FIELDS}
    descriptors = _discovery_chunks(reader, needles)
    table_headers = array("Q")
    if not descriptors:
        raise ReadFailure("没有可读取的匿名扫描区块")

    worker_count = min(DISCOVERY_WORKERS, len(descriptors))
    buckets = [descriptors[index::worker_count] for index in range(worker_count)]
    stop_event = threading.Event()
    with ThreadPoolExecutor(max_workers=worker_count, thread_name_prefix="wpe-battle-scan") as executor:
        futures = [executor.submit(_scan_discovery_bucket, reader, bucket, needles, stop_event, cancel_event)
                   for bucket in buckets]
        completed, _ = wait(futures, return_when=FIRST_EXCEPTION)
        if cancel_event is not None and cancel_event.is_set():
            stop_event.set()
            wait(futures)
            raise ReadCancelled("读取已取消")
        failure = next(
            (future.exception() for future in completed if not future.cancelled() and future.exception()),
            None,
        )
        if failure is not None:
            stop_event.set()
            wait(futures)
            raise failure
        worker_results = [future.result() for future in futures]

    for worker_keys, worker_headers in worker_results:
        for name in ALL_FIELDS:
            result[name].extend(worker_keys[name])
        table_headers.extend(worker_headers)
    missing = [name for name in MANAGER_FIELDS if not result[name]]
    if missing:
        raise ReadFailure("管理器键对象尚未全部发现：" + ", ".join(missing))
    return result, table_headers


def _node_scan_intervals(reader, table_headers: array) -> list[tuple[int, int]]:
    """Union page-aligned node spans, never joining separate maps."""
    intervals: list[tuple[int, int]] = []
    page = 4096
    spans = sorted((table_headers[i + 1], table_headers[i + 2])
                   for i in range(0, len(table_headers), 3))
    starts = [span[0] for span in spans]
    for map_start, map_end in reader.ranges:
        if map_start % page or map_end % page:
            raise ColdDiscoveryFailure("匿名映射边界未按 4 KB 对齐，拒绝进行节点扫描")
        pieces = []
        first = bisect.bisect_left(starts, map_start)
        limit = bisect.bisect_left(starts, map_end)
        for span_index in range(first, limit):
            start, end = spans[span_index]
            aligned_start = max(map_start, start - start % page)
            aligned_end = min(map_end, (end + page - 1) // page * page)
            if aligned_start % page or aligned_end % page:
                raise ColdDiscoveryFailure("节点扫描区间未按 4 KB 对齐，拒绝读取")
            if aligned_end > aligned_start:
                pieces.append((aligned_start, aligned_end))
        pieces.sort()
        for start, end in pieces:
            if intervals and intervals[-1][1] == start and intervals[-1][0] >= map_start:
                intervals[-1] = (intervals[-1][0], end)
            elif intervals and intervals[-1][0] >= map_start and start <= intervals[-1][1]:
                intervals[-1] = (intervals[-1][0], max(intervals[-1][1], end))
            else:
                intervals.append((start, end))
    return intervals


def _cold_discover(reader, cancel_event: threading.Event | None = None) -> tuple[int, dict]:
    reader._cold_discovery = True
    try:
        _raise_if_cancelled(cancel_event)
        key_objects, table_headers = _find_discovery_indexes(reader, cancel_event)
        _raise_if_cancelled(cancel_event)
        _validate_cold_snapshot(reader)
        manager = _find_manager(reader, key_objects, table_headers, cancel_event)
        _raise_if_cancelled(cancel_event)
        _validate_cold_snapshot(reader)
        return manager
    except ReadCancelled:
        raise
    except ReadFailure as error:
        if isinstance(error, ColdDiscoveryFailure):
            raise
        detail = _safe_scan_failure_detail(str(error))
        raise ColdDiscoveryFailure(detail or "冷扫描发现失败") from error
    finally:
        reader._cold_discovery = False


_SAFE_SCAN_READ_ERROR = re.compile(
    r"(?:"
    r"ADB 只读扫描通道(?:未就绪|已关闭|写入失败|读取超时|意外关闭|响应边界失配|返回了无效数据|返回了短读)"
    r"|ADB 只读扫描通道响应长度失配：编码长度(?:超过预期上限 \d+ 字节| \d+，预期 \d+ 字节)"
    r"|ADB 只读扫描通道返回了短读：解码长度 \d+，预期 \d+ 字节"
    r"|扫描区块未按 4 KB 对齐|扫描子区块未按 4 KB 对齐"
    r")"
)
_SAFE_SCAN_FAILURE = re.compile(
    r"扫描失败 stage=(key-discovery|manager-discovery) chunk=(\d+) "
    r"full_read=(.+) piece=(\d+) piece_read=(.+)"
)


def _safe_scan_failure_detail(message: str) -> str | None:
    """Keep only fixed scan diagnostics and numeric read lengths for the UI."""
    match = _SAFE_SCAN_FAILURE.fullmatch(message)
    if match is None:
        return None
    stage, chunk, full_read, piece, piece_read = match.groups()
    if not _SAFE_SCAN_READ_ERROR.fullmatch(full_read) or not _SAFE_SCAN_READ_ERROR.fullmatch(piece_read):
        return None
    return message


def _find_manager(reader, key_objects: dict[str, list[int]], table_headers: array,
                  cancel_event: threading.Event | None = None) -> tuple[int, dict]:
    # The first complete map pass collected every key and plausible header.
    # Search anchors only in the union of validated node spans; keep each
    # header's original span entry so ownership and uniqueness remain intact.
    role_ins_needles = {lua.gc64_tagged_pointer(a, 0xFFFFFFFB).to_bytes(8, "little") for a in key_objects["m_RoleIns"]}
    candidate_nodes: set[int] = set()
    intervals = _node_scan_intervals(reader, table_headers)
    chunk_ordinal = 0
    for interval_start, interval_end in intervals:
        previous_tail = b""
        previous_end: int | None = None
        for start in range(interval_start, interval_end, CHUNK_SIZE):
            _raise_if_cancelled(cancel_event)
            size = min(CHUNK_SIZE, interval_end - start)
            if size % 4096:
                raise ReadFailure("节点扫描区块未按 4 KB 对齐")
            chunk_ordinal += 1
            try:
                blob = _read_scan_chunk(reader, start, size, "manager-discovery", chunk_ordinal)
                prefix = previous_tail if previous_end == start else b""
                scan_blob = prefix + blob
                scan_start = start - len(prefix)
                for needle in role_ins_needles:
                    cursor = 0
                    while True:
                        off = scan_blob.find(needle, cursor)
                        if off < 0:
                            break
                        cursor = off + 1
                        owner_node = scan_start + off - 8
                        if owner_node % 8 == 0 and lua.address_in_ranges(
                            owner_node, lua.NODE_STRIDE, reader.ranges
                        ):
                            candidate_nodes.add(owner_node)
                previous_tail = blob[-7:]
                previous_end = start + size
            finally:
                _release_scan_chunk(reader)
    sorted_nodes = sorted(candidate_nodes)
    possible = {
        table_headers[index]
        for index in range(0, len(table_headers), 3)
        if (node_index := bisect.bisect_left(sorted_nodes, table_headers[index + 1])) < len(sorted_nodes)
        and sorted_nodes[node_index] < table_headers[index + 2]
    }
    matches = []
    for address in possible:
        _raise_if_cancelled(cancel_event)
        try:
            fields = _field_map(reader, address)
        except ReadCancelled:
            raise
        except ReadFailure:
            continue
        if set(MANAGER_FIELDS).issubset(fields):
            matches.append((address, fields))
    if len(matches) != 1:
        raise ReadFailure(f"战斗管理器匹配数为 {len(matches)}，未能唯一绑定")
    return matches[0]


def _userdata_addresses(reader, role_table: int) -> list[int]:
    table = lua.read_gc_table(reader, role_table)
    if table is None:
        raise ReadFailure("m_RoleIns 不是有效 LuaJIT 表")
    nodes = lua.read_gc_table_nodes(reader, table)
    values = lua.read_gc_table_array(reader, table)
    if nodes is None or values is None:
        raise ReadFailure("m_RoleIns 的 array/hash-node 读取不完整")
    addresses: set[int] = set()
    for index, value in enumerate(values, 1):
        address = lua.tagged_pointer_from_value(value, LJ_TUDATA)
        if address:
            addresses.add(address)
    # GC64 hash keys are primitive TValue values; retain their integer role
    # position directly and never pass them through _unwrap.
    for node in nodes:
        key = node.get("key")
        if isinstance(key, int) and not isinstance(key, bool) and key > 0:
            address = lua.tagged_pointer_from_value(node.get("value", {}), LJ_TUDATA)
            if address:
                addresses.add(address)
        elif key is not None and not isinstance(key, str):
            raise ReadFailure("m_RoleIns 含无法识别的 hash key")
    return sorted(addresses)


def _userdata_env(reader, userdata: int) -> int:
    header = _read(reader, userdata, 24)
    if header[9] != GCT_UDATA:
        raise ReadFailure("m_RoleIns 项的 GC 对象不是 userdata")
    # This client stores the environment GCRef as a 64-bit absolute pointer.
    env = struct.unpack("<Q", header[16:24])[0]
    if not lua.address_in_ranges(env, lua.GCTAB_SIZE, reader.ranges) or lua.read_gc_table(reader, env) is None:
        raise ReadFailure("userdata+16 GCRef 未指向有效环境表")
    return env


def _role_position_map(reader, position_table: int) -> dict[int, int]:
    mapping: dict[int, int] = {}
    roles_by_position: dict[int, int] = {}
    for node in _nodes(reader, position_table):
        value_entry = node.get("value")
        if isinstance(value_entry, dict) and value_entry.get("kind") == "nil":
            continue
        key = lua.numeric_table_key(node.get("key"))
        value = _unwrap(reader, value_entry)
        pos = lua.numeric_table_key(value)
        if key is None or pos is None or key <= 0 or pos <= 0:
            raise ReadFailure("m_RoleIdPosDict 含无法解释的键或 position")
        if key in mapping and mapping[key] != pos:
            raise ReadFailure("角色 ID 对应多个 position")
        if pos in roles_by_position and roles_by_position[pos] != key:
            raise ReadFailure("多个角色 ID 映射到同一 raw position ID")
        mapping[key] = pos
        roles_by_position[pos] = key
    return mapping


def _position_mapping(reader, position_table: int) -> dict[int, int]:
    position_by_role = _role_position_map(reader, position_table)
    return position_by_role


def _userdata_index(reader, role_table: int) -> dict[int, int]:
    table = lua.read_gc_table(reader, role_table)
    if table is None:
        raise ReadFailure("m_RoleIns 不是有效 LuaJIT 表")
    nodes = lua.read_gc_table_nodes(reader, table)
    values = lua.read_gc_table_array(reader, table)
    if nodes is None or values is None:
        raise ReadFailure("m_RoleIns 的 array/hash-node 读取不完整")
    by_position: dict[int, int] = {}
    # LuaJIT's array includes its index-zero cell, unlike a Lua ipairs list.
    for position, value in enumerate(values):
        address = lua.tagged_pointer_from_value(value, LJ_TUDATA)
        if address:
            by_position[position] = address
    for node in nodes:
        position = lua.numeric_table_key(node.get("key"))
        value = node.get("value", {})
        if position is not None and position > 0:
            address = lua.tagged_pointer_from_value(value, LJ_TUDATA)
            if address:
                if position in by_position and by_position[position] != address:
                    raise ReadFailure("m_RoleIns 的 position 有重复 userdata")
                by_position[position] = address
    return by_position


def _read_role(reader, userdata: int, position: int, expected_role_id: int,
               local_id: int | None = None, manager_address: int | None = None) -> dict:
        env = _userdata_env(reader, userdata)
        fields = _field_map(reader, env)
        required = ("m_RoleId", "m_IsLocalPlayer", "m_IsAlive", "m_Pos", "m_Battlefield")
        if not set(required).issubset(fields):
            raise ReadFailure("已映射 BattleRole 缺少身份字段")
        values = {name: _unwrap(reader, fields[name]) for name in required}
        role_id = lua.numeric_table_key(values["m_RoleId"])
        alive, local_role = values["m_IsAlive"], values["m_IsLocalPlayer"]
        role_pos = lua.numeric_table_key(values["m_Pos"])
        battlefield = lua.tagged_pointer_from_value(fields["m_Battlefield"], lua.LJ_TTAB)
        if (role_id != expected_role_id or role_pos != position or
                not isinstance(alive, bool) or not isinstance(local_role, bool) or
                battlefield != manager_address or local_role != (role_id == local_id)):
            raise ReadFailure(f"已映射 position {position} 的角色身份不匹配")
        resource_fields = _field_map(reader, env)
        numeric = {key: _numeric(_unwrap(reader, resource_fields.get(key))) for key in ("m_Hp", "m_Mp", "m_Speed")}
        if any(value is None for value in numeric.values()):
            raise ReadFailure(f"position {position} 的 HP/MP/Speed 无效")
        return {"position": position, "roleId": role_id, "local": role_id == local_id,
                "alive": alive, "hp": numeric["m_Hp"], "mp": numeric["m_Mp"],
                "speed": numeric["m_Speed"], "name": f"单位名未确认 · RoleId {role_id}"}


def _read_roles(reader, role_table: int, position_table: int, manager_address: int,
                local_id: int) -> tuple[dict[int, dict], list[int], dict[int, dict], list[int], int]:
    position_by_role = _position_mapping(reader, position_table)
    userdatas = _userdata_index(reader, role_table)
    local_positions = [pos for role_id, pos in position_by_role.items() if role_id == local_id]
    if len(local_positions) != 1:
        raise ReadFailure("本地角色没有唯一 position 映射")
    local_position = local_positions[0]
    if local_position in range(1, 6):
        own_main, own_pet = range(1, 6), range(101, 106)
        enemy_main, enemy_pet = range(10001, 10006), range(10101, 10106)
    elif local_position in range(10001, 10006):
        own_main, own_pet = range(10001, 10006), range(10101, 10106)
        enemy_main, enemy_pet = range(1, 6), range(101, 106)
    else:
        raise ReadFailure("本地角色不在已确认的主单位 position 族，拒绝推断对方")
    position_ids = list(enemy_main) + list(enemy_pet)
    own_position_ids = list(own_main) + list(own_pet)
    slots: dict[int, dict] = {}
    for enemy_pos in position_ids:
        expected = next((rid for rid, pos in position_by_role.items() if pos == enemy_pos), None)
        if expected is None:
            continue
        userdata = userdatas.get(enemy_pos)
        if userdata is None:
            raise ReadFailure(f"m_RoleIdPosDict 映射的对方 position {enemy_pos} 无 userdata")
        role = _read_role(reader, userdata, enemy_pos, expected, local_id, manager_address)
        role["side"] = "对方"
        slots[enemy_pos] = role
    own_slots: dict[int, dict] = {}
    for own_pos in own_position_ids:
        expected = next((rid for rid, pos in position_by_role.items() if pos == own_pos), None)
        if expected is None:
            continue
        userdata = userdatas.get(own_pos)
        if userdata is None:
            raise ReadFailure(f"m_RoleIdPosDict 映射的本方 position {own_pos} 无 userdata")
        role = _read_role(reader, userdata, own_pos, expected, local_id, manager_address)
        role["side"] = "本方"
        own_slots[own_pos] = role
    return slots, position_ids, own_slots, own_position_ids, local_position


def _manager_values(reader, manager_address: int) -> tuple[dict, int | float | str, int | float, int, int]:
    fields = _field_map(reader, manager_address)
    if not set(MANAGER_FIELDS).issubset(fields):
        raise ReadFailure("缓存 manager 不再包含完整业务字段")
    battle_id = _battle_id(reader, fields["m_BattleId"])
    round_number = _numeric(_unwrap(reader, fields["m_CurRoundNum"]))
    roles = lua.tagged_pointer_from_value(fields["m_RoleIns"], lua.LJ_TTAB)
    positions = lua.tagged_pointer_from_value(fields["m_RoleIdPosDict"], lua.LJ_TTAB)
    if battle_id is None or round_number is None or not roles or not positions:
        raise ReadFailure("BattleId/round/角色表/位置字典未绑定")
    return fields, battle_id, round_number, roles, positions


def sample(serial: str = "", package: str = "com.gdoo.xxxy2026",
           stop_event: threading.Event | None = None) -> dict:
    """Take one fresh-reader frame bound to current process and battle round."""
    global _binding
    _raise_if_cancelled(stop_event)
    adb = find_adb()
    serial = _resolve_serial(adb, serial)
    pid = _pid(adb, serial, package)
    identity = _identity(adb, serial, pid)
    session_key = (serial, package, identity["pid"], identity["startTicks"], identity["exe"])
    maps = _root_adb(adb, serial, f"cat /proc/{pid}/maps")
    ranges = lua.parse_maps(maps)
    if not ranges:
        raise ReadFailure("没有可读的匿名进程映射")
    # New reader each frame: no memory page cache crosses sample boundaries.
    reader = lua.BoundedPageMemoryReader(adb, serial, pid, ranges, 8)
    reader.adb_path = adb
    reader.serial = serial
    reader.package = package
    reader._snapshot_identity = identity
    reader._snapshot_ranges = ranges
    reader._stop_event = stop_event
    try:
        _raise_if_cancelled(stop_event)
        cached_binding = _binding if _binding and _binding.get("identity") == session_key else None
        manager_address = cached_binding.get("manager") if cached_binding else _load_manager_cache(session_key)
        cached_candidate = manager_address is not None
        if manager_address is None:
            _binding = None
            manager_address, _ = _cold_discover(reader, stop_event)
        try:
            fields, battle_id, round_number, role_table, position_table = _manager_values(reader, manager_address)
        except ReadCancelled:
            raise
        except ReadFailure:
            _raise_if_cancelled(stop_event)
            if not cached_candidate:
                raise
            _binding = None
            _update_manager_cache(session_key, None)
            manager_address, _ = _cold_discover(reader, stop_event)
            fields, battle_id, round_number, role_table, position_table = _manager_values(reader, manager_address)
        _raise_if_cancelled(stop_event)
        if battle_id in (0, "0"):
            if _identity(adb, serial, pid) != identity:
                raise ReadFailure("采样期间 PID/startTicks/executable 发生变化")
            _raise_if_cancelled(stop_event)
            _binding = {"identity": session_key, "manager": manager_address, "battleId": battle_id}
            _update_manager_cache(session_key, manager_address)
            return {"state": "not_in_battle", "identity": identity, "battleId": battle_id,
                    "round": round_number, "message": "BattleId 为 0，未进入战斗"}
        # The in-memory address is a candidate on the next frame and will be
        # validated again by _manager_values before any role fields are used.
        player_id = lua.numeric_table_key(_unwrap(reader, fields["m_PlayerId"]))
        if player_id is None or player_id <= 0:
            raise ReadFailure("manager.m_PlayerId 无效")
        _raise_if_cancelled(stop_event)
        position_map_before = _position_mapping(reader, position_table)
        _raise_if_cancelled(stop_event)
        slots, position_ids, own_slots, own_position_ids, local_position = _read_roles(reader, role_table, position_table, manager_address, player_id)
        # Re-read round/battle from the same manager and identity after all role fields.
        # The bounded reader caches pages within a sample; drop them so this
        # guard observes device memory again rather than the pre-read snapshot.
        reader.pages.clear()
        reader.accessed_pages.clear()
        _raise_if_cancelled(stop_event)
        _, battle_after, round_after, role_after, pos_after = _manager_values(reader, manager_address)
        _raise_if_cancelled(stop_event)
        position_map_after = _position_mapping(reader, pos_after)
        _raise_if_cancelled(stop_event)
        if _identity(adb, serial, pid) != identity:
            raise ReadFailure("采样期间 PID/startTicks/executable 发生变化")
        if (battle_after != battle_id or round_after != round_number or role_after != role_table or
                pos_after != position_table or position_map_after != position_map_before):
            raise ReadFailure("采样期间 BattleId/round/manager 表发生变化，整帧丢弃")
        _raise_if_cancelled(stop_event)
        _binding = {"identity": session_key, "manager": manager_address, "battleId": battle_id}
        _update_manager_cache(session_key, manager_address)
        return {"state": "ok", "identity": identity, "battleId": battle_id, "round": round_number,
                "slots": slots, "positionIds": position_ids,
                "ownSlots": own_slots, "ownPositionIds": own_position_ids, "localPosition": local_position,
                "message": "本方与对方主单位及宠物各十槽；单位名未确认"}
    finally:
        reader.close()
        scan_session = getattr(reader, "_scan_session", None)
        if scan_session is not None:
            scan_session.close()
