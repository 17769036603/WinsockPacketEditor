"""Inspect and perform a guarded one-shot LuaJIT partner-panel value test.

The locally analyzed UI bytecode reads HpMax, Atk, and Speed from one
GetPartnerDynamicInfo result table. This tool requires all three live values
to match the supplied panel snapshot before considering a table. Writes are
opt-in and journaled.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import math
import os
import struct
import tempfile
from pathlib import Path

PROBE_PATH = Path(__file__).with_name("mount_status_luajit_probe.py")
TRANSPORT_PATH = Path(__file__).with_name("mount_speed_memory_test.py")
PACKAGE = "com.gdoo.yzqcxy"
SUPPORTED_PACKAGES = (PACKAGE, "com.gdoo.dhxy")
JOURNAL_SCHEMA = "partner_panel_memory_journal.v1"
FIELDS = ("HpMax", "Atk", "Speed")
CHUNK_SIZE = 2 * 1024 * 1024
SCAN_BATCH_SIZE = 16
MAX_SCAN_BYTES = 4 * 1024 * 1024 * 1024
MAX_REFERENCES = 100_000
MAX_REFERENCES_PER_CHUNK = 4096
MAX_TABLE_HEADERS = 100_000
MAX_RELEVANT_TABLE_HEADERS = 4096
MAX_DIAGNOSTIC_SAMPLES = 8
MAX_DIAGNOSTIC_VALUE_COUNTS = 16


def _load(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError("cannot load " + path.name)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def parse_number(raw: str):
    text = raw.strip()
    try:
        if text and text.lstrip("+-").isdigit():
            return int(text, 10)
        return float(text)
    except ValueError as error:
        raise argparse.ArgumentTypeError("expected a number") from error


def _positive_finite(value: object) -> bool:
    return (
        isinstance(value, (int, float))
        and not isinstance(value, bool)
        and math.isfinite(float(value))
        and value > 0
    )


def _validate_values(values: dict[str, object]) -> None:
    if set(values) != set(FIELDS) or any(
        not _positive_finite(values[name]) for name in FIELDS
    ):
        raise ValueError("HpMax, Atk, and Speed must be positive finite numbers")


def _is_target_package(process_name: str, package: str) -> bool:
    return process_name == package or process_name.startswith(package + ":")


def _open_session(probe, args):
    broker = (
        probe.RootBrokerClient(args.root_broker_pipe)
        if args.root_broker_pipe
        else None
    )
    session = probe.AdbPageMemoryReadSession(
        args.adb,
        args.serial,
        args.pid,
        root_broker=broker,
    )
    return session


def _read_identity(probe, args) -> tuple[dict, str]:
    session = _open_session(probe, args)
    try:
        identity = probe.process_identity(
            args.adb,
            args.serial,
            args.pid,
            8.0,
            session=session,
        )
        raw_name = session.read_text(f"cat /proc/{args.pid}/cmdline", 8.0)
        process_name = (raw_name or b"").split(b"\0", 1)[0].decode(
            "utf-8", "replace"
        )
        if (
            not identity
            or identity.get("pid") != args.pid
            or not isinstance(identity.get("startTicks"), int)
            or identity["startTicks"] <= 0
            or not isinstance(identity.get("exe"), str)
            or not identity["exe"]
        ):
            raise RuntimeError("process identity could not be established")
        if not _is_target_package(process_name, args.package):
            raise RuntimeError(f"PID {args.pid} is not bound to {args.package}")
        return identity, process_name
    finally:
        session.close()


def _iter_scan_chunks(probe, ranges):
    """Yield page-aligned chunks with a one-page overlap for GC string edges."""
    overlap = probe.PAGE_SIZE
    for range_start, range_end in ranges:
        position = range_start
        while position < range_end:
            size = min(CHUNK_SIZE, range_end - position)
            yield position, size
            if position + size >= range_end:
                break
            position += size - overlap


def _read_scan_chunks(probe, args, session, ranges):
    chunks = list(_iter_scan_chunks(probe, ranges))
    total = sum(size for _, size in chunks)
    if total > MAX_SCAN_BYTES:
        raise RuntimeError(
            f"private writable memory scan is {total // (1024 * 1024)} MiB; "
            f"limit is {MAX_SCAN_BYTES // (1024 * 1024)} MiB"
        )
    for offset in range(0, len(chunks), SCAN_BATCH_SIZE):
        batch = chunks[offset : offset + SCAN_BATCH_SIZE]
        blobs = []
        for attempt in range(2):
            blobs = probe.read_chunks(
                args.adb,
                args.serial,
                args.pid,
                batch,
                30.0,
                batch_size=SCAN_BATCH_SIZE,
                session=session,
            )
            if len(blobs) == len(batch) and all(
                len(blob) == size for blob, (_, size) in zip(blobs, batch)
            ):
                break
            if attempt == 1:
                raise RuntimeError("process memory read failed; scan was discarded")
        for descriptor, blob in zip(batch, blobs):
            yield descriptor[0], descriptor[1], blob


def _discover_key_objects(probe, args, session, ranges) -> dict[str, list[int]]:
    objects: dict[str, set[int]] = {name: set() for name in FIELDS}
    names = [(name, name.encode("ascii")) for name in FIELDS]
    for chunk_start, _chunk_size, blob in _read_scan_chunks(
        probe, args, session, ranges
    ):
        for name, needle in names:
            positions = probe.find_all(blob, needle, 1025)
            if len(positions) > 1024:
                raise RuntimeError(f"too many {name} strings in one memory chunk")
            for offset in positions:
                if probe.verify_gcstr(blob, offset, needle):
                    objects[name].add(chunk_start + offset - probe.GCSTR_SIZE)
    if any(not objects[name] for name in FIELDS):
        missing = ", ".join(name for name in FIELDS if not objects[name])
        raise RuntimeError("LuaJIT field strings were not found: " + missing)
    return {name: sorted(addresses) for name, addresses in objects.items()}


def _discover_key_references(
    probe, args, session, ranges, key_objects, expected: dict, diagnostics=None,
    table_headers: dict[int, tuple[int, int]] | None = None,
    header_overflow: list[bool] | None = None,
) -> dict[str, list[dict]]:
    references: dict[str, list[dict]] = {name: [] for name in FIELDS}
    seen: set[tuple[str, int]] = set()
    raw_seen: set[tuple[str, int]] = set()
    if diagnostics is not None:
        diagnostics.update({
            name: {
                "gcStringObjects": len(key_objects[name]),
                "rawReferenceCount": 0,
                "predecessorKindCounts": {},
                "predecessorValueCounts": {},
                "expectedReferenceCount": 0,
                "expectedReferenceSamples": [],
                "samples": [],
            }
            for name in FIELDS
        })
    needles = [
        (
            name,
            address,
            struct.pack("<Q", probe.gc64_tagged_pointer(address, probe.LJ_TSTR)),
        )
        for name, addresses in key_objects.items()
        for address in addresses
    ]
    for chunk_start, _chunk_size, blob in _read_scan_chunks(
        probe, args, session, ranges
    ):
        if table_headers is not None:
            first = (-chunk_start) & 7
            for offset in range(first, len(blob) - probe.GCTAB_SIZE + 1, 8):
                if blob[offset + 9] != probe.GCT_TAB:
                    continue
                node_address = struct.unpack_from("<Q", blob, offset + 40)[0]
                mask = struct.unpack_from("<I", blob, offset + 52)[0]
                if (
                    node_address
                    and mask <= probe.MAX_HASH_MASK
                    and probe.address_in_ranges(
                        node_address, (mask + 1) * probe.NODE_STRIDE, ranges
                    )
                ):
                    table_headers[chunk_start + offset] = (node_address, mask)
                    if len(table_headers) > MAX_TABLE_HEADERS:
                        table_headers.pop(chunk_start + offset)
                        if header_overflow is not None:
                            header_overflow[0] = True
                        else:
                            raise RuntimeError("LuaJIT table header count exceeds scan limit")
        for name, object_address, needle in needles:
            positions = probe.find_all(blob, needle, MAX_REFERENCES_PER_CHUNK + 1)
            if len(positions) > MAX_REFERENCES_PER_CHUNK:
                raise RuntimeError(f"too many {name} references in one memory chunk")
            for offset in positions:
                absolute = chunk_start + offset
                if (
                    offset < 8
                    or absolute == object_address
                    or absolute % 8 != 0
                    or (name, absolute) in raw_seen
                ):
                    continue
                raw_seen.add((name, absolute))
                if len(raw_seen) > MAX_REFERENCES:
                    raise RuntimeError("LuaJIT key reference count exceeds scan limit")
                value = probe.decode_tvalue(blob[offset - 8 : offset])
                expected_match = (
                    value.get("kind") in {"i32", "f64"}
                    and value.get("value") == expected[name]
                )
                if diagnostics is not None:
                    item = diagnostics[name]
                    item["rawReferenceCount"] += 1
                    kind = str(value.get("kind", "unknown"))[:32]
                    counts = item["predecessorKindCounts"]
                    counts[kind] = counts.get(kind, 0) + 1
                    count_value = value.get("value")
                    value_key = f"{kind}:{str(count_value)[:64]}"
                    value_counts = item["predecessorValueCounts"]
                    if value_key in value_counts:
                        value_counts[value_key] += 1
                    elif len(value_counts) < MAX_DIAGNOSTIC_VALUE_COUNTS:
                        value_counts[value_key] = 1
                    else:
                        value_counts["other"] = value_counts.get("other", 0) + 1
                    if len(item["samples"]) < MAX_DIAGNOSTIC_SAMPLES:
                        sample_value = count_value
                        if isinstance(sample_value, str):
                            sample_value = sample_value[:64]
                        elif not isinstance(sample_value, (int, float, bool)) and sample_value is not None:
                            sample_value = str(sample_value)[:64]
                        item["samples"].append({
                            "object": hex(object_address),
                            "reference": hex(absolute),
                            "kind": kind,
                            "value": sample_value,
                            "expectedMatch": expected_match,
                        })
                if not expected_match:
                    continue
                if (
                    (name, absolute) in seen
                ):
                    continue
                seen.add((name, absolute))
                if diagnostics is not None:
                    item = diagnostics[name]
                    item["expectedReferenceCount"] += 1
                    if len(item["expectedReferenceSamples"]) < MAX_DIAGNOSTIC_SAMPLES:
                        item["expectedReferenceSamples"].append({
                            "reference": hex(absolute),
                            "nodeAddress": hex(absolute - 8),
                            "value": value.get("value"),
                        })
                references[name].append(
                    {
                        "reference": absolute,
                        "nodeAddress": absolute - 8,
                        "value": value,
                    }
                )
                if len(seen) > MAX_REFERENCES:
                    raise RuntimeError("LuaJIT key reference count exceeds scan limit")
    return references


def _rescan_relevant_table_headers(probe, args, session, reader, reference_nodes):
    headers: dict[int, tuple[int, int]] = {}
    for chunk_start, _chunk_size, blob in _read_scan_chunks(
        probe, args, session, reader.ranges
    ):
        first = (-chunk_start) & 7
        for offset in range(first, len(blob) - probe.GCTAB_SIZE + 1, 8):
            if blob[offset + 9] != probe.GCT_TAB:
                continue
            node_address = struct.unpack_from("<Q", blob, offset + 40)[0]
            mask = struct.unpack_from("<I", blob, offset + 52)[0]
            if (
                not node_address
                or mask > probe.MAX_HASH_MASK
                or not probe.address_in_ranges(
                    node_address, (mask + 1) * probe.NODE_STRIDE, reader.ranges
                )
            ):
                continue
            node_end = node_address + (mask + 1) * probe.NODE_STRIDE
            if not any(
                node_address <= node < node_end
                and (node - node_address) % probe.NODE_STRIDE == 0
                for nodes in reference_nodes.values() for node in nodes
            ):
                continue
            headers[chunk_start + offset] = (node_address, mask)
            if len(headers) > MAX_RELEVANT_TABLE_HEADERS:
                raise RuntimeError("relevant LuaJIT table header count exceeds scan limit")
    return headers


def _entry_raw(entry: dict) -> bytes:
    value = entry.get("value")
    raw = value.get("raw") if isinstance(value, dict) else None
    if not isinstance(raw, str):
        raise RuntimeError("LuaJIT table entry has no raw TValue")
    try:
        value_u64 = int(raw, 16)
    except ValueError as error:
        raise RuntimeError("LuaJIT table entry has invalid raw TValue") from error
    if not 0 <= value_u64 < (1 << 64):
        raise RuntimeError("LuaJIT table entry has invalid TValue length")
    return struct.pack("<Q", value_u64)


def _scan_tables(probe, args, session, reader, expected: dict, diagnostics=None) -> list[dict]:
    key_objects = _discover_key_objects(probe, args, session, reader.ranges)
    table_headers: dict[int, tuple[int, int]] = {}
    header_overflow = [False]
    references = _discover_key_references(
        probe, args, session, reader.ranges, key_objects, expected, diagnostics,
        table_headers, header_overflow,
    )
    if diagnostics is not None:
        for name in FIELDS:
            diagnostics[name]["localParentHitCount"] = 0
            diagnostics[name]["localTableSamples"] = []
    hits: dict[int, dict] = {}
    checked_nodes: set[int] = set()
    checked_tables: set[int] = set()
    table_summaries: dict[int, dict] = {}
    for name in FIELDS:
        for reference in references[name]:
            node_address = reference["nodeAddress"]
            if node_address in checked_nodes:
                continue
            checked_nodes.add(node_address)
            found = probe.find_luajit_table_for_node(reader, node_address)
            if found is None:
                continue
            table_address, table = found
            if diagnostics is not None:
                item = diagnostics[name]
                item["localParentHitCount"] += 1
                if len(item["localTableSamples"]) < MAX_DIAGNOSTIC_SAMPLES:
                    sample = {
                        "tableAddress": hex(table_address),
                        "nodeAddress": hex(node_address),
                    }
                    if sample not in item["localTableSamples"]:
                        item["localTableSamples"].append(sample)
            if table_address in checked_tables:
                if diagnostics is not None and table_address in table_summaries:
                    item = diagnostics[name]
                    sample = next(
                        (
                            entry for entry in item["localTableSamples"]
                            if entry["tableAddress"] == hex(table_address)
                        ),
                        None,
                    )
                    if sample is not None:
                        sample.update(table_summaries[table_address])
                continue
            checked_tables.add(table_address)
            entries = probe.read_gc_table_entries(reader, table)
            reader.pages.clear()
            if entries is None:
                continue
            by_key: dict[str, dict] = {}
            for entry in entries:
                key = entry.get("key")
                if key in FIELDS and key not in by_key:
                    by_key[key] = entry
            summary = {
                "fields": sorted(by_key),
                "values": {
                    field: by_key[field].get("value", {}).get("value")
                    for field in FIELDS
                    if isinstance(by_key.get(field, {}).get("value"), dict)
                    and by_key[field]["value"].get("kind") in {"i32", "f64"}
                },
            }
            if diagnostics is not None:
                table_summaries[table_address] = summary
                item = diagnostics[name]
                sample = next(
                    (
                        entry for entry in item["localTableSamples"]
                        if entry["tableAddress"] == hex(table_address)
                    ),
                    None,
                )
                if sample is not None:
                    sample.update(summary)
            if set(by_key) != set(FIELDS):
                continue
            values = {}
            for field in FIELDS:
                value = by_key[field].get("value")
                if not isinstance(value, dict) or value.get("kind") not in {"i32", "f64"}:
                    break
                values[field] = value.get("value")
            if set(values) != set(FIELDS) or any(
                values[field] != expected[field]
                for field in FIELDS
            ):
                continue
            hits[table_address] = {
                "table": table,
                "entries": by_key,
                "values": values,
            }
            # Two matches are enough to refuse writes; do not scan more than needed.
            if len(hits) >= 2:
                break
        if len(hits) >= 2:
            break
    if len(hits) < 2:
        reference_nodes = {
            name: {item["nodeAddress"] for item in references[name]}
            for name in FIELDS
        }
        if header_overflow[0]:
            table_headers = _rescan_relevant_table_headers(
                probe, args, session, reader, reference_nodes
            )
        for table_address, (header_node, header_mask) in sorted(table_headers.items()):
            if table_address in checked_tables:
                continue
            node_start = header_node
            node_end = node_start + (header_mask + 1) * probe.NODE_STRIDE
            if not any(
                node_start <= node < node_end and (node - node_start) % probe.NODE_STRIDE == 0
                for nodes in reference_nodes.values() for node in nodes
            ):
                continue
            table = probe.read_gc_table(reader, table_address)
            if (
                table is None
                or int(table["node"]) != header_node
                or int(table["hashMask"]) != header_mask
            ):
                continue
            checked_tables.add(table_address)
            entries = probe.read_gc_table_entries(reader, table)
            reader.pages.clear()
            if entries is None:
                continue
            by_key = {}
            for entry in entries:
                key = entry.get("key")
                if key in FIELDS and key not in by_key:
                    by_key[key] = entry
            if set(by_key) != set(FIELDS):
                continue
            values = {}
            for field in FIELDS:
                value = by_key[field].get("value")
                if (
                    not isinstance(value, dict)
                    or value.get("kind") not in {"i32", "f64"}
                    or int(by_key[field].get("nodeAddress", 0)) not in reference_nodes[field]
                ):
                    break
                values[field] = value.get("value")
            if set(values) == set(FIELDS) and all(
                values[field] == expected[field] for field in FIELDS
            ):
                hits[table_address] = {"table": table, "entries": by_key, "values": values}
                if len(hits) >= 2:
                    break
    return [hits[address] for address in sorted(hits)]


def _locate(args, expected: dict, probe=None, diagnostics=None) -> tuple[dict, str, list[dict]]:
    probe = probe or _load(PROBE_PATH, "partner_memory_probe")
    if args.pid <= 0:
        raise ValueError("PID must be positive")
    if args.package not in SUPPORTED_PACKAGES:
        raise ValueError("package is not in the supported game package list")
    _validate_values(expected)
    session = _open_session(probe, args)
    reader = None
    try:
        identity = probe.process_identity(
            args.adb, args.serial, args.pid, 8.0, session=session
        )
        raw_name = session.read_text(f"cat /proc/{args.pid}/cmdline", 8.0)
        process_name = (raw_name or b"").split(b"\0", 1)[0].decode(
            "utf-8", "replace"
        )
        if (
            not identity
            or identity.get("pid") != args.pid
            or not isinstance(identity.get("startTicks"), int)
            or identity["startTicks"] <= 0
            or not identity.get("exe")
        ):
            raise RuntimeError("process identity could not be established")
        if not _is_target_package(process_name, args.package):
            raise RuntimeError(f"PID {args.pid} is not bound to {args.package}")
        maps = session.read_text(f"cat /proc/{args.pid}/maps", 8.0)
        ranges = probe.parse_maps(maps or b"")
        if not ranges:
            raise RuntimeError("no readable private writable LuaJIT mappings were found")
        scanned_bytes = sum(end - start for start, end in ranges)
        if scanned_bytes > MAX_SCAN_BYTES:
            raise RuntimeError(
                f"private writable mappings total {scanned_bytes // (1024 * 1024)} MiB; "
                f"limit is {MAX_SCAN_BYTES // (1024 * 1024)} MiB"
            )
        reader = probe.BoundedPageMemoryReader(
            args.adb, args.serial, args.pid, ranges, 30.0, shared_session=session
        )
        hits = _scan_tables(probe, args, session, reader, expected, diagnostics)
        for hit in hits:
            hit["table"]["address"] = int(hit["table"]["address"])
            hit["scannedBytes"] = scanned_bytes
        return identity, process_name, hits
    finally:
        if reader is not None:
            reader.close()
        session.close()


def _identity_now(probe, args) -> dict:
    identity, _ = _read_identity(probe, args)
    return identity


def _make_transport(args, probe):
    module = _load(TRANSPORT_PATH, "partner_memory_transport")
    broker = (
        probe.RootBrokerClient(args.root_broker_pipe)
        if args.root_broker_pipe
        else None
    )
    return module.AdbTransport(
        args.adb,
        args.serial,
        args.pid,
        root_broker=broker,
        dd_command="toybox dd",
        binary_base64=True,
    ), broker


def _read_exact(transport, address: int) -> bytes:
    raw = transport.read(address, 8)
    if len(raw) != 8:
        raise RuntimeError("short LuaJIT TValue read")
    return raw


def _encode_numeric(value: object, old_raw: bytes, probe) -> bytes:
    if not _positive_finite(value):
        raise ValueError("new value must be positive and finite")
    kind = probe.decode_tvalue(old_raw).get("kind")
    if kind == "i32":
        if not isinstance(value, int) or isinstance(value, bool) or not 0 < value < (1 << 31):
            raise ValueError("i32 fields require an integer in [1, 2^31)")
        tag = (probe.LJ_TISNUM & 0x1FFFF) << 47
        return struct.pack("<Q", tag | (value & 0xFFFFFFFF))
    if kind == "f64":
        encoded = struct.pack("<d", float(value))
        decoded = probe.decode_tvalue(encoded)
        if decoded.get("kind") != "f64" or not _positive_finite(decoded.get("value")):
            raise ValueError("value cannot be represented as a positive finite LuaJIT f64")
        return encoded
    raise ValueError("partner field is not an i32 or f64 TValue")


def _snapshot(hit, probe) -> dict[str, bytes]:
    result = {field: _entry_raw(hit["entries"][field]) for field in FIELDS}
    for field, raw in result.items():
        value = probe.decode_tvalue(raw)
        if value.get("kind") not in {"i32", "f64"} or not _positive_finite(value.get("value")):
            raise RuntimeError("partner table contains an invalid numeric TValue")
    return result


def _atomic_journal(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, temporary = tempfile.mkstemp(dir=str(path.parent), prefix=path.name + ".")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump(payload, stream, separators=(",", ":"), sort_keys=True)
            stream.flush()
            os.fsync(stream.fileno())
        try:
            os.link(temporary, str(path))
        except FileExistsError as error:
            raise RuntimeError("journal already exists; restore it or choose another path") from error
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def _expected_snapshot(snapshot: dict[str, bytes], probe) -> dict[str, object]:
    values = {field: probe.decode_tvalue(snapshot[field]).get("value") for field in FIELDS}
    _validate_values(values)
    return values


def _direct_rebind(args, identity, table, node_addresses, expected_snapshot, probe):
    """Re-read one known table without repeating the whole-process discovery scan."""
    session = _open_session(probe, args)
    reader = None
    try:
        current_identity = probe.process_identity(
            args.adb, args.serial, args.pid, 8.0, session=session
        )
        raw_name = session.read_text(f"cat /proc/{args.pid}/cmdline", 8.0)
        process_name = (raw_name or b"").split(b"\0", 1)[0].decode("utf-8", "replace")
        if current_identity != identity or not _is_target_package(process_name, args.package):
            raise RuntimeError("process identity or package changed during table revalidation")
        ranges = probe.parse_maps(session.read_text(f"cat /proc/{args.pid}/maps", 8.0) or b"")
        scanned_bytes = sum(end - start for start, end in ranges)
        if not ranges or scanned_bytes > MAX_SCAN_BYTES:
            raise RuntimeError("bounded LuaJIT mappings are unavailable for table revalidation")
        reader = probe.BoundedPageMemoryReader(
            args.adb, args.serial, args.pid, ranges, 30.0, shared_session=session
        )
        table_address = int(table["address"])
        current_table = probe.read_gc_table(reader, table_address)
        if (
            current_table is None
            or current_table["node"] != int(table["node"])
            or current_table["hashMask"] != int(table["hashMask"])
        ):
            raise RuntimeError("LuaJIT table header moved during direct revalidation")
        entries = probe.read_gc_table_entries(reader, current_table)
        reader.pages.clear()
        if entries is None:
            raise RuntimeError("LuaJIT table entries could not be read completely")
        by_key = {}
        for entry in entries:
            key = entry.get("key")
            if key in FIELDS and key not in by_key:
                by_key[key] = entry
        if set(by_key) != set(FIELDS):
            raise RuntimeError("LuaJIT table fields changed during direct revalidation")
        hit = {"table": current_table, "entries": by_key}
        if _snapshot(hit, probe) != expected_snapshot:
            raise RuntimeError("LuaJIT table snapshot changed during direct revalidation")
        if any(
            int(by_key[field].get("nodeAddress", 0)) != int(node_addresses[field])
            for field in FIELDS
        ):
            raise RuntimeError("LuaJIT table nodes moved during direct revalidation")
        hit["values"] = {
            field: probe.decode_tvalue(expected_snapshot[field]).get("value")
            for field in FIELDS
        }
        return current_identity, hit
    finally:
        if reader is not None:
            reader.close()
        session.close()


def run(args, probe=None, transport=None) -> dict:
    probe = probe or _load(PROBE_PATH, "partner_memory_probe")
    expected = {"HpMax": args.hp_max, "Atk": args.atk, "Speed": args.speed}
    scan_diagnostics = {} if args.action == "scan" else None
    identity, process_name, hits = _locate(args, expected, probe, scan_diagnostics)
    if args.action == "scan":
        return {
            "action": args.action,
            "identity": identity,
            "package": process_name,
            "matches": [
                {"table": hex(hit["table"]["address"]), "values": hit["values"]}
                for hit in hits
            ],
            "diagnostics": scan_diagnostics,
            "unique": len(hits) == 1,
            "knownWriteLimit": "LuaJIT GC/rehash can relocate a table after revalidation",
        }
    if len(hits) != 1:
        raise RuntimeError("set requires exactly one table matching all three panel values")
    field = args.field
    hit = hits[0]
    snapshot = _snapshot(hit, probe)
    address = int(hit["entries"][field]["nodeAddress"])
    old_raw = snapshot[field]
    if probe.decode_tvalue(old_raw).get("value") != expected[field]:
        raise RuntimeError("selected field does not match the displayed panel value")
    new_raw = _encode_numeric(args.value, old_raw, probe)
    if new_raw == old_raw:
        raise ValueError("new value already equals the current value")
    binding = {
        "package": args.package,
        "pid": args.pid,
        "startTicks": identity["startTicks"],
        "exe": identity["exe"],
        "table": hit["table"]["address"],
        "tableNode": hit["table"]["node"],
        "tableHashMask": hit["table"]["hashMask"],
        "nodeAddresses": {
            name: int(hit["entries"][name]["nodeAddress"]) for name in FIELDS
        },
        "field": field,
        "address": address,
        "snapshotRaw": {name: raw.hex() for name, raw in snapshot.items()},
        "oldRaw": old_raw.hex(),
        "newRaw": new_raw.hex(),
    }
    if not args.write:
        return {
            "action": "set",
            "written": False,
            "dryRun": True,
            "binding": binding,
            "knownWriteLimit": "LuaJIT GC/rehash can relocate a table after revalidation",
        }

    # ponytail: direct table rebind plus expected-old compare narrows the GC race,
    # but /proc/PID/mem offers no client-thread synchronization; a game-side hook is the upgrade.
    _direct_rebind(
        args, identity, hit["table"], binding["nodeAddresses"], snapshot, probe
    )
    active_transport, broker = (
        (transport, None) if transport is not None else _make_transport(args, probe)
    )
    try:
        if _identity_now(probe, args) != identity:
            raise RuntimeError("process identity changed immediately before write")
        if _read_exact(active_transport, address) != old_raw:
            raise RuntimeError("selected TValue changed immediately before write")
        _atomic_journal(Path(args.journal), {"schema": JOURNAL_SCHEMA, "binding": binding})
        active_transport.write(
            address,
            new_raw,
            identity["startTicks"],
            expected_raw=old_raw,
        )
        if _read_exact(active_transport, address) != new_raw:
            raise RuntimeError("set write verification failed; journal retained")
    finally:
        if broker is not None:
            broker.close()
    return {
        "action": "set",
        "written": True,
        "field": field,
        "oldValue": probe.decode_tvalue(old_raw).get("value"),
        "newValue": probe.decode_tvalue(new_raw).get("value"),
        "table": hex(hit["table"]["address"]),
        "journal": str(Path(args.journal).resolve()),
        "knownWriteLimit": "LuaJIT GC/rehash can relocate a table after revalidation",
    }


def _read_journal(path: Path) -> dict:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise RuntimeError("partner restore journal could not be read") from error
    if not isinstance(value, dict) or value.get("schema") != JOURNAL_SCHEMA:
        raise RuntimeError("invalid partner restore journal")
    binding = value.get("binding")
    if not isinstance(binding, dict):
        raise RuntimeError("invalid partner restore journal")
    return binding


def restore(args, probe=None, transport=None) -> dict:
    probe = probe or _load(PROBE_PATH, "partner_memory_probe")
    if args.pid <= 0 or args.package not in SUPPORTED_PACKAGES:
        raise ValueError("restore requires the journaled game PID and package")
    binding = _read_journal(Path(args.journal))
    if (
        args.pid != binding.get("pid")
        or args.package != binding.get("package")
        or binding.get("field") not in FIELDS
    ):
        raise RuntimeError("restore process/field does not match journal")
    identity = _identity_now(probe, args)
    if identity != {
        key: binding.get(key) for key in ("pid", "startTicks", "exe")
    }:
        raise RuntimeError("restore process identity changed")
    try:
        snapshot = {
            field: bytes.fromhex(binding["snapshotRaw"][field]) for field in FIELDS
        }
        old_raw = bytes.fromhex(binding["oldRaw"])
        new_raw = bytes.fromhex(binding["newRaw"])
        table_address = int(binding["table"])
        address = int(binding["address"])
        table = {
            "address": table_address,
            "node": int(binding["tableNode"]),
            "hashMask": int(binding["tableHashMask"]),
        }
        node_addresses = {
            field: int(binding["nodeAddresses"][field]) for field in FIELDS
        }
    except (KeyError, TypeError, ValueError) as error:
        raise RuntimeError("restore journal values are invalid") from error
    if (
        any(len(raw) != 8 for raw in snapshot.values())
        or len(old_raw) != 8
        or len(new_raw) != 8
        or old_raw != snapshot[binding["field"]]
        or table_address <= 0
        or address <= 0
        or any(node <= 0 for node in node_addresses.values())
        or node_addresses[binding["field"]] != address
    ):
        raise RuntimeError("restore journal values are invalid")
    expected = _expected_snapshot(snapshot, probe)
    old_value = probe.decode_tvalue(old_raw)
    changed = probe.decode_tvalue(new_raw)
    if (
        old_value.get("kind") not in {"i32", "f64"}
        or changed.get("kind") != old_value.get("kind")
        or changed.get("raw") == old_value.get("raw")
        or not _positive_finite(changed.get("value"))
    ):
        raise RuntimeError("restore journal new TValue is invalid")
    expected[binding["field"]] = changed["value"]
    _validate_values(expected)
    expected_current = {
        field: new_raw if field == binding["field"] else snapshot[field]
        for field in FIELDS
    }
    # The journal binds this exact table and all three nodes; direct rebinding
    # below proves the complete current snapshot before any restore write.
    _direct_rebind(args, identity, table, node_addresses, expected_current, probe)
    if not args.write:
        return {
            "action": "restore",
            "written": False,
            "dryRun": True,
            "field": binding["field"],
            "oldValue": probe.decode_tvalue(old_raw).get("value"),
            "knownWriteLimit": "LuaJIT GC/rehash can relocate a table after revalidation",
        }

    # Rebind again just before restoring; only our exact new TValue is eligible.
    _direct_rebind(args, identity, table, node_addresses, expected_current, probe)
    active_transport, broker = (
        (transport, None) if transport is not None else _make_transport(args, probe)
    )
    try:
        if _identity_now(probe, args) != identity:
            raise RuntimeError("process identity changed at restore boundary")
        if _read_exact(active_transport, address) != new_raw:
            raise RuntimeError("current field is not exactly the journaled new TValue")
        active_transport.write(
            address,
            old_raw,
            identity["startTicks"],
            expected_raw=new_raw,
        )
        if _read_exact(active_transport, address) != old_raw:
            raise RuntimeError("restore write verification failed")
    finally:
        if broker is not None:
            broker.close()
    return {
        "action": "restore",
        "written": True,
        "field": binding["field"],
        "oldValue": probe.decode_tvalue(old_raw).get("value"),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("scan", "set", "restore"))
    parser.add_argument("--adb", required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--pid", required=True, type=int)
    parser.add_argument("--package", default=PACKAGE, choices=SUPPORTED_PACKAGES)
    parser.add_argument("--root-broker-pipe", default="")
    parser.add_argument("--hp-max", type=parse_number)
    parser.add_argument("--atk", type=parse_number)
    parser.add_argument("--speed", type=parse_number)
    parser.add_argument("--field", choices=FIELDS, default="HpMax")
    parser.add_argument("--value", type=parse_number)
    parser.add_argument("--journal", default="partner_panel_memory_test.json")
    parser.add_argument(
        "--write",
        action="store_true",
        help="perform one guarded best-effort write/restore; default is dry-run",
    )
    args = parser.parse_args()
    try:
        if args.action == "restore":
            result = restore(args)
        else:
            if any(value is None for value in (args.hp_max, args.atk, args.speed)):
                parser.error("scan/set requires --hp-max, --atk, and --speed")
            if args.action == "set" and args.value is None:
                parser.error("set requires --value")
            if args.action == "scan" and args.write:
                parser.error("scan is read-only and does not accept --write")
            result = run(args)
        print(json.dumps(result, separators=(",", ":")))
        return 0
    except Exception as error:
        parser.exit(2, f"error: {error}\n")


if __name__ == "__main__":
    raise SystemExit(main())
