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

    detail_candidate = {}
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
        mock.patch.object(probe, "read_ride_skill_instances") as skills,
        mock.patch.object(probe, "bind_refine_ride_instance") as bind_refine,
        mock.patch.object(probe, "copy_current_ride_growth_rate") as growth,
        mock.patch.object(probe, "read_ride_refine_cards") as cards,
    ):
        cache = {
            "ranges": [[0, probe.PAGE_SIZE]],
            "candidate": {"references": {item["name"]: item for item in core_descriptors}},
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
        "candidate": {"references": {item["name"]: item for item in core_descriptors}},
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
        mock.patch.object(probe, "build_cached_candidate", return_value={"ok": True}),
    ):
        # Stale optional skill and same-name ride-ID descriptors are omitted.
        assert probe.read_cached_snapshot("adb", "serial", 1, cache, 1.0, {}, speed_only=True)[0] == {"ok": True}

    with (
        mock.patch.object(probe, "BoundedPageMemoryReader", side_effect=lambda *_args, **_kwargs: CachedReader(0x1000)),
        mock.patch.object(probe, "read_gcstr", side_effect=lambda _reader, address: gcstr_names.get(address)),
        mock.patch.object(probe, "build_cached_candidate", return_value={"ok": True}),
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
        def __init__(self):
            self.sequence = 0
            self.plan = None

        def begin_request(self):
            self.sequence += 1
            return self.sequence

        def get_session(self, *_args):
            return object()

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
        mock.patch.object(probe, "build_local_candidates", return_value=[player]) as candidates_builder,
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
        write_cache.assert_not_called()
        write_warm_hints.assert_not_called()

        assert probe.probe_once(args, runtime=runtime) == 0
        assert read_point.call_count == 1
        discover.assert_called_once()
        assert runtime.plan["lastSequence"] == 2
        write_cache.assert_not_called()
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
            marker = "priority" if selected_ranges == [scan_ranges[0]] else "remaining"
            return [{"pass": marker}]

        def build_from_combined_references(references, _distance):
            if len(references) == 1:
                return []
            assert [item["pass"] for item in references] == ["priority", "remaining"]
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
                "build_local_candidates",
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
        assert scan_passes == [[scan_ranges[0]], scan_ranges[1:]]
        assert combined_builder.call_count == 2
        assert emit_rebind_snapshot.call_args.kwargs["read_path"] == "rebind_scan"
        assert runtime.plan["lastSequence"] == 3
        discover.assert_called_once()
        write_cache.assert_not_called()
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
                return_value=[{"name": "priority-hit"}],
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
        assert priority_hit_mocks[4].call_args.kwargs["read_path"] == "rebind_priority"
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
            mock.patch.object(probe, "build_local_candidates", return_value=[]),
        ]
        for patcher in no_priority_patchers:
            patcher.start()
        try:
            with contextlib.redirect_stdout(io.StringIO()):
                assert probe.probe_once(args, runtime=runtime) == 3
        finally:
            for patcher in reversed(no_priority_patchers):
                patcher.stop()
        assert no_priority_passes == [scan_ranges]

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
        assert discover.call_count == 2

        args.speed_only = False
        args.hold_rebind = False
        maps.return_value = [(0x1000, 0x2000)]
        scan_refs.side_effect = None
        scan_refs.return_value = []
        read_point.return_value = (player, 1, 1, [])
        assert probe.probe_once(args, runtime=runtime) == 0
        assert runtime.plan is None
        full_skills.assert_called_once()
        full_bind_refine.assert_called_once()
        full_growth.assert_called_once()
        full_cards.assert_called_once()
        assert discover.call_count == 3
        write_cache.assert_called_once()
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
