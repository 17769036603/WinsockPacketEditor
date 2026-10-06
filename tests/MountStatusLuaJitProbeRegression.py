"""Offline regression checks for the read-only LuaJIT mount probe."""

from __future__ import annotations

import base64
import contextlib
import importlib.util
import io
import json
import struct
import tempfile
from pathlib import Path
from unittest import mock


def load_probe_module():
    repository_root = Path(__file__).resolve().parents[1]
    probe_path = repository_root / "tools" / "mount-reader" / "mount_status_luajit_probe.py"
    spec = importlib.util.spec_from_file_location("mount_status_probe", probe_path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load probe module: {probe_path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def build_references(probe, gx: dict, gy: dict) -> list[dict]:
    values = {
        "m_PlayerId": {"kind": "i32", "value": 66},
        "m_IsLocalPlayer": {"kind": "boolean", "value": True},
        "m_RideId": {"kind": "i32", "value": 9111},
        "m_RoleMoveSpeed": {"kind": "f64", "value": 410.0},
        "m_Gx": gx,
        "m_Gy": gy,
    }
    base_address = 0x100010
    return [
        {
            "name": name,
            "reference": hex(base_address + index * 24),
            "nodeValueAddress": hex(base_address + index * 24 - 8),
            "nodeValue": values[name],
        }
        for index, name in enumerate(probe.FIELD_NAMES)
    ]


def tagged_value(probe, address: int, tag: int) -> dict:
    raw = struct.pack("<Q", probe.gc64_tagged_pointer(address, tag))
    return probe.decode_tvalue(raw)


class SparsePageSession:
    """Serve real serialized LuaJIT objects to the production page reader."""

    uses_root_broker = False
    uses_run_as = False
    closed = False

    def __init__(self, pages: dict[int, bytes]) -> None:
        self.pages = pages

    def read_pages(self, addresses, _timeout):
        return [self.pages.get(address, b"") for address in addresses]

    def close(self):
        self.closed = True


def build_remote_owner_fixture(probe, *, same_range: bool, marked_byte: int = 0):
    """Serialize one GC64 table whose header may be beyond the local search window."""
    table_address = 0x100000
    node_address = 0x108000 if same_range else 0x200000
    ranges = (
        [(0x100000, 0x110000)]
        if same_range
        else [(0x100000, 0x101000), (0x200000, 0x201000)]
    )
    pages: dict[int, bytearray] = {}

    def put(address: int, raw: bytes) -> None:
        cursor = 0
        while cursor < len(raw):
            page = (address + cursor) & ~(probe.PAGE_SIZE - 1)
            page_offset = (address + cursor) - page
            amount = min(len(raw) - cursor, probe.PAGE_SIZE - page_offset)
            pages.setdefault(page, bytearray(probe.PAGE_SIZE))[
                page_offset : page_offset + amount
            ] = raw[cursor : cursor + amount]
            cursor += amount

    header = bytearray(probe.GCTAB_SIZE)
    header[8] = marked_byte
    header[9] = probe.GCT_TAB
    struct.pack_into("<Q", header, 16, 0)  # array pointer
    struct.pack_into("<Q", header, 40, node_address)
    struct.pack_into("<I", header, 48, 0)  # array size
    struct.pack_into("<I", header, 52, 7)
    put(table_address, header)

    values = {
        "m_PlayerId": struct.pack("<Q", probe.gc64_tagged_pointer(66, probe.LJ_TISNUM)),
        "m_IsLocalPlayer": struct.pack("<Q", probe.gc64_tagged_pointer(0, probe.LJ_TTRUE)),
        "m_RideId": struct.pack("<Q", probe.gc64_tagged_pointer(9111, probe.LJ_TISNUM)),
        "m_RoleMoveSpeed": struct.pack("<d", 410.0),
        "m_Gx": struct.pack("<Q", probe.gc64_tagged_pointer(73, probe.LJ_TISNUM)),
        "m_Gy": struct.pack("<Q", probe.gc64_tagged_pointer(56, probe.LJ_TISNUM)),
    }
    references = []
    for index, name in enumerate(probe.FIELD_NAMES):
        string_address = table_address + 0x100 + index * 0x40
        encoded = name.encode("utf-8")
        string_header = bytearray(probe.GCSTR_SIZE)
        string_header[9] = probe.GCSTR_GCT
        struct.pack_into("<I", string_header, 20, len(encoded))
        put(string_address, string_header + encoded)
        node = node_address + index * probe.NODE_STRIDE
        key = probe.gc64_tagged_pointer(string_address, probe.LJ_TSTR)
        put(node, values[name] + struct.pack("<Q", key) + b"\0" * 8)
        references.append({
            "name": name,
            "reference": hex(node + 8),
            "nodeValueAddress": hex(node),
            "stringObject": hex(string_address),
            "nodeValue": probe.decode_tvalue(values[name]),
        })

    for range_start, range_end in ranges:
        for page in range(range_start, range_end, probe.PAGE_SIZE):
            pages.setdefault(page, bytearray(probe.PAGE_SIZE))

    reader = probe.BoundedPageMemoryReader(
        "offline", "fixture", 1, ranges, 1.0,
        shared_session=SparsePageSession({address: bytes(blob) for address, blob in pages.items()}),
    )
    return table_address, node_address, reader, references


def build_chained_remote_owner_fixture(probe):
    """Put only one field in the table's base node array; chain the rest remotely."""
    table_address, node_address, external_nodes = 0x100000, 0x108000, 0x200000
    ranges = [(0x100000, 0x110000), (0x200000, 0x201000)]
    pages: dict[int, bytearray] = {}

    def put(address: int, raw: bytes) -> None:
        cursor = 0
        while cursor < len(raw):
            page = (address + cursor) & ~(probe.PAGE_SIZE - 1)
            page_offset = (address + cursor) - page
            amount = min(len(raw) - cursor, probe.PAGE_SIZE - page_offset)
            pages.setdefault(page, bytearray(probe.PAGE_SIZE))[page_offset:page_offset + amount] = raw[cursor:cursor + amount]
            cursor += amount

    header = bytearray(probe.GCTAB_SIZE)
    header[8] = 3
    header[9] = probe.GCT_TAB
    struct.pack_into("<Q", header, 40, node_address)
    struct.pack_into("<I", header, 52, 7)
    put(table_address, header)
    values = {
        "m_PlayerId": struct.pack("<Q", probe.gc64_tagged_pointer(66, probe.LJ_TISNUM)),
        "m_IsLocalPlayer": struct.pack("<Q", probe.gc64_tagged_pointer(0, probe.LJ_TTRUE)),
        "m_RideId": struct.pack("<Q", probe.gc64_tagged_pointer(9111, probe.LJ_TISNUM)),
        "m_RoleMoveSpeed": struct.pack("<d", 410.0),
        "m_Gx": struct.pack("<Q", probe.gc64_tagged_pointer(73, probe.LJ_TISNUM)),
        "m_Gy": struct.pack("<Q", probe.gc64_tagged_pointer(56, probe.LJ_TISNUM)),
    }
    key_addresses = {}
    for index, name in enumerate(probe.FIELD_NAMES):
        string_address = table_address + 0x100 + index * 0x40
        encoded = name.encode("utf-8")
        string_header = bytearray(probe.GCSTR_SIZE)
        string_header[9] = probe.GCSTR_GCT
        struct.pack_into("<I", string_header, 20, len(encoded))
        put(string_address, string_header + encoded)
        key_addresses[name] = probe.gc64_tagged_pointer(string_address, probe.LJ_TSTR)

    ordered = ["m_PlayerId"] + [name for name in probe.FIELD_NAMES if name != "m_PlayerId"]
    references = []
    first_external = external_nodes
    for index, name in enumerate(ordered):
        node = node_address if index == 0 else external_nodes + (index - 1) * probe.NODE_STRIDE
        next_node = external_nodes if index == 0 else (
            external_nodes + index * probe.NODE_STRIDE if index < len(ordered) - 1 else 0
        )
        put(node, values[name] + struct.pack("<Q", key_addresses[name]) + struct.pack("<Q", next_node))
        references.append({
            "name": name,
            "reference": hex(node + 8),
            "nodeValueAddress": hex(node),
            "stringObject": hex((key_addresses[name] & probe.GC64_MASK)),
            "nodeValue": probe.decode_tvalue(values[name]),
        })

    for bucket in range(1, 8):
        put(node_address + bucket * probe.NODE_STRIDE, b"\0" * probe.NODE_STRIDE)
    for start, end in ranges:
        for page in range(start, end, probe.PAGE_SIZE):
            pages.setdefault(page, bytearray(probe.PAGE_SIZE))
    reader = probe.BoundedPageMemoryReader(
        "offline", "fixture", 1, ranges, 1.0,
        shared_session=SparsePageSession({address: bytes(blob) for address, blob in pages.items()}),
    )
    return table_address, first_external, reader, references


def build_chained_remote_owner_fixture(probe):
    """Put only one field in the table's base node array; chain the rest remotely."""
    table_address, node_address, external_nodes = 0x100000, 0x108000, 0x200000
    ranges = [(0x100000, 0x110000), (0x200000, 0x201000)]
    pages: dict[int, bytearray] = {}

    def put(address: int, raw: bytes) -> None:
        cursor = 0
        while cursor < len(raw):
            page = (address + cursor) & ~(probe.PAGE_SIZE - 1)
            page_offset = (address + cursor) - page
            amount = min(len(raw) - cursor, probe.PAGE_SIZE - page_offset)
            pages.setdefault(page, bytearray(probe.PAGE_SIZE))[page_offset:page_offset + amount] = raw[cursor:cursor + amount]
            cursor += amount

    header = bytearray(probe.GCTAB_SIZE)
    header[8] = 3
    header[9] = probe.GCT_TAB
    struct.pack_into("<Q", header, 40, node_address)
    struct.pack_into("<I", header, 52, 7)
    put(table_address, header)
    values = {
        "m_PlayerId": struct.pack("<Q", probe.gc64_tagged_pointer(66, probe.LJ_TISNUM)),
        "m_IsLocalPlayer": struct.pack("<Q", probe.gc64_tagged_pointer(0, probe.LJ_TTRUE)),
        "m_RideId": struct.pack("<Q", probe.gc64_tagged_pointer(9111, probe.LJ_TISNUM)),
        "m_RoleMoveSpeed": struct.pack("<d", 410.0),
        "m_Gx": struct.pack("<Q", probe.gc64_tagged_pointer(73, probe.LJ_TISNUM)),
        "m_Gy": struct.pack("<Q", probe.gc64_tagged_pointer(56, probe.LJ_TISNUM)),
    }
    key_addresses = {}
    for index, name in enumerate(probe.FIELD_NAMES):
        string_address = table_address + 0x100 + index * 0x40
        encoded = name.encode("utf-8")
        string_header = bytearray(probe.GCSTR_SIZE)
        string_header[9] = probe.GCSTR_GCT
        struct.pack_into("<I", string_header, 20, len(encoded))
        put(string_address, string_header + encoded)
        key_addresses[name] = probe.gc64_tagged_pointer(string_address, probe.LJ_TSTR)

    ordered = ["m_PlayerId"] + [name for name in probe.FIELD_NAMES if name != "m_PlayerId"]
    references = []
    first_external = external_nodes
    for index, name in enumerate(ordered):
        node = node_address if index == 0 else external_nodes + (index - 1) * probe.NODE_STRIDE
        next_node = external_nodes if index == 0 else (
            external_nodes + index * probe.NODE_STRIDE if index < len(ordered) - 1 else 0
        )
        put(node, values[name] + struct.pack("<Q", key_addresses[name]) + struct.pack("<Q", next_node))
        references.append({
            "name": name,
            "reference": hex(node + 8),
            "nodeValueAddress": hex(node),
            "stringObject": hex((key_addresses[name] & probe.GC64_MASK)),
            "nodeValue": probe.decode_tvalue(values[name]),
        })

    for bucket in range(1, 8):
        put(node_address + bucket * probe.NODE_STRIDE, b"\0" * probe.NODE_STRIDE)
    for start, end in ranges:
        for page in range(start, end, probe.PAGE_SIZE):
            pages.setdefault(page, bytearray(probe.PAGE_SIZE))
    reader = probe.BoundedPageMemoryReader(
        "offline", "fixture", 1, ranges, 1.0,
        shared_session=SparsePageSession({address: bytes(blob) for address, blob in pages.items()}),
    )
    return table_address, first_external, reader, references


def main() -> None:
    probe = load_probe_module()

    identity = {
        "pid": 1966,
        "startTicks": 123456,
        "exe": "/system/bin/app_process64",
    }
    first_snapshot = probe.snapshot_result(1966, identity, "ok", "ok", [{}], 1, 1)
    second_snapshot = probe.snapshot_result(1966, identity, "ok", "ok", [{}], 1, 1)
    restarted_snapshot = probe.snapshot_result(
        1966,
        {**identity, "startTicks": 123457},
        "ok",
        "ok",
        [{}],
        1,
        1,
    )
    assert first_snapshot["sessionId"] == second_snapshot["sessionId"]
    assert first_snapshot["sessionId"] != restarted_snapshot["sessionId"]

    stable_id = {"pid": 7, "startTicks": 11, "exe": "/app"}
    base_ranges = [(0x1000, 0x3000), (0x8000, 0x9000)]
    all_chunks = probe.chunk_ranges(base_ranges, probe.PAGE_SIZE)
    reads: list[list[tuple[int, int]]] = []

    def read_full(chunks):
        reads.append(list(chunks))
        return [bytes([index + 1]) * size for index, (_, size) in enumerate(chunks)]

    ranges_calls = iter([base_ranges, base_ranges])
    stable_ranges, stable_data = probe.collect_stable_speed_only_chunks(
        stable_id, probe.PAGE_SIZE, lambda: stable_id,
        lambda: next(ranges_calls), read_full,
    )
    assert stable_ranges == base_ranges
    assert len(stable_data) == len(all_chunks)
    assert reads == [all_chunks]  # single whole-view memory read for both parse passes

    # A short chunk is retried alone after identity and mapping descriptors are refreshed.
    reads.clear()
    rounds = iter([base_ranges, base_ranges, base_ranges, base_ranges])
    def read_with_short(chunks):
        reads.append(list(chunks))
        return [b"" if len(reads) == 1 and index == 0 else b"x" * size
                for index, (_, size) in enumerate(chunks)]
    probe.collect_stable_speed_only_chunks(
        stable_id, probe.PAGE_SIZE, lambda: stable_id,
        lambda: next(rounds), read_with_short,
    )
    assert reads[1] == [all_chunks[0]]

    # Changed descriptor boundaries invalidate old bytes, and vanished mappings drop out.
    reads.clear()
    changed_ranges = [(0x1000, 0x2000), (0x8000, 0x9000)]
    ranges_sequence = iter([base_ranges, changed_ranges, changed_ranges, changed_ranges])
    changed_view, changed_data = probe.collect_stable_speed_only_chunks(
        stable_id, probe.PAGE_SIZE, lambda: stable_id,
        lambda: next(ranges_sequence), read_full,
    )
    assert changed_view == changed_ranges
    assert all(start < 0x2000 or start >= 0x8000 for start, _, _ in changed_data)
    # Same chunk start/size with a changed map_end must be reread, even though
    # the raw memory read extent itself is unchanged.
    assert reads == [all_chunks, [(0x1000, probe.PAGE_SIZE)]]

    # A newly appeared mapping is included in the current complete view and read.
    reads.clear()
    added_ranges = base_ranges + [(0xA000, 0xB000)]
    added_sequence = iter([base_ranges, added_ranges, added_ranges, added_ranges])
    added_view, added_data = probe.collect_stable_speed_only_chunks(
        stable_id, probe.PAGE_SIZE, lambda: stable_id,
        lambda: next(added_sequence), read_full,
    )
    assert added_view == added_ranges
    assert [(start, size) for start, size, _ in added_data] == (
        all_chunks + [(0xA000, probe.PAGE_SIZE)]
    )
    assert reads == [all_chunks, [(0xA000, probe.PAGE_SIZE)]]

    changed_identity = iter([stable_id, {**stable_id, "startTicks": 12}])
    try:
        probe.collect_stable_speed_only_chunks(
            stable_id, probe.PAGE_SIZE, lambda: next(changed_identity),
            lambda: base_ranges, read_full,
        )
    except probe.SpeedOnlySnapshotIdentityChanged:
        pass
    else:
        raise AssertionError("snapshot reused chunks across a process identity change")

    try:
        probe.collect_stable_speed_only_chunks(
            stable_id, probe.PAGE_SIZE, lambda: stable_id,
            lambda: base_ranges, lambda chunks: [b"" for _ in chunks], max_rounds=3,
        )
    except probe.SpeedOnlySnapshotError:
        pass
    else:
        raise AssertionError("persistent short reads were accepted as a complete snapshot")

    missing_snapshot = probe.snapshot_result(
        1966,
        identity,
        "needs_discover",
        "field_string_missing",
        [],
        1,
        0,
        missing_fields=["m_Gx", "m_Gy"],
    )
    assert missing_snapshot["missingFields"] == ["m_Gx", "m_Gy"]

    class FakeBrokerPipe:
        def __enter__(self):
            return self

        def __exit__(self, *_):
            return False

        def write(self, _):
            return None

        def flush(self):
            return None

        def readline(self):
            return b'{"ok":true,"output":"ready"}\n'

        def close(self):
            return None

    pipe_open_count = 0

    def open_broker_after_transient_failure(*_, **__):
        nonlocal pipe_open_count
        pipe_open_count += 1
        if pipe_open_count == 1:
            raise FileNotFoundError("broker is recreating its pipe")
        return FakeBrokerPipe()

    with mock.patch("builtins.open", side_effect=open_broker_after_transient_failure), mock.patch.object(
        probe.io,
        "BufferedRWPair",
        side_effect=lambda reader, _writer, _buffer_size: reader,
    ), mock.patch.object(probe.time, "sleep"):
        broker_client = probe.RootBrokerClient(r"\\.\pipe\test")
        broker_output = broker_client.execute("id", 1.0)
        second_broker_output = broker_client.execute("id", 1.0)
        broker_client.close()
    assert broker_output == b"ready"
    assert second_broker_output == b"ready"
    assert pipe_open_count == 2

    class RecordingRootBroker:
        def __init__(self) -> None:
            self.timeouts: list[float] = []
            self.command_counts: list[int] = []

        def execute(self, command: str, timeout: float) -> bytes:
            self.timeouts.append(timeout)
            self.command_counts.append(len(command.splitlines()))
            return b"\n".join(
                base64.b64encode(b"\0" * probe.PAGE_SIZE)
                for _ in range(self.command_counts[-1])
            ) + b"\n"

    broker = RecordingRootBroker()
    session = probe.AdbPageMemoryReadSession.__new__(
        probe.AdbPageMemoryReadSession
    )
    session.root_broker = broker
    session._closed = False
    session.process = None
    session.pid = 1
    chunk_blobs = session.read_chunks([(0, probe.PAGE_SIZE)], 8.0)
    assert chunk_blobs is not None
    assert len(chunk_blobs) == 1
    assert len(chunk_blobs[0]) == probe.PAGE_SIZE
    assert broker.timeouts == [probe.ROOT_BROKER_BATCH_TIMEOUT_SECONDS]
    assert broker.command_counts == [1]

    large_chunk_size = 0x200000
    multi_chunk_blobs = session.read_chunks(
        [(index * large_chunk_size, large_chunk_size) for index in range(17)],
        8.0,
    )
    assert multi_chunk_blobs is not None
    assert len(multi_chunk_blobs) == 17
    assert broker.command_counts[-2:] == [16, 1]
    assert broker.timeouts[-2:] == [
        probe.ROOT_BROKER_BATCH_TIMEOUT_SECONDS,
        probe.ROOT_BROKER_BATCH_TIMEOUT_SECONDS,
    ]

    page_blobs = session.read_pages(
        [index * probe.PAGE_SIZE for index in range(65)],
        8.0,
    )
    assert page_blobs is not None
    assert len(page_blobs) == 65
    assert broker.command_counts[-3:] == [32, 32, 1]

    raw_fallback_chunks = [
        (0, probe.PAGE_SIZE),
        (probe.PAGE_SIZE, probe.PAGE_SIZE),
    ]
    raw_fallback_values = [
        b"\x00\n\r\xff" * (probe.PAGE_SIZE // 4),
        bytes(range(256)) * (probe.PAGE_SIZE // 256),
    ]
    raw_fallback_output = b"\r\n".join(
        base64.b64encode(value) for value in raw_fallback_values
    ) + b"\r\n"
    class IncompleteSuSession:
        uses_root_broker = False
        uses_run_as = False

        def __init__(self) -> None:
            self.closed = False

        def read_chunks(self, *_args, **_kwargs):
            return probe.MemoryChunkResults([b"" for _ in raw_fallback_chunks], incomplete=True)

        def close(self) -> None:
            self.closed = True

    incomplete_su_session = IncompleteSuSession()
    fallback_result = mock.Mock(returncode=0, stdout=raw_fallback_output)
    with mock.patch.object(
        probe, "run_adb", return_value=fallback_result
    ) as run_fallback:
        raw_fallback_blobs = probe.read_chunks(
            "adb", "emulator-5554", 1, raw_fallback_chunks, 1.0,
            session=incomplete_su_session,
        )
    assert raw_fallback_blobs == raw_fallback_values
    assert incomplete_su_session.closed is True
    fallback_command = run_fallback.call_args.args[2][3]
    assert "| base64 -w 0; echo" in fallback_command

    class FailedSuSession:
        uses_root_broker = False
        uses_run_as = False

        def __init__(self, result=None, closed=False, raises=False) -> None:
            self.result = result
            self.closed = closed
            self.raises = raises

        def read_chunks(self, *_args, **_kwargs):
            if self.raises:
                raise RuntimeError("shell transport failed")
            return self.result

        def close(self) -> None:
            self.closed = True

    for failed_su_session in (
        FailedSuSession(),
        FailedSuSession(result=[]),
        FailedSuSession(raises=True),
        FailedSuSession(closed=True),
    ):
        with mock.patch.object(probe, "run_adb") as adb_fallback:
            try:
                probe.read_chunks(
                    "adb", "emulator-5554", 1, raw_fallback_chunks, 1.0,
                    session=failed_su_session,
                )
            except probe.MemoryReadTransportError:
                pass
            else:
                raise AssertionError("failed su session fell through to exec-out")
            adb_fallback.assert_not_called()

    class PartialRootBroker:
        def execute(self, _command: str, _timeout: float) -> bytes:
            return b"\n".join(
                [
                    base64.b64encode(b"a" * probe.PAGE_SIZE),
                    base64.b64encode(b"short"),
                    base64.b64encode(b"c" * probe.PAGE_SIZE),
                ]
            ) + b"\n"

    partial_session = probe.AdbPageMemoryReadSession.__new__(
        probe.AdbPageMemoryReadSession
    )
    partial_session.root_broker = PartialRootBroker()
    partial_session._closed = False
    partial_session.process = None
    partial_session.pid = 1
    partial_chunks = [(index * probe.PAGE_SIZE, probe.PAGE_SIZE) for index in range(3)]
    with mock.patch.object(probe, "run_adb") as adb_fallback:
        partial_blobs = probe.read_chunks(
            "adb", "emulator-5554", 1, partial_chunks, 1.0, session=partial_session
        )
        assert partial_blobs[0] == b"a" * probe.PAGE_SIZE
        assert partial_blobs[1] == b"short"
        assert partial_blobs[2] == b"c" * probe.PAGE_SIZE
        assert partial_blobs.incomplete is True
        try:
            probe.discover_key_objects(
                "adb", "emulator-5554", 1,
                [(0, 3 * probe.PAGE_SIZE)], probe.PAGE_SIZE, 1.0,
                session=partial_session,
            )
        except probe.MemoryReadTransportError:
            pass
        else:
            raise AssertionError("short broker chunk was reported as missing fields")
        with (
            mock.patch.object(probe, "find_all", return_value=[0]),
            mock.patch.object(probe, "verify_gcstr", return_value=True),
        ):
            try:
                probe.discover_key_objects(
                    "adb", "emulator-5554", 1,
                    [(0, 3 * probe.PAGE_SIZE)], probe.PAGE_SIZE, 1.0,
                    session=partial_session,
                )
            except probe.MemoryReadTransportError:
                pass
            else:
                raise AssertionError("short read was accepted despite all keys in valid chunks")
        adb_fallback.assert_not_called()

    class FailedRootBroker:
        def execute(self, _command: str, _timeout: float) -> bytes:
            return base64.b64encode(b"one-line-for-two-chunks") + b"\n"

        def close(self) -> None:
            pass

    failed_broker_session = probe.AdbPageMemoryReadSession.__new__(
        probe.AdbPageMemoryReadSession
    )
    failed_broker_session.root_broker = FailedRootBroker()
    failed_broker_session._closed = False
    failed_broker_session.process = None
    failed_broker_session.pid = 1
    with mock.patch.object(probe, "run_adb") as adb_fallback:
        try:
            probe.read_chunks(
                "adb", "emulator-5554", 1, partial_chunks[:2], 1.0,
                session=failed_broker_session,
            )
        except probe.MemoryReadTransportError:
            pass
        else:
            raise AssertionError("broker line-count failure became empty memory")
        adb_fallback.assert_not_called()

    assert probe.validate_run_as_package("com.gdoo.dhxy") == "com.gdoo.dhxy"
    for invalid_package in (
        "com.gdoo;id",
        "com.gdoo$(id)",
        "-com.gdoo.dhxy",
        "com..dhxy",
        " com.gdoo.dhxy",
    ):
        try:
            probe.validate_run_as_package(invalid_package)
        except ValueError:
            pass
        else:
            raise AssertionError(f"accepted invalid run-as package {invalid_package!r}")

    try:
        probe.ProbeRuntime("pipe", "com.gdoo.dhxy")
    except ValueError:
        pass
    else:
        raise AssertionError("accepted both root broker and run-as package")

    with contextlib.redirect_stderr(io.StringIO()):
        try:
            probe.build_parser().parse_args([
                "--adb", "adb", "--pid", "1",
                "--run-as-package", "com.gdoo.dhxy",
                "--root-broker-pipe", "pipe",
            ])
        except SystemExit as error:
            assert error.code == 2
        else:
            raise AssertionError("CLI accepted both root broker and run-as package")

    class FailedRunAsSession:
        uses_root_broker = False
        uses_run_as = True

        def __init__(self, closed=False):
            self._closed = closed

        @property
        def closed(self):
            return self._closed

        def read_chunks(self, *_args, **_kwargs):
            return None

        def read_pages(self, *_args, **_kwargs):
            return None

        def close(self):
            self._closed = True

    for closed in (False, True):
        run_as_session = FailedRunAsSession(closed=closed)
        with mock.patch.object(probe, "run_adb") as adb_fallback:
            try:
                probe.read_chunks(
                    "adb",
                    "emulator-5554",
                    1,
                    [(0, probe.PAGE_SIZE)],
                    1.0,
                    session=run_as_session,
                )
            except probe.MemoryReadTransportError:
                pass
            else:
                raise AssertionError("failed run-as read was accepted as empty memory")
            adb_fallback.assert_not_called()

    server_args = probe.argparse.Namespace(
        root_broker_pipe="", run_as_package="", pid=1, cache_path="",
        cache_only=False, require_complete_refine_cards=False,
        preferred_refine_ride_instance_id="", read_timeout=1.0,
    )
    server_output = io.StringIO()
    with (
        mock.patch.object(probe.sys, "stdin", io.StringIO('{"id":"transport-test","pid":1}\n')),
        mock.patch.object(probe, "probe_once", side_effect=probe.MemoryReadTransportError("pipe lost")),
        contextlib.redirect_stdout(server_output),
    ):
        assert probe.run_server(server_args) == 0
    server_lines = [line for line in server_output.getvalue().splitlines() if line.startswith("{")]
    server_result = json.loads(server_lines[-1])
    assert server_result["diagnosticCode"] == "memory_read_transport_failed"
    assert server_result["diagnosticCode"] != "field_string_missing"

    limit_server_output = io.StringIO()
    with (
        mock.patch.object(probe.sys, "stdin", io.StringIO('{"id":"owner-limit","pid":1}\n')),
        mock.patch.object(
            probe, "probe_once",
            side_effect=probe.SpeedOwnerIndexLimitError("speed_owner_index_limit_exceeded"),
        ),
        contextlib.redirect_stdout(limit_server_output),
    ):
        assert probe.run_server(server_args) == 0
    limit_server_result = json.loads(limit_server_output.getvalue().splitlines()[-1])
    assert limit_server_result["diagnosticCode"] == "speed_owner_resource_limit"
    assert limit_server_result["requestId"] == "owner-limit"

    limit_server_output = io.StringIO()
    with (
        mock.patch.object(probe.sys, "stdin", io.StringIO('{"id":"owner-limit","pid":1}\n')),
        mock.patch.object(
            probe, "probe_once",
            side_effect=probe.SpeedOwnerIndexLimitError("speed_owner_index_limit_exceeded"),
        ),
        contextlib.redirect_stdout(limit_server_output),
    ):
        assert probe.run_server(server_args) == 0
    limit_server_result = json.loads(limit_server_output.getvalue().splitlines()[-1])
    assert limit_server_result["diagnosticCode"] == "speed_owner_resource_limit"
    assert limit_server_result["requestId"] == "owner-limit"

    single_args = probe.argparse.Namespace(server=False, pid=1)
    single_output = io.StringIO()
    with (
        mock.patch.object(
            probe,
            "build_parser",
            return_value=mock.Mock(parse_args=mock.Mock(return_value=single_args)),
        ),
        mock.patch.object(probe, "probe_once", side_effect=probe.MemoryReadTransportError("pipe lost")),
        contextlib.redirect_stdout(single_output),
    ):
        assert probe.main() == 2
    single_result = json.loads(single_output.getvalue())
    assert single_result["status"] == "error"
    assert single_result["diagnosticCode"] == "memory_read_transport_failed"

    single_limit_output = io.StringIO()
    with (
        mock.patch.object(
            probe,
            "build_parser",
            return_value=mock.Mock(parse_args=mock.Mock(return_value=single_args)),
        ),
        mock.patch.object(
            probe, "probe_once",
            side_effect=probe.SpeedOwnerIndexLimitError("speed_owner_candidate_limit_exceeded"),
        ),
        contextlib.redirect_stdout(single_limit_output),
    ):
        assert probe.main() == 2
    single_limit_result = json.loads(single_limit_output.getvalue())
    assert single_limit_result["diagnosticCode"] == "speed_owner_resource_limit"

    single_limit_output = io.StringIO()
    with (
        mock.patch.object(
            probe,
            "build_parser",
            return_value=mock.Mock(parse_args=mock.Mock(return_value=single_args)),
        ),
        mock.patch.object(
            probe, "probe_once",
            side_effect=probe.SpeedOwnerIndexLimitError("speed_owner_candidate_limit_exceeded"),
        ),
        contextlib.redirect_stdout(single_limit_output),
    ):
        assert probe.main() == 2
    single_limit_result = json.loads(single_limit_output.getvalue())
    assert single_limit_result["diagnosticCode"] == "speed_owner_resource_limit"

    failed_run_as_session = FailedRunAsSession()
    reader = probe.BoundedPageMemoryReader(
        "adb",
        "emulator-5554",
        1,
        [(0, probe.PAGE_SIZE)],
        1.0,
        shared_session=failed_run_as_session,
    )
    with mock.patch.object(probe, "read_chunks") as adb_fallback:
        assert reader.prefetch([(0, 8)]) is False
        adb_fallback.assert_not_called()
    reader.close()

    floating_candidates = probe.build_local_candidates(
        build_references(
            probe,
            {"kind": "f64", "value": 95.0},
            {"kind": "f64", "value": 50.0},
        ),
        0x2000,
    )
    assert len(floating_candidates) == 1
    assert floating_candidates[0]["gx"] == 95
    assert floating_candidates[0]["gy"] == 50

    integer_candidates = probe.build_local_candidates(
        build_references(
            probe,
            {"kind": "i32", "value": 73},
            {"kind": "i32", "value": 56},
        ),
        0x2000,
    )
    assert len(integer_candidates) == 1
    assert integer_candidates[0]["gx"] == 73
    assert integer_candidates[0]["gy"] == 56

    # Exercise the actual GC64 owner lookup using serialized table and string
    # bytes. The nearby control proves the fixture is parseable; moving only
    # the node array to another mapped range reproduces the live missing-player
    # failure because owner lookup scans only the node's current range/window.
    near_table, near_nodes, near_reader, near_references = build_remote_owner_fixture(
        probe, same_range=True
    )
    near_node = near_nodes + probe.FIELD_NAMES.index("m_IsLocalPlayer") * probe.NODE_STRIDE
    assert probe.read_gc_table(near_reader, near_table) is not None
    assert probe.read_gc_table_nodes(
        near_reader, probe.read_gc_table(near_reader, near_table)
    ) is not None
    assert probe.find_luajit_table_for_node(near_reader, near_node) is not None
    near_candidates = probe.build_speed_only_candidates(near_references, near_reader)
    assert len(near_candidates) == 1, f"nearby owner control failed: {near_candidates!r}"
    assert near_candidates[0]["playerId"] == 66
    assert near_candidates[0]["mountId"] == 9111
    near_reader.close()

    far_table, far_nodes, far_reader, far_references = build_remote_owner_fixture(
        probe, same_range=False, marked_byte=5
    )
    far_node = far_nodes + probe.FIELD_NAMES.index("m_IsLocalPlayer") * probe.NODE_STRIDE
    assert probe.read_gc_table(far_reader, far_table) is not None
    # Keep direct production calls visible in this regression: the table and
    # its full six-node array are independently valid when read directly.
    parsed_far_table = probe.read_gc_table(far_reader, far_table)
    assert parsed_far_table is not None
    assert probe.read_gc_table_nodes(far_reader, parsed_far_table) is not None
    assert probe.find_luajit_table_for_node(far_reader, far_node) is None
    far_candidates = probe.build_speed_only_candidates(far_references, far_reader)
    if not far_candidates:
        raise AssertionError(
            "local_player_missing: valid mounted player 66/9111 has a table header "
            "in another mapped range, beyond find_luajit_table_for_node's local scan"
        )
    assert len(far_candidates) == 1
    owner_index = []
    indexed_ranges = far_reader.ranges
    indexed_chunks = probe.chunk_ranges(indexed_ranges, probe.PAGE_SIZE)
    indexed_blobs = [
        far_reader.read(address, size) for address, size in indexed_chunks
    ]
    with mock.patch.object(probe, "read_chunks", return_value=indexed_blobs) as chunk_reads:
        indexed_references = probe.scan_references(
            "offline", "fixture", 1, indexed_ranges, {}, probe.PAGE_SIZE, 1.0,
            table_header_index=owner_index,
            table_header_ranges=indexed_ranges,
        )
    assert chunk_reads.call_count == 1, "owner headers must reuse the reference scan chunks"
    assert any(item[0] == far_table for item in owner_index)
    indexed_candidates = probe.build_speed_only_candidates(
        far_references, far_reader, owner_index
    )
    assert len(indexed_candidates) == 1
    assert indexed_candidates[0]["ownerTableAddress"] == far_table
    far_reader.close()

    shape_blob = bytearray(0x180)
    for offset, node_pointer, mask in (
        (0x00, 0x200000, 7),       # valid nonzero marked byte
        (0x80, 0x200000, 5),       # hash mask is not 2^n - 1
        (0x100, 0x200001, 7),      # node array is not 8-byte aligned
    ):
        raw_header = bytearray(probe.GCTAB_SIZE)
        raw_header[8] = 5
        raw_header[9] = probe.GCT_TAB
        struct.pack_into("<Q", raw_header, 40, node_pointer)
        struct.pack_into("<I", raw_header, 52, mask)
        shape_blob[offset:offset + probe.GCTAB_SIZE] = raw_header
    shape_index = []
    probe.scan_references_from_blob_for_headers(
        bytes(shape_blob), 0x300000, shape_index,
        [(0x300000, 0x301000), (0x200000, 0x201000)],
    )
    assert [item[0] for item in shape_index] == [0x300000]
    original_header_limit = probe.MAX_SPEED_OWNER_HEADERS
    try:
        probe.MAX_SPEED_OWNER_HEADERS = 0
        try:
            probe.scan_references_from_blob_for_headers(
                bytes(shape_blob), 0x300000, [],
                [(0x300000, 0x301000), (0x200000, 0x201000)],
            )
        except probe.SpeedOwnerIndexLimitError as error:
            assert "headerCount=1" in str(error)
        else:
            raise AssertionError("header resource limit must fail closed")
    finally:
        probe.MAX_SPEED_OWNER_HEADERS = original_header_limit

    chained_table, external_local_node, chained_reader, chained_references = (
        build_chained_remote_owner_fixture(probe)
    )
    assert all(
        int(item["nodeValueAddress"], 0) != external_local_node
        for item in chained_references
        if item["name"] != "m_IsLocalPlayer"
    )
    chained_index = []
    chained_chunks = probe.chunk_ranges(chained_reader.ranges, probe.PAGE_SIZE)
    chained_blobs = [
        chained_reader.read(address, size) for address, size in chained_chunks
    ]
    with mock.patch.object(probe, "read_chunks", return_value=chained_blobs):
        probe.scan_references(
            "offline", "fixture", 1, chained_reader.ranges, {}, probe.PAGE_SIZE, 1.0,
            table_header_index=chained_index,
            table_header_ranges=chained_reader.ranges,
        )
    chained_candidates = probe.build_speed_only_candidates(
        chained_references, chained_reader, chained_index
    )
    assert len(chained_candidates) == 1
    assert chained_candidates[0]["ownerTableAddress"] == chained_table
    assert chained_candidates[0]["playerId"] == 66
    chained_reader.close()

    chained_table, external_local_node, chained_reader, chained_references = (
        build_chained_remote_owner_fixture(probe)
    )
    assert all(
        int(item["nodeValueAddress"], 0) != external_local_node
        for item in chained_references
        if item["name"] != "m_IsLocalPlayer"
    )
    chained_index = []
    chained_chunks = probe.chunk_ranges(chained_reader.ranges, probe.PAGE_SIZE)
    chained_blobs = [
        chained_reader.read(address, size) for address, size in chained_chunks
    ]
    with mock.patch.object(probe, "read_chunks", return_value=chained_blobs):
        probe.scan_references(
            "offline", "fixture", 1, chained_reader.ranges, {}, probe.PAGE_SIZE, 1.0,
            table_header_index=chained_index,
            table_header_ranges=chained_reader.ranges,
        )
    chained_candidates = probe.build_speed_only_candidates(
        chained_references, chained_reader, chained_index
    )
    assert len(chained_candidates) == 1
    assert chained_candidates[0]["ownerTableAddress"] == chained_table
    assert chained_candidates[0]["playerId"] == 66
    chained_reader.close()

    # The legacy proximity binder can assemble a player from neighboring
    # Lua tables. Keep this concrete counterexample before exercising the
    # owner-based speed-only binder below.
    mixed_table_references = [
        {"name": "m_IsLocalPlayer", "reference": "0x1008", "nodeValueAddress": "0x1000", "stringObject": "0x9008", "nodeValue": {"kind": "boolean", "value": True}},
        {"name": "m_RoleMoveSpeed", "reference": "0x1018", "nodeValueAddress": "0x1010", "stringObject": "0x9010", "nodeValue": {"kind": "f64", "value": 410.0}},
        {"name": "m_RideId", "reference": "0x1028", "nodeValueAddress": "0x1020", "stringObject": "0x9018", "nodeValue": {"kind": "i32", "value": 9111}},
        {"name": "m_PlayerId", "reference": "0x1038", "nodeValueAddress": "0x1030", "stringObject": "0x9020", "nodeValue": {"kind": "i32", "value": 66}},
        {"name": "m_Gx", "reference": "0x1048", "nodeValueAddress": "0x1040", "stringObject": "0x9028", "nodeValue": {"kind": "i32", "value": 73}},
        {"name": "m_Gy", "reference": "0x1058", "nodeValueAddress": "0x1050", "stringObject": "0x9030", "nodeValue": {"kind": "i32", "value": 56}},
    ]
    # Existing code incorrectly accepts this stitched candidate because it
    # never proves these references belong to one Lua table.
    assert len(probe.build_local_candidates(mixed_table_references, 0x2000)) == 1
    mixed_reader = mock.Mock()
    mixed_reader.read.return_value = b"\0" * 8
    with mock.patch.object(
        probe,
        "find_luajit_table_for_node",
        side_effect=lambda _reader, node: (
            (0xA000, {"address": 0xA000})
            if node == 0x1000
            else (0xB000, {"address": 0xB000})
        ),
    ), mock.patch.object(
        probe,
        "read_gc_table_nodes",
        side_effect=lambda _reader, table: (
            [{"key": "m_IsLocalPlayer", "value": {"kind": "boolean", "value": True}, "nodeAddress": 0xA000}]
            if table["address"] == 0xA000
            else [
                {"key": "m_RoleMoveSpeed", "value": {"kind": "f64", "value": 410.0}, "nodeAddress": 0xB000},
                {"key": "m_RideId", "value": {"kind": "i32", "value": 9111}, "nodeAddress": 0xB010},
                {"key": "m_PlayerId", "value": {"kind": "i32", "value": 66}, "nodeAddress": 0xB020},
                {"key": "m_Gx", "value": {"kind": "i32", "value": 73}, "nodeAddress": 0xB030},
                {"key": "m_Gy", "value": {"kind": "i32", "value": 56}, "nodeAddress": 0xB040},
            ]
        ),
    ), mock.patch.object(
        probe, "tagged_pointer", side_effect=lambda _raw, _tag: 0x9900
    ), mock.patch.object(
        probe, "read_gcstr", return_value="m_IsLocalPlayer"
    ):
        speed_only_binder = getattr(
            probe,
            "build_speed_only_candidates",
            lambda refs, _reader: probe.build_local_candidates(refs, 0x2000),
        )
        assert speed_only_binder(mixed_table_references, mixed_reader) == []

    # Equal field values in different real tables remain ambiguous; values
    # are not a valid identity or deduplication key.
    duplicate_table_references = []
    duplicate_nodes = {}
    for table_index, table_address in enumerate((0xA000, 0xB000)):
        refs = build_references(
            probe,
            {"kind": "i32", "value": 73},
            {"kind": "i32", "value": 56},
        )
        for index, item in enumerate(refs):
            node = table_address + index * 0x10
            item = dict(item)
            item["reference"] = hex(node + 8)
            item["nodeValueAddress"] = hex(node)
            item["stringObject"] = hex(0xA100 + index * 8)
            item["nodeValue"] = {
                **item["nodeValue"],
                "value": 66 if item["name"] == "m_PlayerId" else
                    True if item["name"] == "m_IsLocalPlayer" else
                    410.0 if item["name"] == "m_RoleMoveSpeed" else
                    9111 if item["name"] == "m_RideId" else
                    item["nodeValue"].get("value"),
            }
            duplicate_table_references.append(item)
            duplicate_nodes.setdefault(table_address, []).append(
                {"key": item["name"], "value": item["nodeValue"], "nodeAddress": node}
            )
    with mock.patch.object(
        probe,
        "find_luajit_table_for_node",
        side_effect=lambda _reader, node: next(
            (address, {"address": address}) for address, nodes in duplicate_nodes.items()
            if any(item["nodeAddress"] == node for item in nodes)
        ),
    ), mock.patch.object(
        probe, "read_gc_table_nodes", side_effect=lambda _reader, table: duplicate_nodes[table["address"]]
    ), mock.patch.object(
        probe, "tagged_pointer", side_effect=lambda raw, _tag: 0xA100 + struct.unpack("<Q", raw)[0]
    ), mock.patch.object(
        probe, "read_gcstr", side_effect=lambda _reader, address: next(
            item["key"] for nodes in duplicate_nodes.values() for item in nodes
            if 0xA100 + item["nodeAddress"] == address
        )
    ):
        mixed_reader.read.side_effect = lambda address, _size: struct.pack("<Q", address - 8)
        duplicate_candidates = probe.build_speed_only_candidates(
            duplicate_table_references, mixed_reader
        )
    assert len(duplicate_candidates) == 2
    assert {item["ownerTableAddress"] for item in duplicate_candidates} == {0xA000, 0xB000}
    assert probe.speed_only_probe_cache_path(Path("hold-cache.json")) == Path(
        "hold-cache.json.speed-only.json"
    )

    owner_values = {
        item["name"]: item["nodeValue"]
        for item in build_references(
            probe,
            {"kind": "i32", "value": 73},
            {"kind": "i32", "value": 56},
        )
    }
    owner_values.update({
        "m_PlayerId": {"kind": "i32", "value": 66},
        "m_IsLocalPlayer": {"kind": "boolean", "value": True},
        "m_RideId": {"kind": "nil", "value": None},
        "m_RoleMoveSpeed": {"kind": "f64", "value": 410.0},
    })
    owner_candidate_references = {}
    owner_table = {"node": 0xC000, "hashMask": len(probe.FIELD_NAMES) - 1}
    owner_key_by_node = {}
    owner_value_by_node = {}
    for index, name in enumerate(probe.FIELD_NAMES):
        node_address = 0xC000 + index * probe.NODE_STRIDE
        owner_key_by_node[node_address] = name
        owner_value_by_node[node_address] = owner_values[name]
        owner_candidate_references[name] = {
            "reference": hex(node_address + 8),
            "nodeValueAddress": hex(node_address),
            "stringObject": hex(0xD000 + index * 8),
        }
    owner_cached_candidate = {
        "ownerTableAddress": 0xB800,
        "references": owner_candidate_references,
    }
    owner_candidate = {
        "rawValues": owner_values,
        "references": owner_candidate_references,
    }
    owner_reader = mock.Mock()
    owner_reader.read.side_effect = lambda address, size: (
        struct.pack("<Q", address) * 3 if size == probe.NODE_STRIDE else b""
    )
    with mock.patch.object(probe, "read_gc_table", return_value=owner_table), mock.patch.object(
        probe,
        "read_gc_table_key",
        side_effect=lambda _reader, raw: owner_key_by_node.get(struct.unpack("<Q", raw)[0]),
    ), mock.patch.object(
        probe,
        "decode_tvalue",
        side_effect=lambda raw: owner_value_by_node.get(struct.unpack("<Q", raw)[0]),
    ):
        assert probe.validate_cached_speed_owner(
            owner_reader, owner_cached_candidate, owner_candidate
        )
        owner_key_by_node[0xC000] = "m_RoleMoveSpeed"
        assert not probe.validate_cached_speed_owner(
            owner_reader, owner_cached_candidate, owner_candidate
        )
    with mock.patch.object(probe, "read_gc_table", return_value=None):
        assert not probe.validate_cached_speed_owner(
            owner_reader, owner_cached_candidate, owner_candidate
        )

    invalid_candidates = probe.build_local_candidates(
        build_references(
            probe,
            {"kind": "f64", "value": float("nan")},
            {"kind": "f64", "value": 50.0},
        ),
        0x2000,
    )
    assert invalid_candidates == []

    assert not probe.has_complete_ride_refine_cards({"rideRefineCards": []})
    assert not probe.has_complete_ride_refine_cards({"rideRefineCards": [{} for _ in range(20)]})
    assert probe.has_complete_ride_refine_cards(
        {"rideRefineCards": [{} for _ in range(probe.EXPECTED_RIDE_REFINE_CARD_COUNT)]}
    )

    selected_ride_id = "2092100442933833729"
    mounted_ride_id = "2092100442933833732"
    selected_binding_candidate = {
        "mountId": 9111,
        "isMounted": True,
        "rideInstances": [
            {
                "rideInstanceId": selected_ride_id,
                "rideShapeId": 9121,
                "isRiding": False,
                "growthRate": 1.175,
                "skills": [{"skillId": value} for value in (62119, 62121, 62102)],
            },
            {
                "rideInstanceId": mounted_ride_id,
                "rideShapeId": 9111,
                "isRiding": True,
                "growthRate": 0.815,
                "skills": [{"skillId": value} for value in (61220, 61217, 61223)],
            },
        ],
    }
    probe.bind_refine_ride_instance(selected_binding_candidate, selected_ride_id)
    probe.copy_current_ride_growth_rate(selected_binding_candidate)
    assert selected_binding_candidate["activeRideInstanceId"] == selected_ride_id
    assert selected_binding_candidate["rideBindingStatus"] == "refine_selected"
    assert (
        selected_binding_candidate["rideBindingSource"]
        == "LocalRide.m_RideIns+PresetA050"
    )
    assert selected_binding_candidate["growthRate"] == 1.175
    assert selected_binding_candidate["rideInstances"][0]["isCurrent"] is True
    assert "isCurrent" not in selected_binding_candidate["rideInstances"][1]

    mounted_binding_candidate = json.loads(json.dumps(selected_binding_candidate))
    for instance in mounted_binding_candidate["rideInstances"]:
        instance.pop("isCurrent", None)
    mounted_binding_candidate.pop("activeRideInstanceId", None)
    mounted_binding_candidate.pop("rideBindingStatus", None)
    mounted_binding_candidate.pop("rideBindingSource", None)
    probe.bind_refine_ride_instance(mounted_binding_candidate)
    assert mounted_binding_candidate["activeRideInstanceId"] == mounted_ride_id
    assert mounted_binding_candidate["rideBindingStatus"] == "bound"
    assert mounted_binding_candidate["rideInstances"][1]["isCurrent"] is True

    missing_binding_candidate = json.loads(json.dumps(selected_binding_candidate))
    probe.bind_refine_ride_instance(
        missing_binding_candidate,
        "2092100442933833799",
    )
    assert missing_binding_candidate["rideBindingStatus"] == "unresolved"
    assert "activeRideInstanceId" not in missing_binding_candidate

    cache_candidate = integer_candidates[0]
    cache_payload = probe.build_probe_cache(
        identity,
        [(0x100000, 0x110000)],
        build_references(
            probe,
            {"kind": "i32", "value": 73},
            {"kind": "i32", "value": 56},
        ),
        cache_candidate,
        0x2000,
    )
    assert cache_payload is not None
    assert cache_payload["schema"] == "mount_status_probe_cache.v1"
    assert len(cache_payload["candidate"]["references"]) == len(probe.FIELD_NAMES)
    cache_payload_with_warm_pages = probe.build_probe_cache(
        identity,
        [(0x100000, 0x110000)],
        build_references(
            probe,
            {"kind": "i32", "value": 73},
            {"kind": "i32", "value": 56},
        ),
        cache_candidate,
        0x2000,
        warm_pages=[0x100000, 0x100000],
    )
    assert cache_payload_with_warm_pages is not None
    assert cache_payload_with_warm_pages["warmPages"] == [0x100000]

    warm_hints = probe.build_probe_warm_hints_from_cache(cache_payload_with_warm_pages)
    assert warm_hints is not None
    assert warm_hints["schema"] == probe.WARM_HINT_SCHEMA
    assert warm_hints["warmPages"] == []
    assert "0x100010" not in json.dumps(warm_hints)
    assert "0x100000" not in json.dumps(warm_hints)
    assert "reference" in warm_hints["candidate"]["references"]["m_PlayerId"]
    assert isinstance(
        warm_hints["candidate"]["references"]["m_PlayerId"]["reference"],
        dict,
    )
    with tempfile.TemporaryDirectory() as warm_directory:
        warm_cache_path = Path(warm_directory) / "probe-cache.json"
        warm_hint_path = probe.probe_warm_hints_path(warm_cache_path)
        probe.write_probe_warm_hints(warm_hint_path, warm_hints)
        loaded_hints = probe.load_probe_warm_hints(warm_hint_path)
        assert loaded_hints is not None
        shifted_plan = probe.build_warm_cache_from_hints(
            loaded_hints,
            [(0x200000, 0x210000)],
        )
        assert shifted_plan is not None
        assert (
            shifted_plan["candidate"]["references"]["m_PlayerId"]["reference"]
            == "0x200010"
        )
        assert shifted_plan["warmPages"] == []
        priority_hints = json.loads(json.dumps(loaded_hints))
        priority_hints["rangeSizes"] = [0x10000] * 20
        current_ranges = [
            (0x400000 + index * 0x1000000, 0xC00000 + index * 0x1000000)
            for index in range(20)
        ]
        priority_ranges = probe.select_warm_hint_priority_ranges(
            priority_hints,
            current_ranges,
        )
        assert priority_ranges == current_ranges[:9]

    resized_cache_payload = probe.build_probe_cache(
        identity,
        [(0x100000, 0x1E60000)],
        build_references(
            probe,
            {"kind": "i32", "value": 73},
            {"kind": "i32", "value": 56},
        ),
        cache_candidate,
        0x2000,
        warm_pages=[0x1E5F000],
    )
    assert resized_cache_payload is not None
    resized_hints = probe.build_probe_warm_hints_from_cache(resized_cache_payload)
    assert resized_hints is not None
    with tempfile.TemporaryDirectory() as resized_directory:
        resized_hint_path = Path(resized_directory) / "probe-cache.json.warm.json"
        probe.write_probe_warm_hints(resized_hint_path, resized_hints)
        loaded_resized_hints = probe.load_probe_warm_hints(resized_hint_path)
        assert loaded_resized_hints is not None
        resized_plan = probe.build_warm_cache_from_hints(
            loaded_resized_hints,
            [
                (0x200000, 0x300000),
                (0x400000, 0x2040000),
            ],
        )
        assert resized_plan is not None
        assert (
            resized_plan["candidate"]["references"]["m_PlayerId"]["reference"]
            == "0x400010"
        )
    assert resized_plan["warmPages"] == []

    rich_references = build_references(
        probe,
        {"kind": "i32", "value": 73},
        {"kind": "i32", "value": 56},
    )
    rich_references.extend([
        {
            "name": "m_Rideskills",
            "reference": "0x101000",
            "nodeValueAddress": "0x100ff8",
            "nodeValue": tagged_value(probe, 0x200000, probe.LJ_TTAB),
        },
        {
            "name": "m_RideId",
            "reference": "0x101100",
            "nodeValueAddress": "0x1010f8",
            "nodeValue": tagged_value(probe, 0x210000, probe.LJ_TSTR),
        },
        {
            "name": "PropertyValueDict",
            "reference": "0x101180",
            "nodeValueAddress": "0x101178",
            "nodeValue": tagged_value(probe, 0x220000, probe.LJ_TTAB),
        },
        {
            "name": "m_RideIns",
            "reference": "0x301000",
            "nodeValueAddress": "0x300ff8",
            "nodeValue": tagged_value(probe, 0x230000, probe.LJ_TTAB),
        },
    ])
    rich_cache_payload = probe.build_probe_cache(
        identity,
        [(0x100000, 0x310000)],
        rich_references,
        cache_candidate,
        0x2000,
    )
    assert rich_cache_payload is not None
    assert {
        item["name"] for item in rich_cache_payload["references"]
    } >= {"m_Rideskills", "m_RideId", "PropertyValueDict", "m_RideIns"}
    with tempfile.TemporaryDirectory() as temporary_directory:
        cache_path = Path(temporary_directory) / "probe-cache.json"
        probe.write_probe_cache(cache_path, cache_payload)
        loaded_cache = probe.load_probe_cache(cache_path, identity)
        assert loaded_cache is not None
        assert probe.load_probe_cache(
            cache_path,
            {**identity, "startTicks": identity["startTicks"] + 1},
        ) is None

    refreshed_candidate = probe.build_cached_candidate(
        cache_payload["candidate"],
        [
            {
                **reference,
                "nodeValue": next(
                    value["nodeValue"]
                    for value in build_references(
                        probe,
                        {"kind": "i32", "value": 73},
                        {"kind": "i32", "value": 56},
                    )
                    if value["name"] == reference["name"]
                ),
            }
            for reference in cache_payload["references"]
        ],
    )
    assert refreshed_candidate is not None
    assert refreshed_candidate["playerId"] == 66
    assert refreshed_candidate["gx"] == 73
    assert refreshed_candidate["gy"] == 56

    for read_path in ("point", "fast", "fallback"):
        path_snapshot = probe.snapshot_result(
            1966,
            identity,
            "ok",
            "ok",
            [{}],
            2,
            3,
            sequence=7,
            read_path=read_path,
            timing={"rootAdbMs": 4.5, "probeMs": 8.0, "cardParseMs": 1.25},
        )
        assert path_snapshot["sequence"] == 7
        assert path_snapshot["reader"]["readPath"] == read_path
        assert path_snapshot["reader"]["timingMs"]["rootAdbMs"] == 4.5

    key_descriptors = [
        {
            "name": name,
            "stringObject": hex(0x300000 + index * 8),
            "reference": hex(0x400000 + index * 8),
            "nodeValueAddress": hex(0x3FFFF8 + index * 8),
            "nodeValue": {"kind": "pointer", "value": hex(0x300000 + index * 8)},
        }
        for index, name in enumerate(probe.FIELD_NAMES)
    ]
    key_cache = {"references": key_descriptors}
    with (
        mock.patch.object(probe, "BoundedPageMemoryReader") as reader_factory,
        mock.patch.object(
            probe,
            "read_gcstr",
            side_effect=lambda _reader, address: next(
                name for index, name in enumerate(probe.FIELD_NAMES)
                if address == 0x300000 + index * 8
            ),
        ) as read_key,
        mock.patch.object(probe, "refresh_cached_references") as refresh,
    ):
        key_objects = probe.validated_cached_key_objects(
            "adb", "serial", 1, [(0x300000, 0x301000)], key_cache,
            probe.PAGE_SIZE, 1.0,
        )
        assert key_objects == {
            name: [0x300000 + index * 8]
            for index, name in enumerate(probe.FIELD_NAMES)
        }
        assert read_key.call_count == len(probe.FIELD_NAMES)
        refresh.assert_not_called()
        reader_factory.assert_called_once()
    with (
        mock.patch.object(probe, "BoundedPageMemoryReader"),
        mock.patch.object(probe, "read_gcstr", return_value=None),
    ):
        assert probe.validated_cached_key_objects(
            "adb", "serial", 1, [(0x300000, 0x301000)], key_cache,
            probe.PAGE_SIZE, 1.0,
        ) is None

    runtime = probe.ProbeRuntime()
    assert runtime.begin_request() == 1
    assert runtime.begin_request() == 2
    runtime.set_point_plan(
        identity,
        cache_payload,
        {
            "rideInsAddress": "0x200000",
            "resetOuterIndex": 3,
            "containerAddress": "0x210000",
        },
    )
    assert runtime.get_point_plan(identity) is not None
    assert runtime.get_point_plan(
        {**identity, "startTicks": identity["startTicks"] + 1}
    ) is None
    runtime.close()

    cold_ranges = probe.select_cold_priority_ranges(
        [(0x100000, 0x200000), (0x300000, 0x301000), (0x400000, 0x1400000)]
    )
    assert cold_ranges == [(0x100000, 0x200000), (0x300000, 0x301000)]

    page = b"\0" * probe.PAGE_SIZE
    with (
        mock.patch.object(probe, "read_chunks", return_value=[page]),
        mock.patch.object(probe, "find_all", return_value=[]) as find_names,
    ):
        probe.discover_key_objects(
            "adb", "serial", 1, [(0, probe.PAGE_SIZE)], probe.PAGE_SIZE, 1.0,
            field_names=probe.FIELD_NAMES,
        )
        assert [call.args[1].decode("utf-8") for call in find_names.call_args_list] == list(probe.FIELD_NAMES)
        find_names.reset_mock()
        probe.discover_key_objects(
            "adb", "serial", 1, [(0, probe.PAGE_SIZE)], probe.PAGE_SIZE, 1.0,
        )
        assert [call.args[1].decode("utf-8") for call in find_names.call_args_list] == list(probe.DISCOVERED_FIELD_NAMES)

    # Both parser passes consume the same preloaded bytes and never trigger another ADB read.
    with (
        mock.patch.object(probe, "read_chunks") as forbidden_read,
        mock.patch.object(probe, "find_all", return_value=[]) as speed_find,
    ):
        shared = {(0, probe.PAGE_SIZE): page}
        probe.discover_key_objects(
            "adb", "serial", 1, [(0, probe.PAGE_SIZE)], probe.PAGE_SIZE, 1.0,
            field_names=probe.FIELD_NAMES, preloaded_chunks=shared,
            complete_speed_scan=True,
        )
        assert all(call.args[2] == 4001 for call in speed_find.call_args_list)
        probe.scan_references(
            "adb", "serial", 1, [(0, probe.PAGE_SIZE)], {},
            probe.PAGE_SIZE, 1.0, preloaded_chunks=shared, complete_speed_scan=True,
        )
        forbidden_read.assert_not_called()
    with (
        mock.patch.object(probe, "read_chunks") as forbidden_read,
        mock.patch.object(probe, "find_all", return_value=list(range(4001))),
        mock.patch.object(probe, "verify_gcstr", return_value=True),
    ):
        try:
            probe.discover_key_objects(
                "adb", "serial", 1, [(0, probe.PAGE_SIZE)], probe.PAGE_SIZE, 1.0,
                field_names=probe.FIELD_NAMES, preloaded_chunks=shared,
                complete_speed_scan=True,
            )
        except probe.SpeedOwnerIndexLimitError as error:
            assert "key_string_limit" in str(error)
        else:
            raise AssertionError("speed GCstr discovery silently truncated above its limit")
        forbidden_read.assert_not_called()

    with (
        mock.patch.object(probe, "read_chunks") as forbidden_read,
        mock.patch.object(probe, "find_all", return_value=list(range(8, 8 + 4001 * 8, 8))),
    ):
        try:
            probe.scan_references(
                "adb", "serial", 1, [(0, 0x10000)], {"m_PlayerId": [0x2000]},
                0x10000, 1.0,
                preloaded_chunks={(0, 0x10000): b"x" * 0x10000},
                complete_speed_scan=True,
            )
        except probe.SpeedOwnerIndexLimitError as error:
            assert "reference_limit" in str(error)
        else:
            raise AssertionError("speed reference discovery silently truncated at its limit")
        forbidden_read.assert_not_called()

    class PartialSession:
        uses_root_broker = False
        uses_run_as = False
        closed = False

        def read_chunks(self, chunks, _timeout, batch_size=32):
            return [b"x" * (size - probe.PAGE_SIZE) for _, size in chunks]

        def close(self):
            self.closed = True

    partial_plain_session = PartialSession()
    with mock.patch.object(probe, "run_adb") as forbidden_fallback:
        partial_only = probe.read_chunks(
            "adb", "serial", 1, [(0, 2 * probe.PAGE_SIZE)], 1.0,
            session=partial_plain_session, allow_partial_session=True,
        )
        assert partial_only.incomplete
        assert partial_plain_session.closed is False
        forbidden_fallback.assert_not_called()

    detail_candidate = {"rawValues": {"m_RideId": {"kind": "nil"}}}
    core_descriptors = [
        {
            "name": name,
            "reference": hex(0x1000 + index * 16),
            "nodeValueAddress": hex(0x1008 + index * 16),
            "stringObject": hex(0x5000 + index * 8),
        }
        for index, name in enumerate(probe.FIELD_NAMES)
    ]
    reader = type("Reader", (), {"pages": {}, "accessed_pages": []})()
    with (
        mock.patch.object(probe, "BoundedPageMemoryReader", return_value=reader),
        mock.patch.object(probe, "refresh_cached_references", return_value=[]),
        mock.patch.object(probe, "build_cached_candidate", return_value=detail_candidate),
        mock.patch.object(probe, "validate_cached_speed_owner", return_value=True),
        mock.patch.object(probe, "read_ride_skill_instances") as skills,
        mock.patch.object(probe, "bind_refine_ride_instance") as bind_refine,
        mock.patch.object(probe, "copy_current_ride_growth_rate") as growth,
        mock.patch.object(probe, "read_ride_refine_cards") as cards,
    ):
        cache = {
            "ranges": [[0, probe.PAGE_SIZE]],
            "candidate": {
                "references": {item["name"]: item for item in core_descriptors},
                "ownerTableAddress": 0x8000,
            },
            "references": core_descriptors,
        }
        assert probe.read_cached_snapshot("adb", "serial", 1, cache, 1.0, {}, speed_only=True)
        skills.assert_not_called()
        bind_refine.assert_not_called()
        growth.assert_not_called()
        cards.assert_not_called()

        assert probe.read_cached_snapshot("adb", "serial", 1, cache, 1.0, {})
        skills.assert_called_once()
        bind_refine.assert_called_once()
        growth.assert_called_once()
        cards.assert_called_once()

    optional_descriptor = {
        "name": "m_RideSkillId", "reference": "0x2000",
        "nodeValueAddress": "0x2008", "stringObject": "0x6000",
    }
    optional_ride_id_descriptor = {
        "name": "m_RideId", "reference": "0x3000",
        "nodeValueAddress": "0x3008", "stringObject": "0x7000",
    }
    cache = {
        "ranges": [[0, probe.PAGE_SIZE]],
        "candidate": {
            "references": {item["name"]: item for item in core_descriptors},
            "ownerTableAddress": 0x8000,
        },
        "references": [*core_descriptors, optional_descriptor, optional_ride_id_descriptor],
    }

    class CachedReader:
        def __init__(self, mismatch_address=None):
            self.pages = {}
            self.accessed_pages = []
            self.values = {
                int(item["reference"], 16): struct.pack(
                    "<Q", probe.gc64_tagged_pointer(int(item["stringObject"], 16), probe.LJ_TSTR)
                )
                for item in core_descriptors
            }
            self.values[0x2000] = struct.pack("<Q", 0)
            self.values[0x3000] = struct.pack("<Q", 0)
            self.values.update({int(item["nodeValueAddress"], 16): b"\0" * 8 for item in core_descriptors})
            self.values[0x2008] = b"\0" * 8
            self.values[0x3008] = b"\0" * 8
            if mismatch_address is not None:
                self.values[mismatch_address] = struct.pack("<Q", 0)

        def prefetch(self, _spans):
            return True

        def read(self, address, size):
            return self.values.get(address, b"")[:size]

    gcstr_names = {
        int(item["stringObject"], 16): item["name"] for item in core_descriptors
    }
    with (
        mock.patch.object(probe, "BoundedPageMemoryReader", side_effect=lambda *_args, **_kwargs: CachedReader()),
        mock.patch.object(probe, "read_gcstr", side_effect=lambda _reader, address: gcstr_names.get(address)),
        mock.patch.object(
            probe,
            "build_cached_candidate",
            return_value={"ok": True, "rawValues": {"m_RideId": {"kind": "nil"}}},
        ),
        mock.patch.object(probe, "validate_cached_speed_owner", return_value=True),
    ):
        # Stale optional skill and same-name ride-ID descriptors are omitted.
        assert probe.read_cached_snapshot("adb", "serial", 1, cache, 1.0, {}, speed_only=True)[0]["ok"] is True

    with (
        mock.patch.object(probe, "BoundedPageMemoryReader", side_effect=lambda *_args, **_kwargs: CachedReader(0x1000)),
        mock.patch.object(probe, "read_gcstr", side_effect=lambda _reader, address: gcstr_names.get(address)),
        mock.patch.object(
            probe,
            "build_cached_candidate",
            return_value={"ok": True, "rawValues": {"m_RideId": {"kind": "nil"}}},
        ),
        mock.patch.object(probe, "validate_cached_speed_owner", return_value=True),
    ):
        # A stale core slot still rejects the cached speed-only snapshot.
        assert probe.read_cached_snapshot("adb", "serial", 1, cache, 1.0, {}, speed_only=True) is None

    class SlotReader:
        def __init__(self, slot_value, gcstr_name="m_PlayerId"):
            self.slot_value = slot_value
            self.ranges = [(0x1000, 0x3000)]
            self.values = {
                0x1000: slot_value,
                0x1008: struct.pack("<Q", probe.gc64_tagged_pointer(123, probe.LJ_TISNUM)),
            }
            if gcstr_name is not None:
                encoded_name = gcstr_name.encode("utf-8")
                gcstr_header = bytearray(probe.GCSTR_SIZE)
                gcstr_header[9] = probe.GCSTR_GCT
                struct.pack_into("<I", gcstr_header, 20, len(encoded_name))
                self.values[0x2000] = bytes(gcstr_header)
                self.values[0x2000 + probe.GCSTR_SIZE] = encoded_name
            self.spans = None

        def prefetch(self, spans):
            self.spans = spans
            return True

        def read(self, address, size):
            return self.values.get(address, b"")[:size]

    expected_slot = struct.pack("<Q", probe.gc64_tagged_pointer(0x2000, probe.LJ_TSTR))
    slot_descriptor = [{
        "name": "m_PlayerId", "reference": "0x1000", "nodeValueAddress": "0x1008",
        "stringObject": "0x2000",
    }]
    matching_slot_reader = SlotReader(expected_slot)
    assert probe.refresh_cached_references(
        matching_slot_reader, slot_descriptor, verify_reference_slots=True
    ) is not None
    assert (0x1000, 8) in matching_slot_reader.spans
    mismatch_slot_reader = SlotReader(struct.pack("<Q", 0x3000))
    assert probe.refresh_cached_references(
        mismatch_slot_reader, slot_descriptor, verify_reference_slots=True
    ) is None
    legacy_descriptor = [{
        key: value for key, value in slot_descriptor[0].items() if key != "stringObject"
    }]
    legacy_reader = SlotReader(expected_slot)
    legacy_result = probe.refresh_cached_references(
        legacy_reader, legacy_descriptor, verify_reference_slots=True
    )
    assert legacy_result is not None
    assert legacy_result[0]["stringObject"] == "0x2000"
    assert probe.refresh_cached_references(
        SlotReader(expected_slot, gcstr_name="m_RideId"),
        legacy_descriptor,
        verify_reference_slots=True,
    ) is None

    class PointPlanRuntime:
        uses_root_broker = False
        uses_run_as = False

        def __init__(self):
            self.sequence = 0
            self.plan = None
            self.closed = False
            self.session_reads = 0

        def begin_request(self):
            self.sequence += 1
            return self.sequence

        def get_session(self, *_args):
            return self

        def read_chunks(self, chunks, _timeout, batch_size=32):
            self.session_reads += 1
            return [b"x" * size for _, size in chunks]

        def get_point_plan(self, current_identity):
            if self.plan and self.plan["identity"] != current_identity:
                self.clear_point_plan()
            return self.plan

        def set_point_plan(self, current_identity, cache_payload, point_hint, sequence=1, speed_only=False):
            self.plan = {
                "identity": dict(current_identity),
                "cache": cache_payload,
                "point": dict(point_hint),
                "lastSequence": sequence,
                "speedOnly": speed_only,
            }

        def clear_point_plan(self):
            self.plan = None

        def timing_snapshot(self):
            return {}

        @property
        def timing(self):
            return {}

        def add_card_parse_time(self, _started):
            pass

    runtime = PointPlanRuntime()
    player = {
        "playerId": 66,
        "isMounted": True,
        "mountId": 9111,
        "roleMoveSpeed": 410.0,
        "references": {},
    }
    cache = {"candidate": {"references": {}}, "references": []}
    args = probe.argparse.Namespace(
        adb="adb", serial="serial", pid=1966, read_timeout=1.0,
        min_range=probe.PAGE_SIZE, max_range=probe.PAGE_SIZE, max_ranges=0,
        cache_path="speed-only-cache.json", chunk_size=probe.PAGE_SIZE, candidate_distance=0x2000,
        cache_only=False, require_complete_refine_cards=False,
        preferred_refine_ride_instance_id="", speed_only=True,
        root_broker_pipe="", run_as_package="",
    )
    with (
        mock.patch.object(probe, "process_identity", return_value=identity),
        mock.patch.object(probe, "read_maps", return_value=[(0x1000, 0x2000)]) as maps,
        mock.patch.object(probe, "load_probe_cache", return_value=None),
        mock.patch.object(probe, "load_probe_warm_hints", return_value=None),
        mock.patch.object(probe, "select_cold_priority_ranges", return_value=[]),
        mock.patch.object(
            probe,
            "discover_key_objects",
            side_effect=lambda *_args, field_names=probe.DISCOVERED_FIELD_NAMES, **_kwargs: {
                name: [1] for name in field_names
            },
        ) as discover,
        mock.patch.object(probe, "scan_references", return_value=[]) as scan_refs,
        mock.patch.object(probe, "build_speed_only_candidates", return_value=[player]) as candidates_builder,
        mock.patch.object(probe, "BoundedPageMemoryReader", return_value=reader),
        mock.patch.object(probe, "build_probe_cache", return_value=cache),
        mock.patch.object(probe, "has_complete_ride_refine_cards", return_value=False),
        mock.patch.object(probe, "read_ride_skill_instances", return_value=[]) as full_skills,
        mock.patch.object(probe, "bind_refine_ride_instance") as full_bind_refine,
        mock.patch.object(probe, "copy_current_ride_growth_rate") as full_growth,
        mock.patch.object(probe, "read_ride_refine_cards", return_value=[]) as full_cards,
        mock.patch.object(probe, "write_probe_cache") as write_cache,
        mock.patch.object(probe, "write_probe_warm_hints") as write_warm_hints,
        mock.patch.object(probe, "read_cached_snapshot", return_value=(player, 1, 1, [])) as read_point,
        contextlib.redirect_stdout(io.StringIO()),
    ):
        assert probe.probe_once(args, runtime=runtime) == 0
        assert runtime.plan is not None and runtime.plan["point"] == {}
        assert runtime.plan["speedOnly"] is True
        assert discover.call_count == 1
        assert runtime.session_reads == 1
        write_cache.assert_called_once()
        write_warm_hints.assert_not_called()

        assert probe.probe_once(args, runtime=runtime) == 0
        assert read_point.call_count == 1
        discover.assert_called_once()
        assert runtime.plan["lastSequence"] == 2
        assert write_cache.call_count == 1
        write_warm_hints.assert_not_called()

        args.hold_rebind = True
        read_point.return_value = None
        reused_keys = {name: [index + 1] for index, name in enumerate(probe.FIELD_NAMES)}
        scan_ranges = [(0x1000, 0x2000), (0x3000, 0x4000), (0x5000, 0x6000)]
        maps.return_value = scan_ranges
        scan_passes = []
        def collect_references(_adb, _serial, _pid, selected_ranges, *_args, **_kwargs):
            assert runtime.plan is not None
            scan_passes.append(selected_ranges)
            return [{"pass": "full"}]

        def build_from_combined_references(references, _reader, _header_index):
            assert [item["pass"] for item in references] == ["full"]
            return [player]

        patchers = [
            mock.patch.object(probe, "validated_cached_key_objects", return_value=reused_keys),
            mock.patch.object(
                probe,
                "select_warm_hint_priority_ranges",
                return_value=[scan_ranges[0]],
            ),
            mock.patch.object(probe, "scan_references", side_effect=collect_references),
            mock.patch.object(
                probe,
                "build_speed_only_candidates",
                side_effect=build_from_combined_references,
            ),
            mock.patch.object(probe, "snapshot_result", wraps=probe.snapshot_result),
        ]
        started_patchers = [patcher.start() for patcher in patchers]
        combined_builder = started_patchers[-2]
        emit_rebind_snapshot = started_patchers[-1]
        try:
            assert probe.probe_once(args, runtime=runtime) == 0
        finally:
            for patcher in reversed(patchers):
                patcher.stop()
        assert scan_passes == [scan_ranges]
        assert combined_builder.call_count == 1
        assert emit_rebind_snapshot.call_args.kwargs["read_path"] == "rebind_scan"
        assert runtime.plan["lastSequence"] == 3
        assert discover.call_count == 2  # full current-view GCstr discovery on each cold scan
        assert write_cache.call_count >= 1
        write_warm_hints.assert_not_called()

        priority_hit_patchers = [
            mock.patch.object(probe, "validated_cached_key_objects", return_value=reused_keys),
            mock.patch.object(
                probe,
                "build_probe_warm_hints_from_cache",
                return_value={
                    "ranges": [{"size": probe.PAGE_SIZE}],
                    "candidate": {
                        "references": {
                            name: {
                                "name": name,
                                "reference": {"rangeIndex": 0, "offset": index * 8},
                            }
                            for index, name in enumerate(probe.FIELD_NAMES)
                        }
                    },
                },
            ),
            mock.patch.object(
                probe,
                "select_warm_hint_priority_ranges",
                return_value=[scan_ranges[0]],
            ),
            mock.patch.object(
                probe,
                "scan_references",
                side_effect=lambda _adb, _serial, _pid, selected_ranges, *_args, **_kwargs: [
                    {"pass": "full"}
                ],
            ),
            mock.patch.object(
                probe,
                "build_speed_only_candidates",
                side_effect=lambda references, _reader, _header_index: [player]
                if [item["pass"] for item in references] == ["full"] else [],
            ),
            mock.patch.object(probe, "snapshot_result", wraps=probe.snapshot_result),
        ]
        priority_hit_mocks = [patcher.start() for patcher in priority_hit_patchers]
        try:
            assert probe.probe_once(args, runtime=runtime) == 0
        finally:
            for patcher in reversed(priority_hit_patchers):
                patcher.stop()
        assert priority_hit_mocks[2].call_count == 1
        assert priority_hit_mocks[2].call_args.args[0]["rangeSizes"] == [probe.PAGE_SIZE]
        assert priority_hit_mocks[3].call_count == 1
        assert priority_hit_mocks[5].call_args.kwargs["read_path"] == "rebind_scan"
        scan_refs.return_value = []

        # If the cached hint has no priority subset, a failed complete scan
        # must not append and read the same full range a second time.
        no_priority_passes = []
        def no_priority_scan(_adb, _serial, _pid, selected_ranges, *_args, **_kwargs):
            no_priority_passes.append(selected_ranges)
            return []
        no_priority_patchers = [
            mock.patch.object(probe, "validated_cached_key_objects", return_value=reused_keys),
            mock.patch.object(probe, "select_warm_hint_priority_ranges", return_value=[]),
            mock.patch.object(probe, "select_cold_priority_ranges", return_value=[]),
            mock.patch.object(probe, "scan_references", side_effect=no_priority_scan),
            mock.patch.object(probe, "build_speed_only_candidates", return_value=[]),
        ]
        for patcher in no_priority_patchers:
            patcher.start()
        no_priority_output = io.StringIO()
        try:
            with contextlib.redirect_stdout(no_priority_output):
                assert probe.probe_once(args, runtime=runtime) == 3
        finally:
            for patcher in reversed(no_priority_patchers):
                patcher.stop()
        assert no_priority_passes == [scan_ranges]
        no_priority_result = json.loads(no_priority_output.getvalue())
        assert no_priority_result["speedOwnerHeaderCount"] == 0
        assert "ownerTableAddress" not in no_priority_result

        read_point.return_value = None
        before_invalid_hint = discover.call_count
        invalid_hint_patchers = [
            mock.patch.object(probe, "validated_cached_key_objects", return_value=None),
            mock.patch.object(
                probe,
                "select_warm_hint_priority_ranges",
                return_value=[scan_ranges[0]],
            ),
            mock.patch.object(probe, "snapshot_result", wraps=probe.snapshot_result),
        ]
        invalid_hint_mocks = [patcher.start() for patcher in invalid_hint_patchers]
        try:
            assert probe.probe_once(args, runtime=runtime) == 0
        finally:
            for patcher in reversed(invalid_hint_patchers):
                patcher.stop()
        assert discover.call_count == before_invalid_hint + 1
        assert discover.call_args.args[3] == scan_ranges
        assert invalid_hint_mocks[-1].call_args.kwargs["read_path"] == "rebind_full_discovery"
        assert discover.call_count == before_invalid_hint + 1

        # Failed stable-view collection must remain a structured probe error,
        # rather than raising TypeError while attaching its diagnostic text.
        args.speed_only = True
        args.hold_rebind = False
        runtime.clear_point_plan()
        read_point.return_value = None
        original_runtime_read_chunks = runtime.read_chunks
        runtime.read_chunks = lambda chunks, _timeout, batch_size=32: [b"" for _ in chunks]
        unstable_output = io.StringIO()
        try:
            with contextlib.redirect_stdout(unstable_output):
                assert probe.probe_once(args, runtime=runtime) == 2
        finally:
            runtime.read_chunks = original_runtime_read_chunks
        unstable_result = json.loads(unstable_output.getvalue())
        assert unstable_result["status"] == "error"
        assert unstable_result["diagnosticCode"] == "speed_only_snapshot_unstable"
        assert "speed_only_chunks_incomplete" in unstable_result["error"]

        args.speed_only = False
        args.hold_rebind = False
        maps.return_value = [(0x1000, 0x2000)]
        scan_refs.side_effect = None
        scan_refs.return_value = []
        read_point.return_value = (player, 1, 1, [])
        writes_before_full = write_cache.call_count
        with mock.patch.object(probe, "build_local_candidates", return_value=[player]):
            assert probe.probe_once(args, runtime=runtime) == 0
        assert runtime.plan is None
        full_skills.assert_called_once()
        full_bind_refine.assert_called_once()
        full_growth.assert_called_once()
        full_cards.assert_called_once()
        assert discover.call_count == before_invalid_hint + 2
        assert write_cache.call_count == writes_before_full + 1
        assert str(write_cache.call_args.args[0]) == "speed-only-cache.json"
        write_warm_hints.assert_called_once()

    class FakeServerRuntime:
        def __init__(self, *_args, **_kwargs) -> None:
            self.closed = False

        def close(self) -> None:
            self.closed = True

    server_runtime = FakeServerRuntime()
    speed_only_requests = []
    hold_rebind_requests = []

    def fake_probe_once(request_args, runtime=None):
        speed_only_requests.append(request_args.speed_only)
        hold_rebind_requests.append(request_args.hold_rebind)
        print(json.dumps(probe.snapshot_result(
            request_args.pid,
            identity,
            "ok",
            "ok",
            [{}],
            1,
            1,
        )))
        return 0

    server_args = probe.argparse.Namespace(
        pid=1966,
        root_broker_pipe="",
        run_as_package="",
        cache_path="",
        cache_only=False,
        speed_only=False,
        require_complete_refine_cards=False,
        preferred_refine_ride_instance_id="",
        read_timeout=8.0,
    )
    server_output = io.StringIO()
    server_input = io.StringIO(
        '{"id":"speed-only","pid":1966,"speedOnly":true}\n'
        '{"id":"default","pid":1966}\n'
    )
    with (
        mock.patch.object(probe, "ProbeRuntime", return_value=server_runtime),
        mock.patch.object(probe, "probe_once", side_effect=fake_probe_once),
        mock.patch.object(probe.sys, "stdin", server_input),
        mock.patch.object(probe.sys, "stdout", server_output),
    ):
        assert probe.run_server(server_args) == 0
        assert speed_only_requests == [True, False]
        assert hold_rebind_requests == [False, False]
        assert server_runtime.closed
        server_lines = [json.loads(line) for line in server_output.getvalue().splitlines()]
        assert server_lines[0]["event"] == "mount_status_probe_ready"
        assert server_lines[1]["requestId"] == "speed-only"
        assert server_lines[2]["requestId"] == "default"

    print("MountStatusLuaJitProbeRegression: PASS")


if __name__ == "__main__":
    main()
