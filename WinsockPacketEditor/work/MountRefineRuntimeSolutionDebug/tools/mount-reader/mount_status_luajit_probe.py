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
import json
import math
import re
import struct
import subprocess
import sys
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

NODE_STRIDE = 24
GCTAB_SIZE = 64
MAX_HASH_MASK = 0x1000
MAX_TABLE_ARRAY_SIZE = 4096
MAX_RIDE_INSTANCES = 64
MAX_RIDE_SKILLS_PER_INSTANCE = 64
MAX_RIDE_REFINE_CARDS = 64
MAX_RIDE_REFINE_SKILLS_PER_CARD = 64

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


def read_maps(adb_path: str, serial: str, pid: int, timeout: float) -> list[tuple[int, int]]:
    command = f"su -c 'cat /proc/{pid}/maps'"
    result = run_adb(adb_path, serial, ["shell", command], timeout)
    if result.returncode != 0:
        return []

    ranges: list[tuple[int, int]] = []
    for raw_line in result.stdout.decode("utf-8", "replace").splitlines():
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


def read_chunks(
    adb_path: str,
    serial: str,
    pid: int,
    chunks: list[tuple[int, int]],
    timeout: float,
    batch_size: int = 32,
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


class BoundedPageMemoryReader:
    """Small cached reader for validated, page-aligned process-memory spans."""

    def __init__(
        self,
        adb_path: str,
        serial: str,
        pid: int,
        ranges: list[tuple[int, int]],
        timeout: float,
    ) -> None:
        self.adb_path = adb_path
        self.serial = serial
        self.pid = pid
        self.ranges = ranges
        self.timeout = timeout
        self.pages: dict[int, bytes] = {}

    def read(self, address: int, size: int) -> bytes:
        if size <= 0 or not address_in_ranges(address, size, self.ranges):
            return b""

        first = address & ~(PAGE_SIZE - 1)
        last = (address + size - 1) & ~(PAGE_SIZE - 1)
        page_addresses = list(range(first, last + PAGE_SIZE, PAGE_SIZE))
        missing = [page for page in page_addresses if page not in self.pages]
        if missing:
            blobs = read_chunks(
                self.adb_path,
                self.serial,
                self.pid,
                [(page, PAGE_SIZE) for page in missing],
                self.timeout,
            )
            self.pages.update({page: blob for page, blob in zip(missing, blobs)})

        joined = b"".join(self.pages.get(page, b"") for page in page_addresses)
        expected = len(page_addresses) * PAGE_SIZE
        if len(joined) != expected:
            return b""
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

    reader = BoundedPageMemoryReader(adb_path, serial, pid, ranges, timeout)
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

    reader = BoundedPageMemoryReader(adb_path, serial, pid, ranges, timeout)
    seen_ride_ins: set[int] = set()
    selected_cards: list[dict] | None = None
    for reference in ride_ins_references:
        ride_ins_address = tagged_pointer_from_value(reference.get("nodeValue"), LJ_TTAB)
        if ride_ins_address is None or ride_ins_address in seen_ride_ins:
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

        candidate_container: tuple[int, dict, list[dict]] | None = None
        for outer_index, value in enumerate(reset_values, start=1):
            container_address = tagged_pointer_from_value(value, LJ_TTAB)
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
            candidate_container = (outer_index, container, container_values)
            break

        if candidate_container is None:
            continue
        outer_index, _, container_values = candidate_container
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
) -> dict[str, list[int]]:
    key_objects: dict[str, set[int]] = {name: set() for name in DISCOVERED_FIELD_NAMES}
    names = [(name, name.encode("utf-8")) for name in DISCOVERED_FIELD_NAMES]
    chunks = chunk_ranges(ranges, chunk_size)
    for (chunk_start, chunk_size_value), blob in zip(
        chunks,
        read_chunks(adb_path, serial, pid, chunks, timeout),
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
        read_chunks(adb_path, serial, pid, chunks, timeout),
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
        if values["m_Gx"].get("kind") != "i32" or values["m_Gy"].get("kind") != "i32":
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
            "gx": int(values["m_Gx"]["value"]),
            "gy": int(values["m_Gy"]["value"]),
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


def process_identity(adb_path: str, serial: str, pid: int, timeout: float) -> dict | None:
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

    exe_command = f"su -c 'readlink /proc/{pid}/exe'"
    exe_result = run_adb(adb_path, serial, ["shell", exe_command], timeout)
    executable = exe_result.stdout.decode("utf-8", "replace").strip() or "/system/bin/app_process64"
    return {"pid": pid, "startTicks": start_ticks, "exe": executable}


def snapshot_result(
    pid: int,
    identity: dict | None,
    status: str,
    diagnostic_code: str,
    candidates: list[dict],
    ranges_count: int,
    chunks_read: int,
) -> dict:
    result = {
        "event": "mount_status_snapshot",
        "schema": "mount_status_snapshot.v1",
        "schemaVersion": 1,
        "readOnly": True,
        "actionAuthorized": False,
        "sessionId": str(uuid.uuid4()),
        "sequence": 1,
        "readAtUtc": datetime.now(timezone.utc).isoformat(),
        "status": status,
        "diagnosticCode": diagnostic_code,
        "process": identity or {"pid": pid, "startTicks": 0, "exe": ""},
        "candidateCount": len(candidates),
        "reader": {
            "ranges": ranges_count,
            "chunksRead": chunks_read,
            "strategy": "luajit_gc64_table_nodes",
            "writesMemory": False,
            "sendsPackets": False,
        },
    }
    if status == "ok" and len(candidates) == 1:
        result["player"] = candidates[0]
    return result


def main() -> int:
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
    args = parser.parse_args()
    ride_skill_name_map = load_ride_skill_name_map()

    identity = process_identity(args.adb, args.serial, args.pid, args.read_timeout)
    if identity is None:
        print(json.dumps(snapshot_result(args.pid, None, "error", "process_identity_unreadable", [], 0, 0)))
        return 2

    ranges = read_maps(args.adb, args.serial, args.pid, args.read_timeout)
    ranges = [
        (start, end)
        for start, end in ranges
        if args.min_range <= end - start <= args.max_range
    ]
    if args.max_ranges > 0:
        ranges = ranges[: args.max_ranges]
    chunks = chunk_ranges(ranges, args.chunk_size)
    if not ranges or not chunks:
        print(json.dumps(snapshot_result(args.pid, identity, "error", "anonymous_ranges_missing", [], len(ranges), 0)))
        return 2

    key_objects = discover_key_objects(
        args.adb,
        args.serial,
        args.pid,
        ranges,
        args.chunk_size,
        args.read_timeout,
    )
    if any(name not in key_objects for name in FIELD_NAMES):
        print(json.dumps(snapshot_result(args.pid, identity, "needs_discover", "field_string_missing", [], len(ranges), 0)))
        return 3

    references = scan_references(
        args.adb,
        args.serial,
        args.pid,
        ranges,
        key_objects,
        args.chunk_size,
        args.read_timeout,
    )
    candidates = build_local_candidates(references, args.candidate_distance)
    if len(candidates) == 1:
        if all(name in key_objects for name in OPTIONAL_RIDE_SKILL_FIELD_NAMES):
            ride_instances = read_ride_skill_instances(
                args.adb,
                args.serial,
                args.pid,
                ranges,
                references,
                args.read_timeout,
                skill_name_map=ride_skill_name_map,
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
        )
        print(json.dumps(snapshot_result(args.pid, identity, "ok", "ok", candidates, len(ranges), len(chunks))))
        return 0
    diagnostic = "local_player_missing" if not candidates else "ambiguous_local_player"
    print(json.dumps(snapshot_result(args.pid, identity, "needs_discover", diagnostic, candidates, len(ranges), len(chunks))))
    return 3


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
