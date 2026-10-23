"""Offline regression tests for the standalone battle status reader."""
from __future__ import annotations

import importlib.util
import struct
import sys
import threading
import unittest
from pathlib import Path
from unittest.mock import patch


HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("battle_reader_under_test", HERE / "battle_reader.py")
br = importlib.util.module_from_spec(SPEC)
assert SPEC and SPEC.loader
SPEC.loader.exec_module(br)
sys.path.insert(0, str(HERE))
import main as panel_module


class FakeStringVar:
    def __init__(self, value=""):
        self.value = value
    def set(self, value):
        self.value = value
    def get(self):
        return self.value


class FakeSession:
    def __init__(self):
        self.closed = False
    def close(self):
        self.closed = True


class SparseReader:
    """Addressed bytes fixture; lua.read_gc_table remains the production parser."""
    def __init__(self, blobs):
        self.blobs = blobs
        self.ranges = [(min(blobs), max(a + len(b) for a, b in blobs.items()) + 0x1000)]

    def read(self, address, size):
        for start, blob in self.blobs.items():
            if start <= address and address + size <= start + len(blob):
                offset = address - start
                return blob[offset:offset + size]
        return b""


def gc_table_header(address: int) -> bytes:
    data = bytearray(64)
    data[9] = br.lua.GCT_TAB
    struct.pack_into("<Q", data, 40, address + 0x100)
    struct.pack_into("<I", data, 52, 0)
    return bytes(data)


class UserdataEnvTests(unittest.TestCase):
    def test_gc64_raw_absolute_env_pointer_above_32_bits(self):
        userdata, env = 0x100001000, 0x700001000
        header = bytearray(24)
        header[9] = br.GCT_UDATA
        struct.pack_into("<Q", header, 16, env)
        reader = SparseReader({userdata: bytes(header), env: gc_table_header(env)})
        self.assertEqual(br._userdata_env(reader, userdata), env)

    def test_rejects_wrong_gc_type_and_invalid_or_out_of_range_env(self):
        userdata, env = 0x100001000, 0x700001000
        wrong_type = bytearray(24)
        struct.pack_into("<Q", wrong_type, 16, env)
        with self.assertRaises(br.ReadFailure):
            br._userdata_env(SparseReader({userdata: bytes(wrong_type), env: gc_table_header(env)}), userdata)
        valid_type = bytearray(wrong_type)
        valid_type[9] = br.GCT_UDATA
        struct.pack_into("<Q", valid_type, 16, 0x800000000)
        with self.assertRaises(br.ReadFailure):
            br._userdata_env(SparseReader({userdata: bytes(valid_type), env: gc_table_header(env)}), userdata)
        struct.pack_into("<Q", valid_type, 16, env)
        invalid_table = bytearray(gc_table_header(env))
        invalid_table[9] = 0
        with self.assertRaises(br.ReadFailure):
            br._userdata_env(SparseReader({userdata: bytes(valid_type), env: bytes(invalid_table)}), userdata)


class RoleMappingTests(unittest.TestCase):
    def test_role_array_fixture_with_nil_zero_keeps_real_positions(self):
        # Captured role table shape: arr[0] is nil; arr[1..5] are ownmain slots 1..5.
        values = [{"kind": "nil"}] + [
            {"kind": "tagged", "raw": f"0x{0x9000 + i:x}"} for i in range(1, 6)
        ]
        tagged = {f"0x{0x9000 + i:x}": 0x9000 + i for i in range(1, 6)}
        with patch.object(br.lua, "read_gc_table", return_value={"address": 1}), \
             patch.object(br.lua, "read_gc_table_nodes", return_value=[]), \
             patch.object(br.lua, "read_gc_table_array", return_value=values), \
             patch.object(br.lua, "tagged_pointer_from_value", side_effect=lambda v, _tag: tagged.get(v.get("raw")) if isinstance(v, dict) else None):
            indexed = br._userdata_index(object(), 1)
        self.assertEqual(indexed, {i: 0x9000 + i for i in range(1, 6)})

    def make_role_fixture(self, local_position=3):
        # Thirteen battle entries: five friendly, eight opposing. The opposing
        # child position and an unkeyed helper must not enter the ten fixed slots.
        position_to_role = {p: 100 + p for p in range(1, 6)}
        position_to_role.update({p: 200 + p for p in range(10001, 10006)})
        position_to_role.update({p: 300 + p for p in range(10101, 10103)})
        position_to_role[201] = 999
        position_by_role = {role: pos for pos, role in position_to_role.items()}
        position_by_role[100 + local_position] = local_position
        # Add the remaining friendlies and make all 13 ordinary battle entries.
        for pos in range(1, 6):
            position_by_role[100 + pos] = pos
        position_by_role.pop(100 + local_position, None)
        position_by_role.update({role: pos for pos, role in position_to_role.items()})
        # role 103 is local, and enemy keys include main + pet positions.
        role_fields = {}
        userdata_index = {}
        for pos, role in position_to_role.items():
            userdata = 0x5000 + pos
            userdata_index[pos] = userdata
            role_fields[userdata] = {
                "m_RoleId": role, "m_IsLocalPlayer": pos == local_position,
                "m_IsAlive": True, "m_Pos": pos, "m_Battlefield": 0xABC,
                "m_Hp": 90 + pos, "m_Mp": 40 + pos, "m_Speed": 300 + pos,
            }
        # Ensure manager local role is represented by the bound role id.
        local_role_id = 100 + local_position
        local_userdata = 0x6000
        userdata_index[local_position] = local_userdata
        role_fields[local_userdata] = {
            "m_RoleId": local_role_id, "m_IsLocalPlayer": True,
            "m_IsAlive": True, "m_Pos": local_position, "m_Battlefield": 0xABC,
            "m_Hp": 777, "m_Mp": 888, "m_Speed": 999,
        }
        position_by_role[local_role_id] = local_position
        return position_by_role, userdata_index, role_fields, local_role_id

    def run_fixture(self, local_position=3, position_by_role=None, role_fields=None):
        mapping, index, fields, local_id = self.make_role_fixture(local_position)
        mapping = position_by_role or mapping
        fields = role_fields or fields
        with patch.object(br, "_position_mapping", return_value=mapping), \
             patch.object(br, "_userdata_index", return_value=index), \
             patch.object(br, "_userdata_env", side_effect=lambda _r, u: u), \
             patch.object(br, "_field_map", side_effect=lambda _r, env: fields[env]), \
             patch.object(br, "_unwrap", side_effect=lambda _r, value: value), \
             patch.object(br.lua, "tagged_pointer_from_value", return_value=0xABC):
            return br._read_roles(object(), 1, 2, 0xABC, local_id)

    def test_large_roster_filters_only_expected_ten_position_families(self):
        slots, ids, own_slots, own_ids, local = self.run_fixture()
        self.assertEqual(ids, list(range(10001, 10006)) + list(range(10101, 10106)))
        self.assertEqual(local, 3)
        self.assertEqual(set(slots), set(range(10001, 10006)) | set(range(10101, 10103)))
        self.assertEqual(own_ids, list(range(1, 6)) + list(range(101, 106)))
        self.assertEqual(own_slots[3]["hp"], 777)
        self.assertEqual(own_slots[3]["mp"], 888)
        self.assertEqual(own_slots[3]["speed"], 999)
        self.assertEqual(own_slots[3]["side"], "本方")
        self.assertNotIn(201, own_ids + ids)

    def test_reverse_side_mapping_and_bad_mapped_role_fail_closed(self):
        mapping, index, fields, local_id = self.make_role_fixture(local_position=10003)
        # Reverse mapping contract: local 10003 -> enemy 1..5 and 101..105.
        mapping = {local_id: 10003, 501: 1, 502: 2, 503: 3, 504: 4, 505: 5,
                   601: 101, 602: 102, 603: 103, 604: 104, 605: 105}
        fields = {userdata: value for userdata, value in fields.items()}
        for pos, role in [(1, 501), (2, 502), (3, 503), (4, 504), (5, 505),
                          (101, 601), (102, 602), (103, 603), (104, 604), (105, 605)]:
            u = 0x7000 + pos
            index[pos] = u
            fields[u] = {"m_RoleId": role, "m_IsLocalPlayer": False, "m_IsAlive": True,
                         "m_Pos": pos, "m_Battlefield": 0xABC, "m_Hp": 1, "m_Mp": 2, "m_Speed": 3}
        u = 0x8000
        index[10003] = u
        fields[u] = {"m_RoleId": local_id, "m_IsLocalPlayer": True, "m_IsAlive": True,
                     "m_Pos": 10003, "m_Battlefield": 0xABC,
                     "m_Hp": 10, "m_Mp": 20, "m_Speed": 30}
        with patch.object(br, "_position_mapping", return_value=mapping), \
             patch.object(br, "_userdata_index", return_value=index), \
             patch.object(br, "_userdata_env", side_effect=lambda _r, ud: ud), \
             patch.object(br, "_field_map", side_effect=lambda _r, env: fields[env]), \
             patch.object(br, "_unwrap", side_effect=lambda _r, value: value), \
             patch.object(br.lua, "tagged_pointer_from_value", return_value=0xABC):
            slots, ids, own_slots, own_ids, local = br._read_roles(object(), 1, 2, 0xABC, local_id)
        self.assertEqual(ids, list(range(1, 6)) + list(range(101, 106)))
        self.assertEqual(local, 10003)
        self.assertEqual(len(slots), 10)
        self.assertEqual(own_ids, list(range(10001, 10006)) + list(range(10101, 10106)))
        self.assertEqual(own_slots[10003]["side"], "本方")
        self.assertTrue(own_slots[10003]["local"])

        bad_fields = dict(fields)
        bad_fields[index[1]] = dict(fields[index[1]], m_RoleId=999999)
        with patch.object(br, "_position_mapping", return_value=mapping), \
             patch.object(br, "_userdata_index", return_value=index), \
             patch.object(br, "_userdata_env", side_effect=lambda _r, ud: ud), \
             patch.object(br, "_field_map", side_effect=lambda _r, env: bad_fields[env]), \
             patch.object(br, "_unwrap", side_effect=lambda _r, value: value), \
             patch.object(br.lua, "tagged_pointer_from_value", return_value=0xABC):
            with self.assertRaises(br.ReadFailure):
                br._read_roles(object(), 1, 2, 0xABC, local_id)


class SerialAndUiContractTests(unittest.TestCase):
    @staticmethod
    def _manager_candidate_fixture(fields_by_address, cancel_event=None, cancel_after_scan=False):
        reader = type("Reader", (), {"pages": {}, "accessed_pages": set(),
                                     "ranges": [(0x1000, 0x2000)]})()
        needle_value = 0x1122334455667788
        blob = bytearray(4096)
        blob[16:24] = needle_value.to_bytes(8, "little")

        def read_chunk(*_args):
            if cancel_after_scan and cancel_event is not None:
                cancel_event.set()
            return bytes(blob)

        headers = br.array("Q", [0xA000, 0x1000, 0x1010, 0xB000, 0x1000, 0x1010])
        patches = (
            patch.object(br.lua, "gc64_tagged_pointer", return_value=needle_value),
            patch.object(br, "_node_scan_intervals", return_value=[(0x1000, 0x2000)]),
            patch.object(br, "CHUNK_SIZE", 4096),
            patch.object(br, "_read_scan_chunk", side_effect=read_chunk),
        )
        return reader, headers, patches

    def test_manager_candidate_loop_skips_bad_candidate_but_requires_unique_valid_manager(self):
        valid = {field: object() for field in br.MANAGER_FIELDS}
        cases = (
            ({0xA000: br.ReadFailure("bad candidate"), 0xB000: valid}, 0xB000),
            ({0xA000: valid, 0xB000: valid}, None),
        )
        for candidates, expected_address in cases:
            reader, headers, patchers = self._manager_candidate_fixture(candidates)
            with patchers[0], patchers[1], patchers[2], patchers[3], \
                 patch.object(br, "_field_map", side_effect=lambda _r, address: self._candidate_fields(candidates, address)):
                if expected_address is None:
                    with self.assertRaisesRegex(br.ReadFailure, "匹配数为 2"):
                        br._find_manager(reader, {"m_RoleIns": [0x1234]}, headers)
                else:
                    self.assertEqual(br._find_manager(reader, {"m_RoleIns": [0x1234]}, headers),
                                     (expected_address, valid))

    @staticmethod
    def _candidate_fields(candidates, address):
        value = candidates[address]
        if isinstance(value, BaseException):
            raise value
        return value

    def test_manager_candidate_cancellation_propagates_and_checks_each_candidate(self):
        valid = {field: object() for field in br.MANAGER_FIELDS}
        stop = threading.Event()
        reader, headers, patchers = self._manager_candidate_fixture({0xA000: valid, 0xB000: valid})
        with patchers[0], patchers[1], patchers[2], patchers[3], \
             patch.object(br, "_field_map", side_effect=br.ReadCancelled("candidate cancelled")):
            with self.assertRaises(br.ReadCancelled):
                br._find_manager(reader, {"m_RoleIns": [0x1234]}, headers)

        stop = threading.Event()
        reader, headers, patchers = self._manager_candidate_fixture({0xA000: valid, 0xB000: valid}, stop,
                                                                      cancel_after_scan=True)
        field_map = unittest.mock.Mock(return_value=valid)
        with patchers[0], patchers[1], patchers[2], patchers[3], \
             patch.object(br, "_field_map", field_map):
            with self.assertRaises(br.ReadCancelled):
                br._find_manager(reader, {"m_RoleIns": [0x1234]}, headers, stop)
        field_map.assert_not_called()

    def test_blank_serial_requires_exactly_one_online_device_and_explicit_is_preserved(self):
        with patch.object(br, "_adb", return_value=b"List of devices attached\nabc device\n"):
            self.assertEqual(br._resolve_serial("adb", ""), "abc")
            self.assertEqual(br._resolve_serial("adb", "explicit-serial"), "explicit-serial")
        with patch.object(br, "_adb", return_value=b"List of devices attached\na device\nb device\n"):
            with self.assertRaises(br.ReadFailure):
                br._resolve_serial("adb", "")

    def test_failure_render_path_clears_slot_values_and_static_autostart(self):
        source = (HERE / "main.py").read_text(encoding="utf-8")
        self.assertIn("root.after(100, self.start)", source)
        self.assertIn('if state != "ok":', source)
        self.assertIn("self._show_side_reason(side, label)", source)
        values = [FakeStringVar("stale HP 123") for _ in range(10)]
        panel = panel_module.Panel.__new__(panel_module.Panel)
        panel.run_id = 4
        panel.stop_event = __import__("threading").Event()
        panel.latest = None
        panel.latest_at = 0.0
        panel.status, panel.meta = FakeStringVar(), FakeStringVar()
        panel.position_order = [*range(10001, 10006), *range(10101, 10106)]
        panel.position_mapping_session = (1, 2, "exe", 99)
        panel.slot_vars = values
        panel.side_slots = {"本方": [FakeStringVar("stale") for _ in range(10)], "对方": values}
        panel.side_position_orders = {"本方": [], "对方": panel.position_order}
        panel.side_mapping_sessions = {"本方": None, "对方": None}
        panel._show_reason = lambda reason: [var.set(reason) for var in values]
        panel._render({"state": "failed", "message": "读取失败", "run_id": 4})
        self.assertEqual(panel.position_order, [*range(10001, 10006), *range(10101, 10106)])
        self.assertTrue(all(value.get() == "读取失败" for value in values))

    def test_fixed_slot_coordinates_match_both_client_families_without_overlap(self):
        for positions in (
            [*range(10001, 10006), *range(10101, 10106)],
            [*range(1, 6), *range(101, 106)],
        ):
            points = panel_module._slot_coordinates(positions)
            self.assertEqual(len(points), 10)
            rectangles = [(x, y, x + 180, y + 86) for x, y in points]
            for i, a in enumerate(rectangles):
                for b in rectangles[i + 1:]:
                    self.assertTrue(a[2] <= b[0] or b[2] <= a[0] or a[3] <= b[1] or b[3] <= a[1],
                                    f"overlapping card rectangles: {a} and {b}")
        with self.assertRaises(ValueError):
            panel_module._slot_coordinates([*range(10201, 10206), *range(10301, 10306)])

    def test_pre_cancelled_sample_stops_before_adb_discovery_or_binding_write(self):
        stop = threading.Event()
        stop.set()
        with patch.object(br, "find_adb") as find_adb, \
             patch.object(br, "_cold_discover") as discover, \
             patch.object(br, "_update_manager_cache") as update:
            with self.assertRaises(br.ReadCancelled):
                br.sample(stop_event=stop)
        find_adb.assert_not_called()
        discover.assert_not_called()
        update.assert_not_called()

    def test_discovery_scan_cancellation_discards_partial_result_and_closes_worker_session(self):
        stop = threading.Event()
        sessions = []
        def fake_read(reader, *_args):
            session = FakeSession()
            reader._scan_session = session
            sessions.append(session)
            stop.set()
            return b"\0" * 4096

        reader = type("Reader", (), {"pages": {}, "accessed_pages": set(), "ranges": [(0x1000, 0x4000)]})()
        descriptors = [(1, 0x4000, 0x1000, 0x2000, 4096), (2, 0x4000, 0x2000, 0x3000, 4096)]
        with patch.object(br, "_discovery_chunks", return_value=descriptors), \
             patch.object(br, "DISCOVERY_WORKERS", 1), \
             patch.object(br, "_read_scan_chunk", side_effect=fake_read), \
             patch.object(br, "_index_table_headers"), \
             patch.object(br.lua, "find_all", return_value=[]):
            with self.assertRaises(br.ReadCancelled):
                br._find_discovery_indexes(reader, stop)
        self.assertEqual(len(sessions), 1, "second descriptor should not be scanned after cancellation")
        self.assertTrue(sessions[0].closed, "worker must close its private scan session")

    def test_discovery_failure_does_not_set_user_cancel_event(self):
        user_stop = threading.Event()
        reader = type("Reader", (), {"pages": {}, "accessed_pages": set(), "ranges": [(0x1000, 0x2000)]})()
        descriptors = [(1, 0x2000, 0x1000, 0x2000, 4096)]
        with patch.object(br, "_discovery_chunks", return_value=descriptors), \
             patch.object(br, "DISCOVERY_WORKERS", 1), \
             patch.object(br, "_read_scan_chunk", side_effect=br.ReadFailure("synthetic scan failure")):
            with self.assertRaises(br.ReadFailure):
                br._find_discovery_indexes(reader, user_stop)
        self.assertFalse(user_stop.is_set(), "internal fail-stop must not masquerade as a user stop request")

    def test_manager_scan_cancellation_stops_before_next_chunk(self):
        stop = threading.Event()
        reader = type("Reader", (), {"pages": {}, "accessed_pages": set(), "ranges": [(0x1000, 0x3000)]})()
        calls = []
        def read_chunk(*_args):
            calls.append(1)
            stop.set()
            return b"\0" * 4096
        with patch.object(br, "_node_scan_intervals", return_value=[(0x1000, 0x3000)]), \
             patch.object(br, "CHUNK_SIZE", 4096), \
             patch.object(br, "_read_scan_chunk", side_effect=read_chunk), \
             patch.object(br, "_release_scan_chunk"):
            with self.assertRaises(br.ReadCancelled):
                br._find_manager(reader, {"m_RoleIns": [0x1100]}, __import__("array").array("Q"), stop)
        self.assertEqual(len(calls), 1, "manager scan must stop between bounded chunks")

    def test_freshness_does_not_overwrite_failed_or_stopped_status(self):
        class Thread:
            def is_alive(self):
                return True
        for state, stopped in (("failed", False), ("ok", True)):
            panel = panel_module.Panel.__new__(panel_module.Panel)
            panel.thread = Thread()
            panel.stop_event = threading.Event()
            if stopped:
                panel.stop_event.set()
            panel.latest = {"state": state, "message": "原始读取诊断"}
            panel.latest_at = 1.0
            panel.status = FakeStringVar("读取失败" if state == "failed" else "已停止")
            panel.slot_vars = [FakeStringVar("原始读取诊断") for _ in range(10)]
            panel.side_slots = {"本方": [FakeStringVar("原始读取诊断") for _ in range(10)], "对方": panel.slot_vars}
            panel._show_reason = lambda reason: [value.set(reason) for value in panel.slot_vars]
            panel.root = type("Root", (), {"after": lambda *_args: None})()
            with patch.object(panel_module.time, "monotonic", return_value=100.0):
                panel._freshness()
            self.assertEqual(panel.status.get(), "读取失败" if state == "failed" else "已停止")
            self.assertTrue(all(v.get() == "原始读取诊断" for v in panel.slot_vars))

    def test_freshness_clears_old_hp_on_both_sides_and_initializes_both_layouts(self):
        class Thread:
            def is_alive(self):
                return True
        panel = panel_module.Panel.__new__(panel_module.Panel)
        panel.thread = Thread()
        panel.stop_event = threading.Event()
        panel.latest = {"state": "ok"}
        panel.latest_at = 1.0
        panel.status = FakeStringVar("实时")
        panel.side_slots = {
            "本方": [FakeStringVar("HP 123") for _ in range(10)],
            "对方": [FakeStringVar("HP 456") for _ in range(10)],
        }
        panel.root = type("Root", (), {"after": lambda *_args: None})()
        with patch.object(panel_module.time, "monotonic", return_value=100.0):
            panel._freshness()
        self.assertEqual(panel.status.get(), "数据过期")
        self.assertTrue(all(var.get() == "数据过期，等待新读取"
                            for side in panel.side_slots.values() for var in side))
        source = (HERE / "main.py").read_text(encoding="utf-8")
        self.assertIn('self._place_side_slots("本方", [*range(1, 6), *range(101, 106)])', source)
        self.assertIn('self._place_slots([*range(10001, 10006), *range(10101, 10106)])', source)

    def test_sample_flushes_reader_cache_and_rejects_changed_position_mapping(self):
        class Reader:
            def __init__(self, *_args):
                self.ranges = [(0x1000, 0x2000)]
                self.pages = {0x1000: b"cached pre-frame"}
                self.accessed_pages = {0x1000}
            def close(self):
                pass

        identity = {"pid": 7, "startTicks": 9, "exe": "/game"}
        manager_reads = 0
        def manager_values(_reader, _manager):
            nonlocal manager_reads
            manager_reads += 1
            if manager_reads == 1:
                return ({"m_PlayerId": 77}, 123, 4, 0x1100, 0x1200)
            self.assertEqual(_reader.pages, {}, "post-frame manager read reused cached page")
            self.assertEqual(_reader.accessed_pages, set(), "post-frame read reused access set")
            return ({"m_PlayerId": 77}, 123, 4, 0x1100, 0x1200)

        mappings = iter([{1: 77, 77: 3}, {1: 77, 77: 4}])
        with patch.object(br, "find_adb", return_value="adb"), \
             patch.object(br, "_resolve_serial", return_value="device"), \
             patch.object(br, "_pid", return_value=7), \
             patch.object(br, "_identity", return_value=identity), \
             patch.object(br, "_root_adb", return_value=b"1000-2000 rw-p 00000000 00:00 0\\n"), \
             patch.object(br.lua, "parse_maps", return_value=[(0x1000, 0x2000)]), \
             patch.object(br.lua, "BoundedPageMemoryReader", Reader), \
             patch.object(br, "_load_manager_cache", return_value=0x1300), \
             patch.object(br, "_manager_values", side_effect=manager_values), \
             patch.object(br, "_position_mapping", side_effect=lambda *_: next(mappings)), \
             patch.object(br.lua, "numeric_table_key", return_value=77), \
             patch.object(br, "_unwrap", side_effect=lambda _r, value: value), \
             patch.object(br, "_read_roles", return_value=({}, list(range(10001, 10006)) + list(range(10101, 10106)), {}, list(range(1, 6)) + list(range(101, 106)), 3)), \
             patch.object(br, "_update_manager_cache"), \
             patch.object(br, "_binding", {"identity": ("device", "com.gdoo.xxxy2026", 7, 9, "/game"), "manager": 0x1300}, create=True):
            with self.assertRaisesRegex(br.ReadFailure, "整帧丢弃"):
                br.sample("device")


class WindowlessProcessTests(unittest.TestCase):
    def test_panel_and_private_probe_hide_windows_children(self):
        with patch.object(br.os, "name", "nt"), \
             patch.object(br.subprocess, "CREATE_NO_WINDOW", 0x08000000, create=True), \
             patch.object(br.subprocess, "run") as run, \
             patch.object(br.subprocess, "Popen") as popen:
            run.return_value.returncode = 0
            run.return_value.stdout = b"ok"
            self.assertEqual(br._adb("adb", "device", "devices"), b"ok")
            self.assertEqual(run.call_args.kwargs["creationflags"], 0x08000000)
            br.lua.run_adb("adb", "device", ["shell", "true"], 10)
            self.assertEqual(run.call_args.kwargs["creationflags"], 0x08000000)
            br._panel_processes.Popen(["adb"], creationflags=0x200)
            self.assertEqual(popen.call_args.kwargs["creationflags"], 0x08000200)
            br.lua.subprocess.Popen(["adb"])
            self.assertEqual(popen.call_args.kwargs["creationflags"], 0x08000000)
            self.assertIs(br.lua.subprocess.TimeoutExpired, br.subprocess.TimeoutExpired)
            self.assertIsNot(br.lua.subprocess, br.subprocess)

    def test_non_windows_process_launch_is_unchanged(self):
        with patch.object(br.os, "name", "posix"), \
             patch.object(br.subprocess, "run") as run, \
             patch.object(br.subprocess, "Popen") as popen:
            br._panel_processes.run(["adb"])
            br._panel_processes.Popen(["adb"])
            self.assertNotIn("creationflags", run.call_args.kwargs)
            self.assertNotIn("creationflags", popen.call_args.kwargs)


if __name__ == "__main__":
    unittest.main(verbosity=2)
