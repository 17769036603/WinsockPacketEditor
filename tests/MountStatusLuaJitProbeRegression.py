"""Offline regression checks for the read-only LuaJIT mount probe."""

from __future__ import annotations

import base64
import importlib.util
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

    print("MountStatusLuaJitProbeRegression: PASS")


if __name__ == "__main__":
    main()
