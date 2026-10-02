"""Offline checks for the guarded partner-panel memory test client."""
from __future__ import annotations

import importlib.util
import struct
import tempfile
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[1]
TOOL_PATH = ROOT / "tools" / "mount-reader" / "partner_panel_memory_test.py"


def load(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError("could not load test target")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class FakeTransport:
    def __init__(self, address: int, raw: bytes):
        self.address = address
        self.raw = raw
        self.writes: list[bytes] = []

    def read(self, address: int, size: int) -> bytes:
        assert address == self.address and size == 8
        return self.raw

    def write(self, address: int, raw: bytes, ticks: int, expected_raw=None) -> None:
        assert address == self.address and ticks == 999
        if expected_raw is not None:
            assert self.raw == expected_raw
        self.writes.append(raw)
        self.raw = raw


def i32_raw(probe, value: int) -> bytes:
    tag = (probe.LJ_TISNUM & 0x1FFFF) << 47
    return struct.pack("<Q", tag | (value & 0xFFFFFFFF))


def make_hit(probe, values: dict[str, bytes]) -> dict:
    entries = {}
    for index, field in enumerate(values):
        raw = values[field]
        entries[field] = {
            "value": probe.decode_tvalue(raw),
            "nodeAddress": 0x1234 if field == "HpMax" else 0x1240 + index * 8,
        }
    return {"table": {"address": 0x5000, "node": 0x6000, "hashMask": 15}, "entries": entries, "values": {
        field: probe.decode_tvalue(raw)["value"]
        for field, raw in values.items()
    }}


def main() -> None:
    client = load(TOOL_PATH, "partner_panel_memory_test")
    probe_module = load(client.PROBE_PATH, "partner_panel_memory_test_probe")
    transport_options = {}

    class SelectedTransport:
        def __init__(self, *_args, **kwargs):
            transport_options.update(kwargs)

    with patch.object(client, "_load", return_value=SimpleNamespace(AdbTransport=SelectedTransport)):
        client._make_transport(SimpleNamespace(
            adb="adb", serial="emulator-5554", pid=25, root_broker_pipe="",
        ), probe_module)
    assert transport_options == {"root_broker": None, "dd_command": "toybox dd", "binary_base64": True}
    assert client._is_target_package("com.gdoo.dhxy:service", "com.gdoo.dhxy")
    assert not client._is_target_package("com.gdoo.dhxy.extra", "com.gdoo.dhxy")
    # Diagnostic scan must retain raw aligned pointers even when their TValue
    # predecessor differs from the supplied screenshot value.
    raw_region = bytearray(32)
    raw_region[8:16] = i32_raw(probe_module, 23818)
    key_address = 0x9998
    raw_region[16:24] = struct.pack(
        "<Q", probe_module.gc64_tagged_pointer(key_address, probe_module.LJ_TSTR)
    )
    fake_chunk = [(0x1000, len(raw_region), bytes(raw_region))]
    diagnostics = {}
    with patch.object(client, "_read_scan_chunks", return_value=iter(fake_chunk)):
        found = client._discover_key_references(
            probe_module, None, None, [(0x1000, 0x1020)],
            {"HpMax": [key_address], "Atk": [], "Speed": []},
            {"HpMax": 30000, "Atk": 2072, "Speed": 158}, diagnostics,
        )
    assert not found["HpMax"]
    assert diagnostics["HpMax"]["gcStringObjects"] == 1
    assert diagnostics["HpMax"]["rawReferenceCount"] == 1
    assert diagnostics["HpMax"]["expectedReferenceCount"] == 0
    assert diagnostics["HpMax"]["predecessorKindCounts"] == {"i32": 1}
    assert diagnostics["HpMax"]["predecessorValueCounts"] == {"i32:23818": 1}
    assert diagnostics["HpMax"]["samples"][0]["value"] == 23818
    assert diagnostics["HpMax"]["samples"][0]["expectedMatch"] is False
    for diagnostic_target in ({}, None):
        with patch.object(client, "MAX_REFERENCES", 0), \
                patch.object(client, "_read_scan_chunks", return_value=iter(fake_chunk)):
            try:
                client._discover_key_references(
                    probe_module, None, None, [(0x1000, 0x1020)],
                    {"HpMax": [key_address], "Atk": [], "Speed": []},
                    {"HpMax": 30000, "Atk": 2072, "Speed": 158}, diagnostic_target,
                )
            except RuntimeError as error:
                assert "reference count exceeds scan limit" in str(error)
            else:
                raise AssertionError("raw LuaJIT references exceeded configured limit")
    table_entries = {
        field: {"key": field, "value": probe_module.decode_tvalue(i32_raw(probe_module, value))}
        for field, value in {"HpMax": 23818, "Atk": 2072, "Speed": 158}.items()
    }
    table_diagnostics = {field: {} for field in client.FIELDS}

    class FakeReader:
        ranges = []
        pages = {}

    class FakeTableProbe:
        @staticmethod
        def find_luajit_table_for_node(_reader, _node):
            return 0x5000, {"address": 0x5000}

        @staticmethod
        def read_gc_table_entries(_reader, _table):
            return list(table_entries.values())

    def fake_references(_probe, _args, _session, _ranges, _keys, _expected, diagnostics, table_headers, _overflow):
        diagnostics.update({field: {} for field in client.FIELDS})
        return {
            field: [{"nodeAddress": 0x1234 + index * 0x18}]
            for index, field in enumerate(client.FIELDS)
        }

    with patch.object(client, "_discover_key_objects", return_value={
        field: [] for field in client.FIELDS
    }), patch.object(client, "_discover_key_references", side_effect=fake_references):
        table_hits = client._scan_tables(
            FakeTableProbe(), None, None, FakeReader(),
            {"HpMax": 23818, "Atk": 2072, "Speed": 158}, table_diagnostics,
        )
    assert len(table_hits) == 1
    for field in client.FIELDS:
        assert table_diagnostics[field]["localParentHitCount"] == 1
        assert set(table_diagnostics[field]["localTableSamples"][0]["fields"]) == set(client.FIELDS)
        assert table_diagnostics[field]["localTableSamples"][0]["values"] == {
            "HpMax": 23818, "Atk": 2072, "Speed": 158,
        }

    # A validated header may be far from its node array; the global header
    # candidate still resolves it, while all three node references stay exact.
    far_headers = {}
    header_blob = bytearray(128)
    header_blob[9] = probe_module.GCT_TAB
    struct.pack_into("<Q", header_blob, 40, 0x5000)
    struct.pack_into("<I", header_blob, 52, 15)
    with patch.object(client, "_read_scan_chunks", return_value=iter([
        (0x1000, len(header_blob), bytes(header_blob)),
    ])):
        client._discover_key_references(
            probe_module, None, None, [(0x1000, 0x1080), (0x5000, 0x5180)],
            {field: [] for field in client.FIELDS},
            {"HpMax": 23818, "Atk": 2072, "Speed": 158},
            table_headers=far_headers,
        )
    assert far_headers == {0x1000: (0x5000, 15)}

    far_nodes = {"HpMax": 0x5000, "Atk": 0x5018, "Speed": 0x5030}
    far_entries = [
        {
            "key": field,
            "value": probe_module.decode_tvalue(i32_raw(probe_module, value)),
            "nodeAddress": far_nodes[field],
        }
        for field, value in {"HpMax": 23818, "Atk": 2072, "Speed": 158}.items()
    ]

    class FarHeaderProbe:
        GCTAB_SIZE = probe_module.GCTAB_SIZE
        GCT_TAB = probe_module.GCT_TAB
        MAX_HASH_MASK = probe_module.MAX_HASH_MASK
        NODE_STRIDE = probe_module.NODE_STRIDE
        address_in_ranges = staticmethod(probe_module.address_in_ranges)

        @staticmethod
        def find_luajit_table_for_node(_reader, _node):
            return None

        @staticmethod
        def read_gc_table(_reader, address):
            assert address == 0x1000
            return {"address": address, "node": 0x5000, "hashMask": 15}

        @staticmethod
        def read_gc_table_entries(_reader, _table):
            return far_entries

    def far_references(_probe, _args, _session, _ranges, _keys, _expected, diagnostics, headers, _overflow):
        headers[0x1000] = (0x5000, 15)
        if diagnostics is not None:
            diagnostics.update({field: {} for field in client.FIELDS})
        return {
            field: [{"nodeAddress": node}]
            for field, node in far_nodes.items()
        }

    with patch.object(client, "_discover_key_objects", return_value={field: [] for field in client.FIELDS}), \
            patch.object(client, "_discover_key_references", side_effect=far_references):
        far_hits = client._scan_tables(
            FarHeaderProbe(), None, None, FakeReader(),
            {"HpMax": 23818, "Atk": 2072, "Speed": 158},
        )
    assert len(far_hits) == 1 and far_hits[0]["table"]["address"] == 0x1000

    overflow_blob = bytearray(0x100)
    for offset, node_base in ((0, 0x5000), (0x40, 0x7000)):
        overflow_blob[offset + 9] = probe_module.GCT_TAB
        struct.pack_into("<Q", overflow_blob, offset + 40, node_base)
        struct.pack_into("<I", overflow_blob, offset + 52, 15)

    class OverflowReader:
        ranges = [(0x1000, 0x1100), (0x5000, 0x5180), (0x7000, 0x7180)]
        pages = {}

    def overflowing_references(_probe, _args, _session, _ranges, _keys, _expected,
                               diagnostics, headers, overflow):
        overflow[0] = True
        if diagnostics is not None:
            diagnostics.update({field: {} for field in client.FIELDS})
        return {field: [{"nodeAddress": node}] for field, node in far_nodes.items()}

    with patch.object(client, "_discover_key_objects", return_value={field: [] for field in client.FIELDS}), \
            patch.object(client, "_discover_key_references", side_effect=overflowing_references), \
            patch.object(client, "_read_scan_chunks", return_value=iter([
                (0x1000, len(overflow_blob), bytes(overflow_blob)),
            ])):
        overflow_hits = client._scan_tables(
            FarHeaderProbe(), None, None, OverflowReader(),
            {"HpMax": 23818, "Atk": 2072, "Speed": 158},
        )
    assert len(overflow_hits) == 1 and overflow_hits[0]["table"]["address"] == 0x1000
    class FakeChunkProbe:
        PAGE_SIZE = 4096

        def __init__(self, fail_twice=False):
            self.calls = 0
            self.fail_twice = fail_twice

        def read_chunks(self, _adb, _serial, _pid, chunks, _timeout, batch_size, session):
            self.calls += 1
            if self.fail_twice or self.calls == 1:
                return [b""] * len(chunks)
            return [b"x" * size for _, size in chunks]

    read_args = SimpleNamespace(adb="adb", serial="emulator-5554", pid=25)
    retry_probe = FakeChunkProbe()
    with patch.object(client, "CHUNK_SIZE", 4096):
        chunks = list(client._read_scan_chunks(
            retry_probe, read_args, None, [(0x1000, 0x2000)]
        ))
    assert retry_probe.calls == 2 and len(chunks) == 1 and len(chunks[0][2]) == 4096
    failed_probe = FakeChunkProbe(fail_twice=True)
    with patch.object(client, "CHUNK_SIZE", 4096):
        try:
            list(client._read_scan_chunks(
                failed_probe, read_args, None, [(0x1000, 0x2000)]
            ))
        except RuntimeError as error:
            assert "scan was discarded" in str(error)
        else:
            raise AssertionError("repeated short memory reads were accepted")
    assert failed_probe.calls == 2
    identity = {"pid": 25, "startTicks": 999, "exe": "/system/bin/app_process64"}
    baseline = {
        "HpMax": i32_raw(probe_module, 23818),
        "Atk": i32_raw(probe_module, 2072),
        "Speed": i32_raw(probe_module, 158),
    }
    direct_entries = {
        field: dict(entry, key=field)
        for field, entry in make_hit(probe_module, baseline)["entries"].items()
    }

    class DirectSession:
        def read_text(self, _command, _timeout):
            return b"com.gdoo.yzqcxy\0" if "cmdline" in _command else b"maps"

        def close(self):
            pass

    class DirectReader:
        def __init__(self, *_args, **_kwargs):
            self.pages = {}

        def close(self):
            pass

    class DirectProbe:
        AdbPageMemoryReadSession = staticmethod(lambda *_args, **_kwargs: DirectSession())
        BoundedPageMemoryReader = DirectReader
        parse_maps = staticmethod(lambda _raw: [(0x1000, 0x10000)])
        process_identity = staticmethod(lambda *_args, **_kwargs: identity)
        read_gc_table = staticmethod(lambda _reader, address: {
            "address": address, "node": 0x6000, "hashMask": 15,
        })
        read_gc_table_entries = staticmethod(lambda _reader, _table: list(direct_entries.values()))
        decode_tvalue = staticmethod(probe_module.decode_tvalue)

    direct_args = SimpleNamespace(
        adb="adb", serial="emulator-5554", pid=25, package=client.PACKAGE,
        root_broker_pipe="",
    )
    node_map = {field: item["nodeAddress"] for field, item in direct_entries.items()}
    direct_snapshot = dict(baseline)
    table_binding = {"address": 0x5000, "node": 0x6000, "hashMask": 15}
    direct_identity, _ = client._direct_rebind(
        direct_args, identity, table_binding, node_map, direct_snapshot, DirectProbe
    )
    assert direct_identity == identity
    moved_direct_entries = dict(direct_entries)
    moved_direct_entries["HpMax"] = dict(direct_entries["HpMax"], nodeAddress=0x1300)
    DirectProbe.read_gc_table_entries = staticmethod(
        lambda _reader, _table: list(moved_direct_entries.values())
    )
    try:
        client._direct_rebind(
            direct_args, identity, table_binding, node_map, direct_snapshot, DirectProbe
        )
    except RuntimeError as error:
        assert "nodes moved" in str(error)
    else:
        raise AssertionError("direct rebinding accepted moved LuaJIT nodes")
    DirectProbe.read_gc_table_entries = staticmethod(
        lambda _reader, _table: list(direct_entries.values())
    )
    changed = dict(baseline)
    changed["HpMax"] = i32_raw(probe_module, 30000)
    args = SimpleNamespace(
        action="set", adb="adb", serial="emulator-5554", pid=25,
        package=client.PACKAGE, root_broker_pipe="", hp_max=23818,
        atk=2072, speed=158, field="HpMax", value=30000,
        write=False, journal="",
    )

    with tempfile.TemporaryDirectory() as directory:
        journal = Path(directory) / "partner-panel.json"
        args.journal = str(journal)
        transport = FakeTransport(0x1234, baseline["HpMax"])
        locate = lambda *_: (identity, client.PACKAGE, [make_hit(probe_module, baseline)])
        with patch.object(client, "_locate", side_effect=locate):
            result = client.run(args, probe_module, transport)
        assert result["dryRun"] and not result["written"]
        assert not transport.writes and not journal.exists()

        args.write = True
        moved_hit = make_hit(probe_module, baseline)
        moved_hit["entries"]["HpMax"]["nodeAddress"] = 0x1300
        args.journal = str(Path(directory) / "moved-set.json")
        with patch.object(client, "_locate", side_effect=locate), \
                patch.object(client, "_direct_rebind", side_effect=RuntimeError("LuaJIT table nodes moved")):
            try:
                client.run(args, probe_module, transport)
            except RuntimeError as error:
                assert "nodes moved" in str(error)
            else:
                raise AssertionError("set accepted a moved LuaJIT node")
        assert not transport.writes and not Path(args.journal).exists()

        args.write = True
        args.journal = str(journal)
        with patch.object(client, "_locate", side_effect=locate), \
                patch.object(client, "_direct_rebind", return_value=(identity, make_hit(probe_module, baseline))), \
                patch.object(client, "_identity_now", return_value=identity):
            result = client.run(args, probe_module, transport)
        assert result["written"] and transport.raw == changed["HpMax"]
        assert transport.writes == [changed["HpMax"]] and journal.exists()

        args.action = "restore"
        with patch.object(client, "_locate", side_effect=AssertionError("restore must use journaled binding")), \
                patch.object(client, "_direct_rebind", side_effect=RuntimeError("LuaJIT table nodes moved")), \
                patch.object(client, "_identity_now", return_value=identity):
            try:
                client.restore(args, probe_module, transport)
            except RuntimeError as error:
                assert "nodes moved" in str(error)
            else:
                raise AssertionError("restore accepted a moved LuaJIT node")
        assert len(transport.writes) == 1 and transport.raw == changed["HpMax"]

        args.write = False
        with patch.object(client, "_locate", side_effect=AssertionError("restore must use journaled binding")), \
                patch.object(client, "_direct_rebind", return_value=(identity, make_hit(probe_module, changed))), \
                patch.object(client, "_identity_now", return_value=identity):
            result = client.restore(args, probe_module, transport)
        assert result["dryRun"] and not result["written"]
        assert len(transport.writes) == 1

        args.write = True
        with patch.object(client, "_locate", side_effect=AssertionError("restore must use journaled binding")), \
                patch.object(client, "_direct_rebind", return_value=(identity, make_hit(probe_module, changed))), \
                patch.object(client, "_identity_now", return_value=identity):
            result = client.restore(args, probe_module, transport)
        assert result["written"] and transport.raw == baseline["HpMax"]
        assert transport.writes == [changed["HpMax"], baseline["HpMax"]]

        args.action = "set"
        args.write = False
        args.journal = str(Path(directory) / "ambiguous.json")
        with patch.object(client, "_locate", return_value=(
            identity, client.PACKAGE,
            [make_hit(probe_module, baseline), make_hit(probe_module, baseline)],
        )):
            try:
                client.run(args, probe_module, transport)
            except RuntimeError as error:
                assert "exactly one table" in str(error)
            else:
                raise AssertionError("ambiguous panel tables were accepted")
        assert len(transport.writes) == 2 and not Path(args.journal).exists()

    old = probe_module.decode_tvalue(baseline["HpMax"])
    assert client._encode_numeric(30000, baseline["HpMax"], probe_module) == changed["HpMax"]
    try:
        client._encode_numeric(30000.5, baseline["HpMax"], probe_module)
    except ValueError:
        pass
    else:
        raise AssertionError("fractional i32 panel value was accepted")
    assert old["value"] == 23818
    print("partner panel memory test regression checks passed")


if __name__ == "__main__":
    main()
