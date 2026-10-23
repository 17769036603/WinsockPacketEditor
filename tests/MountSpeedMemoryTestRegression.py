"""Offline checks for the explicit mount speed memory test client."""
from __future__ import annotations

import importlib.util
import base64
import io
import json
import shlex
import struct
import tempfile
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools" / "mount-reader" / "mount_speed_memory_test.py"


def load(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class FakeProbe:
    decode_tvalue = staticmethod(load(ROOT / "tools/mount-reader/mount_status_luajit_probe.py", "mount_probe_test").decode_tvalue)
    validate_run_as_package = staticmethod(load(ROOT / "tools/mount-reader/mount_status_luajit_probe.py", "mount_probe_run_as_test").validate_run_as_package)

    @staticmethod
    def process_identity(*_):
        return {"pid": 25, "startTicks": 999, "exe": "/system/bin/app_process64"}

    class RootBrokerClient:
        def __init__(self, _pipe):
            raise AssertionError("hold must reuse the preflight RootBroker session")

    class ProbeRuntime:
        def __init__(self, **_):
            self.closed = False
            self._root_broker = object()

        def close(self):
            self.closed = True


class FakeTransport:
    def __init__(self, raw):
        self.raw, self.writes = raw, []

    def read(self, _address, _size, phase="read"):
        return self.raw

    def write(self, _address, raw, expected_start_ticks, expected_raw=None):
        assert expected_start_ticks == 999
        if expected_raw is None:
            raise AssertionError("memory writes must bind to the previously read TValue")
        assert self.raw == expected_raw
        self.writes.append(raw)
        self.raw = raw


class ChangedBeforeWriteTransport(FakeTransport):
    def __init__(self, raw, changed):
        super().__init__(raw)
        self.changed = changed

    def write(self, address, raw, expected_start_ticks, expected_raw=None):
        self.raw = self.changed
        if expected_raw is not None and self.raw != expected_raw:
            raise RuntimeError("current TValue changed before write")
        super().write(address, raw, expected_start_ticks, expected_raw)


class FakeRootBroker:
    def __init__(self, raw):
        self.raw, self.commands = raw, []

    def execute(self, command, _timeout):
        if command.startswith("sh -c "):
            command = shlex.split(command)[2]
        self.commands.append(command)
        if " if=/proc/" in command:
            return base64.b64encode(self.raw)
        if "printf '" in command:
            escaped = command.split("printf '", 1)[1].split("'", 1)[0]
            self.raw = bytes(int(escaped[index + 2:index + 4], 16) for index in range(0, len(escaped), 4))
        return b""


def main():
    client = load(SCRIPT, "mount_speed_memory_test")
    speed_only_calls = []
    hold_rebind_calls = []
    cache_paths = []
    class CaptureProbe:
        @staticmethod
        def probe_once(args, runtime=None):
            speed_only_calls.append(args.speed_only)
            hold_rebind_calls.append(args.hold_rebind)
            cache_paths.append(args.cache_path)
            print("{}")
            return 0
    assert client.capture(CaptureProbe, "adb", "serial", 25) == {}
    assert client.capture(CaptureProbe, "adb", "serial", 25, speed_only=True) == {}
    assert client.capture(CaptureProbe, "adb", "serial", 25, speed_only=True, hold_rebind=True) == {}
    assert client.capture(
        CaptureProbe, "adb", "serial", 25, speed_only=True,
        hold_rebind=True, cache_path="C:/cache/base.json") == {}
    assert speed_only_calls == [False, True, True, True]
    assert hold_rebind_calls == [False, False, True, True]
    assert cache_paths == ["", "", "", "C:/cache/base.json"]
    probe = FakeProbe()
    identity = probe.process_identity()
    short_reads = iter((b"ab", b"abcdefgh"))
    with patch.object(client.subprocess, "run", side_effect=lambda *_args, **_kwargs: SimpleNamespace(stdout=next(short_reads))) as adb_run:
        assert client.AdbTransport("adb", "serial", 25).read(0x1234, 8, phase="pre-write") == b"abcdefgh"
    assert adb_run.call_count == 2
    with patch.object(client.subprocess, "run", return_value=SimpleNamespace(stdout=b"abcdefgh")) as adb_run:
        client.AdbTransport("adb", "serial", 25).read(0x1234, 8)
    assert "toybox dd if=/proc/25/mem" in adb_run.call_args.args[0][-1]
    with patch.object(client.subprocess, "run", return_value=SimpleNamespace(stdout=b"abcdefgh")) as adb_run:
        client.AdbTransport("adb", "serial", 25, dd_command="toybox dd").read(0x1234, 8)
    assert "toybox dd if=/proc/25/mem" in adb_run.call_args.args[0][-1]
    binary_value = b"\x0a\x5d\0\0\0\0\xf9\xff"
    with patch.object(
        client.subprocess, "run",
        return_value=SimpleNamespace(stdout=base64.b64encode(binary_value)),
    ) as adb_run:
        assert client.AdbTransport(
            "adb", "serial", 25, dd_command="toybox dd", binary_base64=True
        ).read(0x1234, 8) == binary_value
    assert "toybox dd if=/proc/25/mem" in adb_run.call_args.args[0][-1]
    assert "| base64 -w 0" in adb_run.call_args.args[0][-1]
    with patch.object(client.subprocess, "run", return_value=SimpleNamespace(stdout=b"ab")) as adb_run:
        try:
            client.AdbTransport("adb", "serial", 25).read(0x1234, 8, phase="pre-write")
        except RuntimeError as error:
            message = str(error)
            assert all(value in message for value in ("pid=25", "address=0x1234", "expected=8", "actual=2", "attempts=4", "phase=pre-write"))
        else:
            raise AssertionError("persistent short read was accepted")
    assert adb_run.call_count == 4
    with patch.object(client.subprocess, "run", return_value=SimpleNamespace(stdout=b"")) as adb_run:
        client.AdbTransport("adb", "emulator-5554", 25).write(0x1234, b"\x12\x34", 999)
    memory_command = adb_run.call_args.args[0][-1]
    assert "exec 3<>/proc/25/mem" in memory_command
    assert '[ "${20}" = 999 ]' in memory_command
    assert "of=/proc/$$/fd/3" in memory_command
    assert "printf '\\x12\\x34' | toybox dd" in memory_command
    with patch.object(client.subprocess, "run", return_value=SimpleNamespace(stdout=b"")) as adb_run:
        client.AdbTransport("adb", "emulator-5554", 25).write(
            0x1234, b"\x12\x34", 999, expected_raw=b"\x56\x78"
        )
    compare_command = adb_run.call_args.args[0][-1]
    assert "current=$(toybox dd if=/proc/$$/fd/3 bs=1 skip=4660 count=2" in compare_command
    assert "[ \"$current\" = 'Vng=' ] || exit 54" in compare_command
    assert "current=$(toybox dd if=" in compare_command
    with patch.object(client.subprocess, "run", return_value=SimpleNamespace(stdout=b"")) as adb_run:
        client.AdbTransport(
            "adb", "serial", 25, dd_command="toybox dd"
        ).write(0x1234, b"\x12\x34", 999, expected_raw=b"\x56\x78")
    selected_dd_command = adb_run.call_args.args[0][-1]
    assert "current=$(toybox dd if=" in selected_dd_command
    assert "printf '\\x12\\x34' | toybox dd" in selected_dd_command
    with patch.object(client.subprocess, "run", return_value=SimpleNamespace(stdout=b"")) as adb_run:
        client.AdbTransport(
            "adb", "serial", 25, dd_command="toybox dd", binary_base64=True
        ).write(0x1234, b"\x11" * 8, 999, expected_raw=binary_value)
    binary_compare_command = adb_run.call_args.args[0][-1]
    assert f"current=$(toybox dd if=/proc/$$/fd/3" in binary_compare_command
    assert base64.b64encode(binary_value).decode("ascii") in binary_compare_command
    assert "printf '" + "\\x11" * 8 + "' | toybox dd" in binary_compare_command
    with patch.object(client.subprocess, "run", return_value=SimpleNamespace(stdout=b"")) as adb_run:
        client.AdbTransport("adb", "emulator-5554", 25, "com.example.app").write(
            0x1234, b"\x12\x34", 999
        )
    run_as_args = adb_run.call_args.args[0]
    assert run_as_args[3:8] == [
        "exec-out", "run-as", "com.example.app", "sh", "-c"
    ]
    assert "su" not in run_as_args
    broker = FakeRootBroker(b"\x00\xffmemory")
    broker_transport = client.AdbTransport("adb", "serial", 25, root_broker=broker)
    assert broker_transport.read(0x1234, 8) == b"\x00\xffmemory"
    broker_transport.write(0x1234, b"\x01\x02\x03\x04\x05\x06\x07\x08", 999)
    assert broker.raw == b"\x01\x02\x03\x04\x05\x06\x07\x08"
    assert len(broker.commands) == 2 and all("/proc/25/mem" in command for command in broker.commands)
    assert "toybox dd if=/proc/25/mem" in broker.commands[0]
    assert "printf '\\x01\\x02\\x03\\x04\\x05\\x06\\x07\\x08' | toybox dd" in broker.commands[1]
    assert "base64 -w 0" in broker.commands[0]
    assert "printf '\\x01\\x02\\x03\\x04\\x05\\x06\\x07\\x08'" in broker.commands[1]
    assert "exec 3<>/proc/25/mem" in broker.commands[1]
    identity_calls = {}
    class IdentityBroker:
        def __init__(self, pipe):
            identity_calls["pipe"] = pipe

        def close(self):
            identity_calls["closed"] = True

    class IdentitySession:
        def __init__(self, _adb, _serial, _pid, root_broker=None, run_as_package=""):
            identity_calls["broker"] = root_broker
            identity_calls["package"] = run_as_package

        def close(self):
            identity_calls["session_closed"] = True
            identity_calls["broker"].close()

    class IdentityProbe:
        RootBrokerClient = IdentityBroker
        AdbPageMemoryReadSession = IdentitySession
        process_identity = staticmethod(lambda *_args, **_kwargs: identity)

    assert client.identity(IdentityProbe, "adb", "serial", 25, root_broker_pipe="\\\\.\\pipe\\wpe-root") == identity
    assert identity_calls["pipe"] == "\\\\.\\pipe\\wpe-root"
    assert identity_calls["broker"] is not None and identity_calls["session_closed"] and identity_calls["closed"]
    old = struct.pack("<Q", ((0xFFFFFFF2 & 0x1FFFF) << 47) | 410)
    player = {"playerId": 7, "isLocalPlayer": True, "isMounted": True, "mountId": 42,
              "references": {"m_RoleMoveSpeed": {"nodeValueAddress": "0x1234"}}}
    snapshot = {"status": "ok", "candidateCount": 1, "process": identity, "player": player}
    with tempfile.TemporaryDirectory() as directory:
        journal = Path(directory) / "journal.json"
        transport = FakeTransport(old)
        args = type("Args", (), {"pid": 25, "mount_id": None, "speed": 500.0,
            "action": "set", "write": False, "journal": str(journal), "adb": "fake", "serial": "fake"})()
        provider = lambda: snapshot
        result = client.run_action(args, transport, provider, probe)
        assert result["written"] is False and not transport.writes and not journal.exists()
        args.write = True
        result = client.run_action(args, transport, provider, probe)
        expected = struct.pack("<Q", ((0xFFFFFFF2 & 0x1FFFF) << 47) | 500)
        assert transport.raw == expected and transport.writes == [expected]
        bound = json.loads(journal.read_text())["binding"]
        assert bound["pid"] == 25 and bound["startTicks"] == 999 and bound["mountId"] == 42

        # Root-broker restore must use the same identity channel as its memory reads.
        args.action = "restore"
        args.write = False
        args.root_broker_pipe = "fake-root-broker"
        identity_pipes = []
        class RootBrokerIdentityProbe(FakeProbe):
            class RootBrokerClient:
                def __init__(self, _pipe):
                    pass

        def capture_identity(_probe, _adb, _serial, _pid, _run_as_package="", root_broker_pipe=""):
            identity_pipes.append(root_broker_pipe)
            return identity

        with patch.object(client, "identity", side_effect=capture_identity):
            restored = client.run_action(args, transport, provider, RootBrokerIdentityProbe)
        assert restored["written"] is False
        assert journal.exists()
        assert identity_pipes == ["fake-root-broker", "fake-root-broker"]
        args.action = "set"
        args.write = True
        delattr(args, "root_broker_pipe")

        changed = struct.pack("<d", 333.0)
        raced_set = ChangedBeforeWriteTransport(old, changed)
        args.journal = str(Path(directory) / "set-race.json")
        try:
            client.run_action(args, raced_set, provider, probe)
        except RuntimeError as error:
            assert "changed before write" in str(error)
        else:
            raise AssertionError("set overwrote a TValue changed after its pre-write read")
        assert raced_set.raw == changed and not raced_set.writes
        args.journal = str(journal)
        writes_before_duplicate = len(transport.writes)
        try:
            client.run_action(args, transport, provider, probe)
        except RuntimeError:
            pass
        else:
            raise AssertionError("overwrote an existing restore journal")
        assert len(transport.writes) == writes_before_duplicate
        args.action = "restore"
        args.speed = None
        saved_journal = journal.read_text()
        raced_restore = ChangedBeforeWriteTransport(expected, changed)
        try:
            client.run_action(args, raced_restore, provider, probe)
        except RuntimeError as error:
            assert "changed before write" in str(error)
        else:
            raise AssertionError("restore overwrote a TValue changed after its pre-write read")
        assert raced_restore.raw == changed and not raced_restore.writes
        invalid_journal = json.loads(saved_journal)
        invalid_journal["binding"]["oldRaw"] = "0000000000000000"
        journal.write_text(json.dumps(invalid_journal))
        try:
            client.run_action(args, transport, provider, probe)
        except RuntimeError:
            pass
        else:
            raise AssertionError("accepted an invalid restore TValue")
        journal.write_text(saved_journal)
        client.run_action(args, transport, provider, probe)
        assert transport.raw == old and not journal.exists()
        args.action = "hold"
        args.speed = 500.0
        args.duration = 0.3
        args.interval = 0.1
        args.journal = str(Path(directory) / "hold.json")
        ticks = [0.0]
        def advance(seconds):
            assert transport.writes and transport.raw == expected
            ticks[0] += seconds
        with patch.object(client.time, "monotonic", side_effect=lambda: ticks[0]), \
                patch.object(client.time, "sleep", side_effect=advance):
            held = client.run_action(args, transport, provider, probe)
        assert held["restored"] is True and held["writes"] == 1 and transport.raw == old

        # Persistent robot control restores on both an explicit stop and stdin EOF.
        args.persistent = True
        for control in ("stop\n", ""):
            args.journal = str(Path(directory) / ("persistent-stop.json" if control else "persistent-eof.json"))
            transport.raw = old
            with patch.object(client.sys, "stdin", io.StringIO(control)):
                held = client.run_action(args, transport, provider, probe)
            assert held["restored"] and transport.raw == old and not Path(args.journal).exists()
        args.interval = 1.0
        waits = []
        transport.raw = old
        held = client.run_hold(
            args,
            transport,
            provider,
            probe,
            "",
            lambda wait: (waits.append(wait) or True),
        )
        assert held["restored"] and waits == [0.1] and transport.raw == old
        args.interval = 0.1
        args.persistent = False

        # A preflight binding saves one startup snapshot while preserving the
        # fresh rebind before writing and the final binding check on release.
        args.mount_id = 42
        args.preflight_player_id = 7
        args.preflight_start_ticks = identity["startTicks"]
        args.preflight_exe = identity["exe"]
        args.preflight_speed_address = 0x1234
        args.persistent = True
        args.journal = str(Path(directory) / "hold-preflight.json")
        transport.raw = old
        preflight_snapshots = []
        def preflight_provider():
            preflight_snapshots.append(True)
            return snapshot
        class CapturesHoldJournalBindingTransport(FakeTransport):
            def write(self, address, raw, expected_start_ticks, expected_raw=None):
                self.journal_binding = json.loads(
                    Path(args.journal).read_text(encoding="utf-8")
                )["binding"]
                super().write(address, raw, expected_start_ticks, expected_raw)

        preflight_transport = CapturesHoldJournalBindingTransport(old)
        held = client.run_hold(
            args,
            preflight_transport,
            preflight_provider,
            probe,
            "",
            lambda _wait: True,
        )
        assert held["restored"] and preflight_transport.raw == old
        assert preflight_transport.journal_binding["address"] == 0x1234
        assert isinstance(preflight_transport.journal_binding["address"], int)
        assert len(preflight_snapshots) == 2
        for name in (
            "preflight_player_id",
            "preflight_start_ticks",
            "preflight_exe",
            "preflight_speed_address",
        ):
            delattr(args, name)
        args.persistent = False
        args.mount_id = None

        # A brief game-side overwrite should be retried after a fresh binding
        # check instead of terminating the persistent hold on the first miss.
        class RevertsHoldWritesTransport(FakeTransport):
            def __init__(self, raw, reverts):
                super().__init__(raw)
                self.reverts_remaining = reverts

            def write(self, address, raw, expected_start_ticks, expected_raw=None):
                super().write(address, raw, expected_start_ticks, expected_raw)
                if raw == expected and self.reverts_remaining:
                    self.raw = old
                    self.reverts_remaining -= 1

        class FakeHoldLogger:
            def __init__(self):
                self.events = []

            def log(self, event, data):
                self.events.append((event, data))

        args.persistent = True
        args.journal = str(Path(directory) / "hold-retry-transient-write.json")
        retry_transport = RevertsHoldWritesTransport(old, 1)
        retry_logger = FakeHoldLogger()
        args._hold_logger = retry_logger
        retry_waits = []
        retry_stop_calls = [0]
        def stop_after_retry(_wait):
            retry_stop_calls[0] += 1
            retry_waits.append(_wait)
            return retry_stop_calls[0] > 1

        held_after_retry = client.run_hold(
            args,
            retry_transport,
            provider,
            probe,
            "",
            stop_after_retry,
        )
        assert held_after_retry["restored"] and retry_transport.raw == old
        assert retry_transport.writes == [expected, expected, old]
        assert retry_waits == [0.1, 0.1]
        assert [event for event, _data in retry_logger.events].count(
            "holdWriteVerificationRetry"
        ) == 1
        assert not Path(args.journal).exists()
        delattr(args, "_hold_logger")

        # One explicit unmounted snapshot followed by the original mount and
        # baseline retries from a fresh snapshot before another target write.
        args.journal = str(Path(directory) / "hold-unmounted-transient.json")
        args.persistent = True
        class SnapshotAwareTransport(FakeTransport):
            def __init__(self, raw):
                super().__init__(raw)
                self.raw_before_writes = []

            def write(self, address, raw, expected_start_ticks, expected_raw=None):
                self.raw_before_writes.append(self.raw)
                super().write(address, raw, expected_start_ticks, expected_raw)

        retry_transport = SnapshotAwareTransport(old)
        unmounted = json.loads(json.dumps(snapshot))
        unmounted["player"]["isMounted"] = False
        unmounted["player"]["mountId"] = 0
        retry_sequence = iter((snapshot, snapshot, unmounted, snapshot, snapshot))
        snapshot_count = [0]
        def transient_snapshot():
            snapshot_count[0] += 1
            if snapshot_count[0] == 3:
                retry_transport.raw = struct.pack("<d", 230.0)
            elif snapshot_count[0] == 4:
                retry_transport.raw = old
            return next(retry_sequence)
        retry_logger = FakeHoldLogger()
        args._hold_logger = retry_logger
        controls = [0]
        def stop_after_unmounted_retry(_wait):
            controls[0] += 1
            return controls[0] > 2
        before = len(retry_transport.writes)
        recovered = client.run_hold(
            args, retry_transport, transient_snapshot, probe, "",
            stop_after_unmounted_retry,
        )
        assert recovered["restored"] and retry_transport.raw == old
        assert retry_transport.writes[before:] == [expected, expected, old]
        assert struct.pack("<d", 230.0) not in retry_transport.raw_before_writes
        retry_events = [event for event, _data in retry_logger.events]
        assert "holdUnmountedRetry" in retry_events
        assert not Path(args.journal).exists()
        delattr(args, "_hold_logger")

        # Unknown TValue retries and unmounted retries share one two-snapshot
        # budget for a single normal validation call.
        args.journal = str(Path(directory) / "hold-unmounted-unknown-budget.json")
        budget_transport = FakeTransport(old)
        budget_logger = FakeHoldLogger()
        args._hold_logger = budget_logger
        budget_sequence = iter((snapshot, snapshot, unmounted, snapshot,
                                unmounted, snapshot, unmounted, snapshot))
        budget_snapshot_calls = [0]
        def interleaved_unknown_snapshot():
            budget_snapshot_calls[0] += 1
            if budget_snapshot_calls[0] == 4:
                budget_transport.raw = struct.pack("<d", 333.0)
            return next(budget_sequence)
        before = len(budget_transport.writes)
        try:
            client.run_hold(
                args, budget_transport, interleaved_unknown_snapshot, probe, "", lambda _wait: False,
            )
        except RuntimeError as error:
            assert "after 2 hold retries" in str(error) and "recovery blocked" in str(error)
        else:
            raise AssertionError("unknown retries reset the unmounted retry budget")
        budget_events = [event for event, _data in budget_logger.events]
        assert budget_events.count("holdUnmountedRetry") == 2
        assert budget_events.count("holdUnknownValueRetry") == 2
        assert budget_transport.writes[before:] == [expected]
        assert Path(args.journal).exists()
        Path(args.journal).unlink()
        delattr(args, "_hold_logger")

        # Stop during the first post-journal validation must not acquire the
        # target value; final release still requires a fresh mounted snapshot.
        args.journal = str(Path(directory) / "hold-unmounted-stop-before-write.json")
        startup_stop_transport = FakeTransport(old)
        startup_unmounted = json.loads(json.dumps(unmounted))
        startup_stop_sequence = iter((snapshot, startup_unmounted, snapshot))
        startup_stop = client.run_hold(
            args, startup_stop_transport, lambda: next(startup_stop_sequence), probe, "",
            lambda _wait: True,
        )
        assert startup_stop["written"] is False and not startup_stop_transport.writes
        assert not Path(args.journal).exists()

        # A manual stop during the retry goes straight to strict final release
        # and performs no further target write.
        args.journal = str(Path(directory) / "hold-unmounted-stop.json")
        stop_transport = FakeTransport(old)
        stop_calls = [0]
        def stop_during_unmounted_retry(_wait):
            stop_calls[0] += 1
            return stop_calls[0] > 1
        stop_sequence = iter((snapshot, snapshot, unmounted, snapshot))
        def stopped_snapshot():
            value = next(stop_sequence)
            if value is unmounted:
                stop_transport.raw = struct.pack("<d", 230.0)
            elif stop_transport.raw == struct.pack("<d", 230.0):
                stop_transport.raw = old
            return value
        before = len(stop_transport.writes)
        stopped = client.run_hold(
            args, stop_transport, stopped_snapshot, probe, "", stop_during_unmounted_retry,
        )
        assert stopped["restored"] and stop_transport.raw == old
        assert stop_transport.writes[before:] == [expected]
        assert struct.pack("<d", 230.0) not in stop_transport.writes[before:]

        # Persistent unmounted state and any process/player/new-mount binding
        # change fail closed; only the initial target write may have occurred.
        args.journal = str(Path(directory) / "hold-unmounted-persistent.json")
        retry_transport = FakeTransport(old)
        controls = [0]
        def never_stop(_wait):
            controls[0] += 1
            return False
        before = len(retry_transport.writes)
        persistent_sequence = iter((snapshot, snapshot, unmounted, unmounted, unmounted, unmounted))
        try:
            client.run_hold(
                args, retry_transport, lambda: next(persistent_sequence), probe, "", never_stop,
            )
        except RuntimeError as error:
            assert "remained unmounted" in str(error) and "recovery blocked" in str(error)
        else:
            raise AssertionError("hold accepted a persistent unmounted snapshot")
        assert retry_transport.writes[before:] == [expected]
        assert Path(args.journal).exists()
        Path(args.journal).unlink()

        for mismatch, label in (("process", "pid"), ("player", "player"), ("mount", "mount")):
            args.journal = str(Path(directory) / f"hold-unmounted-{mismatch}-change.json")
            retry_transport = FakeTransport(old)
            changed = json.loads(json.dumps(snapshot))
            if mismatch == "process":
                changed["process"]["pid"] += 1
            elif mismatch == "player":
                changed["player"]["playerId"] += 1
            else:
                changed["player"]["mountId"] += 1
            sequence = iter((snapshot, snapshot, unmounted, changed, changed))
            before = len(retry_transport.writes)
            try:
                client.run_hold(args, retry_transport, lambda: next(sequence), probe, "", never_stop)
            except RuntimeError as error:
                assert label in str(error) or mismatch == "process"
            else:
                raise AssertionError(f"hold accepted an unmounted {mismatch} binding change")
            assert retry_transport.writes[before:] == [expected]
            assert Path(args.journal).exists()
            Path(args.journal).unlink()

        args.journal = str(Path(directory) / "hold-unmounted-not-local.json")
        retry_transport = FakeTransport(old)
        not_local = json.loads(json.dumps(unmounted))
        not_local["player"]["isLocalPlayer"] = False
        retry_logger = FakeHoldLogger()
        args._hold_logger = retry_logger
        sequence = iter((snapshot, snapshot, not_local, not_local))
        try:
            client.run_hold(args, retry_transport, lambda: next(sequence), probe, "", never_stop)
        except RuntimeError as error:
            assert "local player is not mounted" in str(error)
        else:
            raise AssertionError("hold retried an unmounted non-local player snapshot")
        assert "holdUnmountedRetry" not in [event for event, _data in retry_logger.events]
        assert retry_transport.writes == [expected]
        assert Path(args.journal).exists()
        Path(args.journal).unlink()
        delattr(args, "_hold_logger")

        # Repeated baseline read-backs remain bounded and use the normal
        # failure-recovery path without writing over an unknown value.
        args.journal = str(Path(directory) / "hold-retry-limit.json")
        repeated_reverts = RevertsHoldWritesTransport(old, 3)
        repeated_retry_logger = FakeHoldLogger()
        args._hold_logger = repeated_retry_logger
        repeated_retry_waits = []
        try:
            client.run_hold(
                args,
                repeated_reverts,
                provider,
                probe,
                "",
                lambda wait: (repeated_retry_waits.append(wait) or False),
            )
        except RuntimeError as error:
            assert "3 consecutive attempts" in str(error)
            assert "recovery already at baseline; no write needed" in str(error)
        else:
            raise AssertionError("persistent verification misses exceeded the retry limit")
        assert repeated_reverts.writes == [expected, expected, expected]
        assert repeated_reverts.raw == old and not Path(args.journal).exists()
        assert repeated_retry_waits == [0.1, 0.1]
        assert [event for event, _data in repeated_retry_logger.events].count(
            "holdWriteVerificationMiss"
        ) == 3
        delattr(args, "_hold_logger")
        args.persistent = False

        # A short verification read triggers a bound CAS recovery and keeps the original error.
        class ShortAfterWriteTransport(FakeTransport):
            def __init__(self, raw):
                super().__init__(raw)
                self.failed = False

            def read(self, address, size, phase="read"):
                if self.writes and phase == "hold-write-verification":
                    self.failed = True
                    raise RuntimeError("short process memory read pid=25 address=0x1234 expected=8 actual=2 attempts=4 phase=hold-write-verification")
                return self.raw

        args.journal = str(Path(directory) / "hold-short-after-write.json")
        short_transport = ShortAfterWriteTransport(old)
        try:
            client.run_action(args, short_transport, provider, probe)
        except RuntimeError as error:
            assert "phase=hold-write-verification" in str(error)
            assert "recovery restored and verified" in str(error)
        else:
            raise AssertionError("hold accepted a persistent short read after writing")
        assert short_transport.writes == [expected, old] and short_transport.failed
        assert short_transport.raw == old and not Path(args.journal).exists()

        # If another actor restored the baseline before recovery reads, do not write again.
        args.journal = str(Path(directory) / "hold-recovery-already-baseline.json")
        class BaselineBeforeRecoveryTransport(ShortAfterWriteTransport):
            def read(self, address, size, phase="read"):
                if self.writes and phase == "hold-write-verification":
                    self.raw = old
                    self.failed = True
                    raise RuntimeError("simulated hold write verification read failure")
                return self.raw
        already_baseline = BaselineBeforeRecoveryTransport(old)
        try:
            client.run_action(args, already_baseline, provider, probe)
        except RuntimeError as error:
            assert "simulated hold write verification read failure" in str(error)
            assert "recovery already at baseline; no write needed" in str(error)
        else:
            raise AssertionError("hold swallowed the verification read failure")
        assert already_baseline.writes == [expected] and already_baseline.raw == old
        assert not Path(args.journal).exists()

        # A desired value seen before any write was not acquired by this hold.
        args.journal = str(Path(directory) / "hold-unacquired.json")
        class DesiredBeforeWriteTransport(FakeTransport):
            def read(self, address, size, phase="read"):
                if phase == "hold-rebind":
                    self.raw = expected
                return self.raw
        unacquired = DesiredBeforeWriteTransport(old)
        result = client.run_hold(args, unacquired, provider, probe, "", lambda _wait: True)
        assert "did not acquire" in result["reason"] and unacquired.raw == expected
        assert not unacquired.writes and Path(args.journal).exists()
        Path(args.journal).unlink()

        # Keep the recovery journal and report it accurately if the value
        # changes between the hold loop and its final restore check.
        class ChangedBeforeRestoreTransport(FakeTransport):
            def read(self, address, size, phase="read"):
                if phase == "hold-final-check":
                    self.raw = old
                return self.raw

        args.persistent = True
        args.journal = str(Path(directory) / "hold-value-changed-before-restore.json")
        changed_before_restore = ChangedBeforeRestoreTransport(old)
        changed_logger = FakeHoldLogger()
        args._hold_logger = changed_logger
        stop_calls = [0]
        def stop_after_write(_wait):
            stop_calls[0] += 1
            return stop_calls[0] > 1

        changed_result = client.run_hold(
            args, changed_before_restore, provider, probe, "", stop_after_write
        )
        assert not changed_result["restored"]
        assert "journal retained" in changed_result["reason"]
        assert changed_before_restore.writes == [expected]
        assert Path(args.journal).exists()
        run_end = [data for event, data in changed_logger.events if event == "runEnd"][-1]
        assert run_end["journal"] == "retained"
        assert run_end["outcome"] == "value-changed-before-restore"
        Path(args.journal).unlink()
        delattr(args, "_hold_logger")
        args.persistent = False

        # Unknown bytes after a failed verification block recovery without overwriting them.
        args.journal = str(Path(directory) / "hold-recovery-unknown.json")
        unknown = struct.pack("<d", 333.0)
        class UnknownAfterWriteTransport(ShortAfterWriteTransport):
            def read(self, address, size, phase="read"):
                if self.writes and phase == "hold-write-verification":
                    self.raw = unknown
                    return super().read(address, size, phase)
                return super().read(address, size, phase)
        blocked_transport = UnknownAfterWriteTransport(old)
        try:
            client.run_action(args, blocked_transport, provider, probe)
        except RuntimeError as error:
            assert "phase=hold-write-verification" in str(error)
            assert "recovery blocked" in str(error) and "unknown value" in str(error)
        else:
            raise AssertionError("hold accepted blocked failure recovery")
        assert blocked_transport.raw == unknown and blocked_transport.writes == [expected]
        assert Path(args.journal).exists()

        # A binding change during recovery also blocks the CAS and retains the journal.
        args.journal = str(Path(directory) / "hold-recovery-binding.json")
        snapshot_calls = [0]
        def changed_recovery_binding():
            snapshot_calls[0] += 1
            changed = json.loads(json.dumps(snapshot))
            if snapshot_calls[0] >= 3:
                changed["player"]["playerId"] = 8
            return changed
        binding_blocked = ShortAfterWriteTransport(old)
        try:
            client.run_hold(args, binding_blocked, changed_recovery_binding, probe, "", lambda _wait: True)
        except RuntimeError as error:
            assert "phase=hold-write-verification" in str(error)
            assert "recovery blocked" in str(error) and "player changed" in str(error)
        else:
            raise AssertionError("hold recovered after its player binding changed")
        assert binding_blocked.writes == [expected] and binding_blocked.raw == expected
        assert Path(args.journal).exists()

        # Journal cleanup failure must not hide the hold failure or recovery outcome.
        args.journal = str(Path(directory) / "hold-recovery-cleanup-fail.json")
        cleanup_transport = ShortAfterWriteTransport(old)
        original_unlink = Path.unlink
        def fail_journal_unlink(path, *unlink_args, **unlink_kwargs):
            if path == Path(args.journal):
                raise OSError("simulated journal unlink failure")
            return original_unlink(path, *unlink_args, **unlink_kwargs)
        try:
            with patch.object(Path, "unlink", fail_journal_unlink):
                client.run_hold(args, cleanup_transport, provider, probe, "", lambda _wait: True)
        except RuntimeError as error:
            assert "phase=hold-write-verification" in str(error)
            assert "recovery restored and verified" in str(error)
            assert "journal cleanup failed: simulated journal unlink failure" in str(error)
        else:
            raise AssertionError("journal cleanup failure hid neither error")
        assert cleanup_transport.raw == old and Path(args.journal).exists()

        # Existing desired value has no trustworthy original value, so fail before claiming a hold.
        args.journal = str(Path(directory) / "hold-existing.json")
        transport.raw = expected
        before_writes = len(transport.writes)
        try:
            client.run_action(args, transport, provider, probe)
        except RuntimeError as error:
            assert "normal value cannot be inferred safely" in str(error)
        else:
            raise AssertionError("hold accepted a target value with no known baseline")
        assert transport.raw == expected and len(transport.writes) == before_writes and not Path(args.journal).exists()

        # A verified A-to-B transition atomically moves the journal and all later
        # writes/restores to B; the old address is never touched again.
        args.journal = str(Path(directory) / "hold-rebind-fail.json")
        normal_b = struct.pack("<Q", ((0xFFFFFFF2 & 0x1FFFF) << 47) | 420)
        desired_b = expected
        calls = [0]
        def changed_address_snapshot():
            calls[0] += 1
            changed = json.loads(json.dumps(snapshot))
            if calls[0] > 1:
                changed["player"]["references"]["m_RoleMoveSpeed"]["nodeValueAddress"] = "0x5678"
            return changed
        class AddressTransport:
            def __init__(self):
                self.values = {0x1234: old, 0x5678: normal_b}
                self.writes = []

            def read(self, address, _size, phase="read"):
                return self.values[address]

            def write(self, address, raw, expected_start_ticks, expected_raw=None):
                assert expected_start_ticks == 999 and self.values[address] == expected_raw
                self.writes.append((address, raw))
                self.values[address] = raw

        args.persistent = True
        rebind_transport = AddressTransport()
        rebound = client.run_hold(args, rebind_transport, changed_address_snapshot, probe, "", lambda _wait: True)
        assert rebound["restored"]
        assert rebind_transport.values == {0x1234: old, 0x5678: normal_b}
        assert rebind_transport.writes == [(0x5678, desired_b), (0x5678, normal_b)]
        assert not Path(args.journal).exists()

        # If a verified target value follows a relocated field during stop,
        # final restore uses the previously journaled baseline at the new slot.
        args.journal = str(Path(directory) / "hold-rebind-final-target.json")
        calls[0] = 0
        final_rebind_transport = AddressTransport()
        def final_rebind_snapshot():
            calls[0] += 1
            changed = json.loads(json.dumps(snapshot))
            if calls[0] >= 3:
                changed["player"]["references"]["m_RoleMoveSpeed"]["nodeValueAddress"] = "0x5678"
                final_rebind_transport.values[0x5678] = desired_b
            return changed
        def stop_at_final_rebind(_wait):
            return True
        final_rebound = client.run_hold(
            args, final_rebind_transport, final_rebind_snapshot, probe, "", stop_at_final_rebind
        )
        assert final_rebound["restored"]
        assert final_rebind_transport.values == {0x1234: desired_b, 0x5678: old}
        assert final_rebind_transport.writes == [(0x1234, desired_b), (0x5678, old)]
        assert not Path(args.journal).exists()

        # Exception recovery also revalidates and restores the current slot.
        args.journal = str(Path(directory) / "hold-rebind-recovery-target.json")
        calls[0] = 0
        recovery_rebind_transport = AddressTransport()
        def recovery_rebind_snapshot():
            calls[0] += 1
            changed = json.loads(json.dumps(snapshot))
            if calls[0] >= 3:
                changed["player"]["references"]["m_RoleMoveSpeed"]["nodeValueAddress"] = "0x5678"
                recovery_rebind_transport.values[0x5678] = desired_b
            return changed
        def fail_after_first_write(_wait):
            raise RuntimeError("simulated hold-loop failure")
        try:
            client.run_hold(
                args, recovery_rebind_transport, recovery_rebind_snapshot, probe, "",
                fail_after_first_write,
            )
        except RuntimeError as error:
            assert "simulated hold-loop failure" in str(error)
            assert "recovery restored and verified" in str(error)
        else:
            raise AssertionError("hold failure did not report its recovery")
        assert recovery_rebind_transport.values == {0x1234: desired_b, 0x5678: old}
        assert recovery_rebind_transport.writes == [(0x1234, desired_b), (0x5678, old)]
        assert not Path(args.journal).exists()

        # A->B->A is safe when each new snapshot confirms the address and its
        # memory contains a valid non-target baseline before the write.
        args.journal = str(Path(directory) / "hold-rebind-return-a.json")
        calls[0] = 0
        last_snapshot_address = [0x1234]
        def switching_address_snapshot():
            calls[0] += 1
            changed = json.loads(json.dumps(snapshot))
            address = 0x1234 if calls[0] in (1, 3, 4) else 0x5678
            last_snapshot_address[0] = address
            changed["player"]["references"]["m_RoleMoveSpeed"]["nodeValueAddress"] = f"0x{address:x}"
            return changed
        class SnapshotBoundAddressTransport(AddressTransport):
            def write(self, address, raw, expected_start_ticks, expected_raw=None):
                assert address == last_snapshot_address[0]
                if raw == desired_b:
                    decoded = probe.decode_tvalue(self.values[address])
                    assert decoded.get("kind") in {"i32", "f64"} and decoded.get("value") > 0
                    assert self.values[address] != desired_b
                super().write(address, raw, expected_start_ticks, expected_raw)
        args.persistent = True
        return_a_transport = SnapshotBoundAddressTransport()
        stop_calls = [0]
        def stop_after_return(_wait):
            stop_calls[0] += 1
            return stop_calls[0] > 1
        returned = client.run_hold(
            args, return_a_transport, switching_address_snapshot, probe, "", stop_after_return
        )
        assert returned["restored"]
        assert return_a_transport.writes == [
            (0x5678, desired_b), (0x1234, desired_b), (0x1234, old)
        ]
        assert return_a_transport.values[0x1234] == old
        assert return_a_transport.values[0x5678] == desired_b
        assert not Path(args.journal).exists()

        # A target value at B has no safe baseline, so it is never adopted.
        args.journal = str(Path(directory) / "hold-rebind-target-at-b.json")
        calls[0] = 0
        rebind_transport = AddressTransport()
        rebind_transport.values[0x5678] = desired_b
        try:
            client.run_hold(args, rebind_transport, changed_address_snapshot, probe, "", lambda _wait: True)
        except RuntimeError as error:
            assert "already equals target" in str(error)
        else:
            raise AssertionError("hold adopted a target value at the new address")
        assert not rebind_transport.writes and rebind_transport.values[0x1234] == old
        assert not Path(args.journal).exists()

        # Player identity changes fail closed before any address is written.
        args.journal = str(Path(directory) / "hold-rebind-stale-player.json")
        calls[0] = 0
        def stale_player_snapshot():
            calls[0] += 1
            changed = json.loads(json.dumps(snapshot))
            if calls[0] > 1:
                changed["player"]["playerId"] = 8
            return changed
        rebind_transport = AddressTransport()
        try:
            client.run_hold(args, rebind_transport, stale_player_snapshot, probe, "", lambda _wait: True)
        except RuntimeError as error:
            assert "player changed" in str(error)
        else:
            raise AssertionError("hold accepted a stale player binding")
        assert not rebind_transport.writes and rebind_transport.values[0x1234] == old
        assert not Path(args.journal).exists()
        args.persistent = False

        # Exercise the actual default hold capture pipeline and its probe namespace.
        # This catches a missing helper-to-capture handoff as an empty cache path.
        observed_cache_paths = []
        class CachePathProbe(FakeProbe):
            @staticmethod
            def probe_once(probe_args, runtime=None):
                observed_cache_paths.append(probe_args.cache_path)
                print("{}")
                return 0
        cache_args = SimpleNamespace(
            action="hold", adb="adb", serial="serial", pid=25, mount_id=42,
            speed=500.0, interval=0.1, persistent=False, journal=str(Path(directory) / "capture-path.json"),
            probe_cache_path="C:/cache/preflight.json", root_broker_pipe="", run_as_package="",
        )
        with patch.object(
            client, "run_hold",
            side_effect=lambda _args, _transport, snapshot_provider, _probe, _package, _stop: snapshot_provider(),
        ):
            captured = client.run_action(cache_args, FakeTransport(old), None, CachePathProbe)
        assert captured == {} and observed_cache_paths == ["C:/cache/preflight.json"]

        # The default capture path receives one shared runtime and closes it at completion.
        args.journal = str(Path(directory) / "hold-runtime.json")
        transport.raw = old
        seen = []
        speed_only_seen = []
        hold_rebind_seen = []
        forwarded_cache_paths = []
        args.probe_cache_path = "C:/cache/preflight.json"
        def fake_capture(_probe, _adb, _serial, _pid, _run_as_package, runtime, _root_broker_pipe="", speed_only=False, hold_rebind=False, cache_path=""):
            seen.append(runtime)
            speed_only_seen.append(speed_only)
            hold_rebind_seen.append(hold_rebind)
            forwarded_cache_paths.append(cache_path)
            return snapshot
        ticks[0] = 0.0
        with patch.object(client, "capture", side_effect=fake_capture), \
                patch.object(client.time, "monotonic", side_effect=lambda: ticks[0]), \
                patch.object(client.time, "sleep", side_effect=advance):
            client.run_action(args, transport, None, probe)
        assert seen and all(runtime is seen[0] for runtime in seen)
        assert speed_only_seen and all(speed_only_seen)
        assert hold_rebind_seen and all(hold_rebind_seen)
        assert forwarded_cache_paths and all(path == "C:/cache/preflight.json" for path in forwarded_cache_paths)
        assert seen[0].closed

        # A root broker hold must reuse its one long-lived client for snapshots and identity.
        args.persistent = True
        args.root_broker_pipe = "fake-root-broker"
        args.journal = str(Path(directory) / "hold-root-broker.json")
        transport.raw = old
        seen.clear()
        with patch.object(client, "capture", side_effect=fake_capture), \
                patch.object(client.sys, "stdin", io.StringIO("stop\n")):
            held = client.run_action(args, transport, None, probe)
        assert held["restored"] and transport.raw == old
        assert seen and all(runtime is seen[0] for runtime in seen) and seen[0].closed
        assert forwarded_cache_paths and all(path == "C:/cache/preflight.json" for path in forwarded_cache_paths)
        del args.root_broker_pipe
        args.persistent = False

        calls = [0]
        def changing_snapshot():
            calls[0] += 1
            if calls[0] > 1:
                transport.raw = struct.pack("<d", 333.0)
            return snapshot
        transport.raw = old
        args.journal = str(Path(directory) / "hold-unknown.json")
        ticks[0] = 0.0
        try:
            with patch.object(client.time, "monotonic", side_effect=lambda: ticks[0]), \
                    patch.object(client.time, "sleep", side_effect=lambda seconds: ticks.__setitem__(0, ticks[0] + seconds)):
                client.run_action(args, transport, changing_snapshot, probe)
        except RuntimeError as error:
            assert "unknown value" in str(error)
        else:
            raise AssertionError("hold accepted an unknown current TValue")
        assert transport.raw == struct.pack("<d", 333.0)

        # Persistent mode also leaves an externally changed value untouched.
        args.persistent = True
        args.journal = str(Path(directory) / "persistent-unknown.json")
        transport.raw = old
        controls = [0]
        def stop_after_external_change(_wait):
            controls[0] += 1
            if controls[0] == 1:
                return False
            transport.raw = struct.pack("<d", 333.0)
            return True
        released = client.run_hold(args, transport, provider, probe, "", stop_after_external_change)
        assert released["restored"] is False
        assert transport.raw == struct.pack("<d", 333.0) and Path(args.journal).exists()
        Path(args.journal).unlink()

        # A third TValue during a map switch is retried only after a fresh
        # binding snapshot; the transient integer is never written.
        args.journal = str(Path(directory) / "hold-transient-unknown.json")
        args.persistent = True
        args.interval = 0.1
        transient = struct.pack("<Q", ((0xFFFFFFF2 & 0x1FFFF) << 47) | 200)
        transport.raw = old
        snapshot_calls = [0]
        def transient_snapshot():
            snapshot_calls[0] += 1
            if snapshot_calls[0] == 4:
                transport.raw = old
            return snapshot
        controls = [0]
        def stop_after_transient(_wait):
            controls[0] += 1
            if controls[0] == 1:
                transport.raw = transient
                return False
            return True
        before = len(transport.writes)
        held = client.run_hold(args, transport, transient_snapshot, probe, "", stop_after_transient)
        assert held["restored"] and transport.raw == old
        assert transport.writes[before:] == [expected, expected, old]
        assert transient not in transport.writes[before:]
        assert not Path(args.journal).exists()

        # A persistent third value fails closed, and recovery performs no
        # additional unknown-value retries or writes.
        args.journal = str(Path(directory) / "hold-persistent-unknown.json")
        transport.raw = old
        def persistent_unknown_snapshot():
            return snapshot
        controls = [0]
        def remain_unknown(_wait):
            controls[0] += 1
            if controls[0] == 1:
                transport.raw = transient
            return False
        before = len(transport.writes)
        try:
            client.run_hold(args, transport, persistent_unknown_snapshot, probe, "", remain_unknown)
        except RuntimeError as error:
            assert "unknown value" in str(error) and "recovery blocked" in str(error)
        else:
            raise AssertionError("hold accepted a persistent third TValue")
        assert transport.raw == transient and transport.writes[before:] == [expected]
        assert Path(args.journal).exists()
        Path(args.journal).unlink()
        args.persistent = False

        args.write = False
        try:
            client.run_action(args, transport, provider, probe)
        except ValueError as error:
            assert "requires --write" in str(error)
        else:
            raise AssertionError("hold ran without explicit --write")
        args.write = True
        args.journal = str(journal)
        args.action = "set"
        assert client.encode_speed(420.0, old, probe) == struct.pack("<Q", ((0xFFFFFFF2 & 0x1FFFF) << 47) | 420)
        for invalid in (0.0, -1.0, float("inf"), float("nan")):
            try:
                client.encode_speed(invalid, old, probe)
            except ValueError:
                pass
            else:
                raise AssertionError(f"accepted invalid speed {invalid}")
        transport.raw = expected
        args.mount_id = 42
        player["mountId"] = 43
        try:
            client.run_action(args, transport, provider, probe)
        except RuntimeError:
            pass
        else:
            raise AssertionError("accepted mismatched mount snapshot")
    print("MountSpeedMemoryTestRegression: PASS")


if __name__ == "__main__":
    main()
