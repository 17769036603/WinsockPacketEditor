"""Offline regression for guarded ride growth writes and restore."""
from __future__ import annotations

import importlib.util
import json
import struct
import tempfile
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools/mount-reader/mount_growth_rate_memory_test.py"
PROBE = ROOT / "tools/mount-reader/mount_status_luajit_probe.py"


def load(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class FakeTransport:
    def __init__(self, raw, fail_write=False, fail_readback=False, fail_after_write=False):
        self.raw, self.writes, self.memory = raw, [], {0x123456: raw}
        self.fail_write, self.fail_readback, self.fail_after_write, self.reads = fail_write, fail_readback, fail_after_write, 0

    def read(self, address, size):
        assert size == 8
        self.reads += 1
        if self.fail_readback and self.reads >= 2:
            raise OSError("simulated readback failure")
        return self.memory.get(address, self.raw)

    def write(self, address, raw, expected_start_ticks=None, expected_raw=None):
        assert expected_start_ticks == 9876
        assert self.memory.get(address, self.raw) == expected_raw
        if self.fail_write:
            raise OSError("simulated write failure")
        self.writes.append(raw)
        self.raw = raw
        self.memory[address] = raw
        if self.fail_after_write:
            raise OSError("simulated post-write failure")


def snapshot(address="0x123456", ride_id="2092100442933833732", growth=1.175, start=9876):
    return {"status": "ok", "readOnly": True, "actionAuthorized": False, "candidateCount": 1,
        "process": {"pid": 321, "startTicks": start, "exe": "/system/bin/app_process64"},
        "player": {"playerId": 66, "mountId": 9111, "isLocalPlayer": True, "isMounted": True,
            "rideBindingStatus": "bound", "activeRideInstanceId": ride_id,
            "rideInstances": [{"rideInstanceId": ride_id, "isCurrent": True, "isRiding": True,
                "growthRate": growth, "growthRateAddress": address}]}}


def main():
    client, probe = load(SCRIPT, "growth_writer_test"), load(PROBE, "growth_probe_test")
    with tempfile.TemporaryDirectory() as directory:
        journal = str(Path(directory) / "growth.json")
        args = SimpleNamespace(target=10.935, ride_instance_id="2092100442933833732", pid=321,
            expected_start_ticks=9876, expected_exe="/system/bin/app_process64", expected_player_id=66,
            expected_mount_id=9111, expected_growth_rate=1.175, expected_growth_address=0x123456,
            journal=journal, run_as_package="com.example.game", serial="emulator-5554", write=False)
        old, target = struct.pack("<d", 1.175), struct.pack("<d", 10.935)
        assert client.encode_target(10.935, old, probe) == target
        transport = FakeTransport(old)

        def live_snapshot():
            return snapshot(growth=probe.decode_tvalue(transport.raw)["value"])

        dry = client.run(args, transport, live_snapshot, probe)
        assert dry["status"] == "dry_run" and dry["newValue"] == 10.935
        assert not dry["writeAttempted"] and not transport.writes and not Path(journal).exists()
        args.write = True
        set_result = client.run(args, transport, live_snapshot, probe)
        assert set_result["status"] == "field_verified" and transport.raw == target
        assert transport.writes == [target]
        data = json.loads(Path(journal).read_text(encoding="utf-8"))
        assert data["binding"]["oldRaw"] == old.hex() and data["binding"]["targetRaw"] == target.hex()
        assert data["binding"]["serial"] == args.serial

        # Restore rejects stale process/binding/value and never writes.
        drifted = [snapshot(start=9877, growth=10.935),
                   snapshot(ride_id="2092100442933833799", growth=10.935),
                   snapshot(growth=10.934)]
        for bad in drifted:
            before = len(transport.writes)
            try:
                result = client.restore(args, transport, lambda bad=bad: bad, probe)
            except RuntimeError:
                raise AssertionError("restore should return a guarded skip result")
            assert result["status"] == "skipped"
            assert len(transport.writes) == before and Path(journal).exists()
        args.serial = "emulator-other"
        writes_before_serial = len(transport.writes)
        try:
            client.restore(args, transport, live_snapshot, probe)
        except RuntimeError:
            pass
        else:
            raise AssertionError("restore accepted a different ADB serial")
        assert len(transport.writes) == writes_before_serial and Path(journal).exists()
        args.serial = "emulator-5554"

        # A syntactically valid but non-finite prior value is not restorable.
        corrupt = json.loads(Path(journal).read_text(encoding="utf-8"))
        corrupt["binding"]["oldRaw"] = struct.pack("<d", float("nan")).hex()
        Path(journal).write_text(json.dumps(corrupt), encoding="utf-8")
        before = len(transport.writes)
        try:
            client.restore(args, transport, live_snapshot, probe)
        except RuntimeError:
            pass
        else:
            raise AssertionError("restore accepted a non-finite journaled old value")
        assert len(transport.writes) == before and transport.raw == target
        # A finite, same-kind but altered prior value is also rejected.
        corrupt["binding"]["oldRaw"] = struct.pack("<d", 7.0).hex()
        Path(journal).write_text(json.dumps(corrupt), encoding="utf-8")
        before = len(transport.writes)
        try:
            client.restore(args, transport, live_snapshot, probe)
        except RuntimeError:
            pass
        else:
            raise AssertionError("restore accepted journaled old value that differs from expected growth")
        assert len(transport.writes) == before and transport.raw == target
        Path(journal).write_text(json.dumps(data), encoding="utf-8")
        transport.raw = struct.pack("<d", 7.0)
        transport.memory[0x123456] = transport.raw
        skipped = client.restore(args, transport, live_snapshot, probe)
        assert skipped["status"] == "skipped"

        transport.raw = target
        transport.memory[0x123456] = target
        restored = client.restore(args, transport, live_snapshot, probe)
        assert restored["status"] == "restored" and transport.raw == old
        assert transport.writes[-1] == old and not Path(journal).exists()

        # Stop resolves a relocated field from the fresh snapshot, not the journal address.
        relocated = FakeTransport(old)
        relocated.memory[0xABCDEF] = target
        Path(journal).write_text(json.dumps(data), encoding="utf-8")
        relocated_snapshot = lambda: snapshot(address="0xABCDEF",
            growth=probe.decode_tvalue(relocated.memory[0xABCDEF])["value"])
        relocated_result = client.restore(args, relocated, relocated_snapshot, probe)
        assert relocated_result["status"] == "restored"
        assert relocated.writes == [old] and relocated.memory[0xABCDEF] == old

        # Cleanup failure follows a verified memory restore and must report
        # written=true while retaining the journal for manual cleanup/retry.
        cleanup_transport = FakeTransport(target)
        Path(journal).write_text(json.dumps(data), encoding="utf-8")
        cleanup_snapshot = lambda: snapshot(growth=probe.decode_tvalue(cleanup_transport.raw)["value"])
        with patch.object(Path, "unlink", side_effect=PermissionError("simulated journal cleanup failure")):
            cleanup = client.restore(args, cleanup_transport, cleanup_snapshot, probe)
        assert cleanup["status"] == "restored" and cleanup["written"] is True
        assert cleanup["journalRetained"] is True and "simulated journal cleanup failure" in cleanup["journalCleanupError"]
        assert cleanup_transport.raw == old and Path(journal).exists()
        Path(journal).unlink()

        # An already-active target is verified without a redundant write/journal.
        args.target = 1.175
        before = len(transport.writes)
        no_op = client.run(args, transport, live_snapshot, probe)
        assert no_op["status"] == "field_verified" and not no_op["writeAttempted"]
        assert len(transport.writes) == before and not Path(journal).exists()
        args.target = 10.935

        # Restore transport failures stay ambiguous and preserve the journal,
        # including a write that took effect before the transport raised.
        for failed in (FakeTransport(target, fail_after_write=True),
                       FakeTransport(target, fail_readback=True)):
            Path(journal).write_text(json.dumps(data), encoding="utf-8")
            outcome = client.restore(args, failed, lambda: snapshot(growth=10.935), probe)
            assert outcome["status"] == "ambiguous" and outcome["writeAttempted"]
            assert Path(journal).exists()
            Path(journal).unlink()

        # A post-restore snapshot drift is ambiguous after the side effect and
        # must leave recovery evidence in place.
        post_drift = FakeTransport(target)
        Path(journal).write_text(json.dumps(data), encoding="utf-8")
        calls = [0]
        def drift_after_restore():
            calls[0] += 1
            return snapshot(growth=10.935 if calls[0] == 1 else 9.0)
        outcome = client.restore(args, post_drift, drift_after_restore, probe)
        assert outcome["status"] == "ambiguous" and outcome["writeAttempted"]
        assert post_drift.raw == old and Path(journal).exists()
        Path(journal).unlink()

        # Failed restoration never claims success and keeps the journal for retry.
        restore_write_failure = FakeTransport(target, fail_write=True)
        Path(journal).write_text(json.dumps(data), encoding="utf-8")
        failure_snapshot = lambda: snapshot(growth=probe.decode_tvalue(restore_write_failure.raw)["value"])
        ambiguous_restore = client.restore(args, restore_write_failure, failure_snapshot, probe)
        assert ambiguous_restore["status"] == "ambiguous" and ambiguous_restore["writeAttempted"]
        assert Path(journal).exists()
        Path(journal).unlink()

        restore_read_failure = FakeTransport(target, fail_readback=True)
        Path(journal).write_text(json.dumps(data), encoding="utf-8")
        failure_snapshot = lambda: snapshot(growth=probe.decode_tvalue(restore_read_failure.raw)["value"])
        ambiguous_restore = client.restore(args, restore_read_failure, failure_snapshot, probe)
        assert ambiguous_restore["status"] == "ambiguous" and ambiguous_restore["writeAttempted"]
        assert Path(journal).exists() and restore_read_failure.raw == old
        Path(journal).unlink()

        # Set rebinds to the latest address after startup and validates there.
        transport = FakeTransport(old)
        args.write = True
        calls = [0]
        def moved_address():
            calls[0] += 1
            address = "0x123456" if calls[0] == 1 else "0x123457"
            return snapshot(address=address,
                growth=probe.decode_tvalue(transport.memory.get(int(address, 16), transport.raw))["value"])
        transport.memory[0x123457] = old
        moved_set = client.run(args, transport, moved_address, probe)
        assert moved_set["status"] == "field_verified"
        assert transport.writes == [target] and transport.memory[0x123457] == target
        assert json.loads(Path(journal).read_text(encoding="utf-8"))["binding"]["address"] == 0x123457
        Path(journal).unlink()

        # Startup value or ride identity drift during rebind must fail closed.
        for stale_snapshot in (
            lambda: snapshot(growth=1.176),
            lambda: snapshot(start=9877),
        ):
            guarded = FakeTransport(old)
            try:
                client.run(args, guarded, stale_snapshot, probe)
            except RuntimeError:
                pass
            else:
                raise AssertionError("set accepted stale startup value/process identity")
            assert not guarded.writes and not Path(journal).exists()

        failed_write = FakeTransport(old, fail_write=True)
        ambiguous = client.run(args, failed_write, lambda: snapshot(), probe)
        assert ambiguous["status"] == "ambiguous" and ambiguous["writeAttempted"]
        assert "simulated write failure" in ambiguous["error"]
        assert Path(journal).exists()
        Path(journal).unlink()

        failed_readback = FakeTransport(old, fail_readback=True)
        ambiguous = client.run(args, failed_readback, lambda: snapshot(), probe)
        assert ambiguous["status"] == "ambiguous" and ambiguous["writeAttempted"]
        assert "simulated readback failure" in ambiguous["error"]
        assert failed_readback.raw == target and Path(journal).exists()
        Path(journal).unlink()
    print("MountGrowthRateMemoryWriteRegression: PASS")


if __name__ == "__main__":
    main()
