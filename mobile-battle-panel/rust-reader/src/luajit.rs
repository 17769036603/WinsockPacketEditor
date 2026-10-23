use crate::battle::{read_manager_values, read_role_position_map, read_slots};
use crate::memory::{MemoryReader, PAGE_SIZE};
use crate::model::{MemoryRange, ProbeError};
use std::collections::{HashMap, HashSet};

pub const GC64_MASK: u64 = (1u64 << 47) - 1;
const GCSTR_SIZE: usize = 24;
const GCSTR_GCT: u8 = 4;
const GCT_TAB: u8 = 11;
const LJ_TISNUM: u32 = 0xFFFFFFF2;
const LJ_TTRUE: u32 = 0xFFFFFFFD;
const LJ_TFALSE: u32 = 0xFFFFFFFE;
const LJ_TNIL: u32 = 0xFFFFFFFF;
pub const LJ_TTAB: u32 = 0xFFFFFFF4;
pub const LJ_TSTR: u32 = 0xFFFFFFFB;
pub const LJ_TUDATA: u32 = 0xFFFFFFF3;
pub const LJ_TFUNC: u32 = 0xFFFFFFF7;
const GCTAB_SIZE: usize = 64;
const NODE_STRIDE: usize = 24;
const GC64_TAG_MASK: u64 = (1 << 17) - 1;
const MAX_HASH_MASK: u32 = 0x1000;
const MAX_TABLE_ARRAY_SIZE: u32 = 4096;
const MAX_TABLE_HEADERS: usize = 250_000;
const MAX_KEY_OBJECTS: usize = 100_000;
const MAX_CANDIDATE_NODES: usize = 500_000;
const CHUNK_SIZE: u64 = 0x0100_0000;
const SCAN_KEYS: [&str; 6] = [
    "m_BattleId",
    "m_CurRoundNum",
    "m_PlayerId",
    "m_RoleIns",
    "m_SpareRoleIns",
    "m_RoleIdPosDict",
];
const SCAN_KEY_PREFIX: &[u8] = b"m_";

#[derive(Clone, Debug)]
pub enum Value {
    Integer(i32),
    Number(f64),
    Boolean(bool),
    Nil,
    Tagged { tag: u32, address: u64 },
}

#[derive(Clone, Debug, PartialEq)]
pub enum TableKey {
    String(String),
    Number(f64),
}

#[derive(Clone, Debug)]
pub struct TableNode {
    pub key: Option<TableKey>,
    pub value: Value,
}

#[derive(Clone, Copy, Debug)]
pub struct GcTable {
    pub address: u64,
    pub array_address: u64,
    pub node_address: u64,
    pub array_size: u32,
    pub hash_mask: u32,
}

#[derive(Clone, Copy, Debug)]
struct TableHeaderIndex {
    address: u64,
}

pub fn decode_tvalue(raw: &[u8]) -> Result<Value, ProbeError> {
    if raw.len() != 8 {
        return Err(ProbeError::read("TValue must contain exactly eight bytes"));
    }
    let mut word_bytes = [0u8; 8];
    word_bytes.copy_from_slice(raw);
    let word = u64::from_le_bytes(word_bytes);
    let tag = ((word as i64) >> 47) as u32;
    if tag == LJ_TISNUM {
        let mut number_bytes = [0u8; 4];
        number_bytes.copy_from_slice(&raw[0..4]);
        return Ok(Value::Integer(i32::from_le_bytes(number_bytes)));
    }
    if tag == LJ_TTRUE {
        return Ok(Value::Boolean(true));
    }
    if tag == LJ_TFALSE {
        return Ok(Value::Boolean(false));
    }
    if tag == LJ_TNIL {
        return Ok(Value::Nil);
    }
    if word >> 51 != 0x1fff {
        let number = f64::from_bits(word);
        if number.is_finite() {
            return Ok(Value::Number(number));
        }
    }
    Ok(Value::Tagged {
        tag,
        address: word & GC64_MASK,
    })
}

pub fn tagged_pointer_value(value: &Value, expected_tag: u32) -> Option<u64> {
    match value {
        Value::Tagged { tag, address } if *tag == expected_tag => Some(*address),
        _ => None,
    }
}

pub fn table_key_number(key: Option<&TableKey>) -> Option<i64> {
    let TableKey::Number(value) = key? else {
        return None;
    };
    if !value.is_finite()
        || value.fract() != 0.0
        || *value < i64::MIN as f64
        || *value >= 9_223_372_036_854_775_808.0
    {
        return None;
    }
    Some(*value as i64)
}

pub fn numeric_i64(value: &Value) -> Option<i64> {
    match value {
        Value::Integer(number) => Some(*number as i64),
        Value::Number(number)
            if number.is_finite()
                && number.fract() == 0.0
                && *number >= i64::MIN as f64
                && *number < 9_223_372_036_854_775_808.0 =>
        {
            Some(*number as i64)
        }
        _ => None,
    }
}

pub fn read_gcstr(reader: &mut MemoryReader, address: u64) -> Result<String, ProbeError> {
    if !reader.contains(address, GCSTR_SIZE) {
        return Err(ProbeError::read(
            "GC string header is outside validated maps",
        ));
    }
    let header = reader.read(address, GCSTR_SIZE)?;
    if header[9] != GCSTR_GCT {
        return Err(ProbeError::read("GC string has an unexpected object type"));
    }
    let length = u32::from_le_bytes(
        header[20..24]
            .try_into()
            .map_err(|_| ProbeError::read("GC string length field is truncated"))?,
    ) as usize;
    if length > 4096 {
        return Err(ProbeError::read("GC string exceeds the 4096-byte limit"));
    }
    if length == 0 {
        return Ok(String::new());
    }
    let data_address = address
        .checked_add(GCSTR_SIZE as u64)
        .ok_or_else(|| ProbeError::read("GC string address overflow"))?;
    if !reader.contains(data_address, length) {
        return Err(ProbeError::read("GC string data is outside validated maps"));
    }
    let bytes = reader.read(data_address, length)?;
    Ok(String::from_utf8_lossy(&bytes).into_owned())
}

pub fn read_table(reader: &mut MemoryReader, address: u64) -> Result<GcTable, ProbeError> {
    read_table_bounded(reader, address, MAX_HASH_MASK)
}

/// Attribute globals are larger than ordinary object tables; keep this larger
/// bound local to that validated lookup instead of widening cold scanning.
pub fn read_attribute_globals(
    reader: &mut MemoryReader,
    address: u64,
) -> Result<GcTable, ProbeError> {
    read_table_bounded(reader, address, 0x1fff)
}

fn read_table_bounded(
    reader: &mut MemoryReader,
    address: u64,
    max_hash_mask: u32,
) -> Result<GcTable, ProbeError> {
    if !reader.contains(address, GCTAB_SIZE) {
        return Err(ProbeError::read(
            "LuaJIT table header is outside validated maps",
        ));
    }
    let header = reader.read(address, GCTAB_SIZE)?;
    validate_table_header(address, &header, reader.ranges(), max_hash_mask)
        .ok_or_else(|| ProbeError::read("LuaJIT table header failed GC64 validation"))
}

fn validate_table_header(
    address: u64,
    header: &[u8],
    ranges: &[MemoryRange],
    max_hash_mask: u32,
) -> Option<GcTable> {
    if address % 8 != 0 || header.len() != GCTAB_SIZE || header[9] != GCT_TAB {
        return None;
    }
    let array_address = u64::from_le_bytes(header[16..24].try_into().ok()?);
    let node_address = u64::from_le_bytes(header[40..48].try_into().ok()?);
    let array_size = u32::from_le_bytes(header[48..52].try_into().ok()?);
    let hash_mask = u32::from_le_bytes(header[52..56].try_into().ok()?);
    if node_address == 0
        || node_address % 8 != 0
        || hash_mask > max_hash_mask
        || ((hash_mask + 1) & hash_mask) != 0
        || array_size > MAX_TABLE_ARRAY_SIZE
    {
        return None;
    }
    let node_bytes = (hash_mask as usize + 1).checked_mul(NODE_STRIDE)?;
    if !contains_sorted_range(ranges, node_address, node_bytes) {
        return None;
    }
    if array_size > 0 {
        let array_bytes = (array_size as usize).checked_mul(8)?;
        if array_address == 0 || !contains_sorted_range(ranges, array_address, array_bytes) {
            return None;
        }
    }
    Some(GcTable {
        address,
        array_address,
        node_address,
        array_size,
        hash_mask,
    })
}

pub fn read_table_nodes(
    reader: &mut MemoryReader,
    table: &GcTable,
) -> Result<Vec<TableNode>, ProbeError> {
    let mut nodes = Vec::new();
    let mut seen = HashSet::new();
    for bucket in 0..=table.hash_mask {
        let mut node_address = table.node_address + bucket as u64 * NODE_STRIDE as u64;
        let mut hops = 0u32;
        while node_address != 0 {
            if hops > table.hash_mask + 1 {
                return Err(ProbeError::read(
                    "LuaJIT hash-node chain is cyclic or too long",
                ));
            }
            if !seen.insert(node_address) {
                break;
            }
            if !reader.contains(node_address, NODE_STRIDE) {
                return Err(ProbeError::read(
                    "LuaJIT hash node is outside validated maps",
                ));
            }
            let blob = reader.read(node_address, NODE_STRIDE)?;
            let value = decode_tvalue(&blob[0..8])?;
            let key_raw = &blob[8..16];
            let key = read_table_key(reader, key_raw)?;
            let next_address = u64::from_le_bytes(
                blob[16..24]
                    .try_into()
                    .map_err(|_| ProbeError::read("LuaJIT hash-node link is truncated"))?,
            );
            if next_address != 0 && !reader.contains(next_address, NODE_STRIDE) {
                return Err(ProbeError::read(
                    "LuaJIT hash-node link points outside validated maps",
                ));
            }
            if key.is_some() {
                nodes.push(TableNode { key, value });
            }
            node_address = next_address;
            hops += 1;
        }
    }
    Ok(nodes)
}

fn read_table_key(reader: &mut MemoryReader, raw: &[u8]) -> Result<Option<TableKey>, ProbeError> {
    match decode_tvalue(raw)? {
        Value::Integer(number) => Ok(Some(TableKey::Number(number as f64))),
        Value::Number(number) => Ok(Some(TableKey::Number(number))),
        Value::Tagged { tag, address } if tag == LJ_TSTR => {
            Ok(Some(TableKey::String(read_gcstr(reader, address)?)))
        }
        _ => Ok(None),
    }
}

pub fn read_table_array(
    reader: &mut MemoryReader,
    table: &GcTable,
) -> Result<Vec<Value>, ProbeError> {
    if table.array_size == 0 {
        return Ok(Vec::new());
    }
    let byte_count = table.array_size as usize * 8;
    let blob = reader.read(table.array_address, byte_count)?;
    blob.chunks_exact(8).map(decode_tvalue).collect()
}

pub fn field_map(
    reader: &mut MemoryReader,
    address: u64,
) -> Result<HashMap<String, Value>, ProbeError> {
    let table = read_table(reader, address)?;
    let mut fields = HashMap::new();
    for node in read_table_nodes(reader, &table)? {
        if let Some(TableKey::String(key)) = node.key {
            fields.insert(key, node.value);
        }
    }
    Ok(fields)
}

pub fn unwrap_value(
    reader: &mut MemoryReader,
    value: &Value,
    depth: u8,
) -> Result<Option<Value>, ProbeError> {
    if depth > 2 {
        return Ok(None);
    }
    match value {
        Value::Integer(_) | Value::Number(_) | Value::Boolean(_) => Ok(Some(value.clone())),
        Value::Tagged { tag, address } if *tag == LJ_TTAB => {
            let table = read_table(reader, *address)?;
            let cells = read_table_array(reader, &table)?;
            if cells.len() == 3
                && matches!(cells.get(0), Some(Value::Nil))
                && matches!(cells.get(2), Some(Value::Nil))
            {
                unwrap_value(reader, &cells[1], depth + 1)
            } else {
                Ok(None)
            }
        }
        _ => Ok(None),
    }
}

/// Cold-scan anonymous private maps for GC64 strings, validated table headers,
/// and the hash node that owns the m_RoleIns key. No fixed addresses are used.
pub fn discover_manager(
    reader: &mut MemoryReader,
    progress: &mut impl FnMut(&str, u64, u64) -> Result<(), ProbeError>,
) -> Result<u64, ProbeError> {
    let ranges = reader.ranges().to_vec();
    let key_objects = scan_key_objects(reader, &ranges, progress)?;
    let role_ins_keys = key_objects
        .get("m_RoleIns")
        .filter(|items| !items.is_empty())
        .ok_or_else(|| ProbeError::read("manager field key m_RoleIns was not found"))?;
    let owner_nodes = scan_role_ins_owner_nodes(reader, &ranges, role_ins_keys, progress)?;
    let table_headers = scan_related_table_headers(reader, &ranges, &owner_nodes, progress)?;
    let possible_managers: HashSet<u64> = table_headers
        .into_iter()
        .map(|table| table.address)
        .collect();

    let mut validated = Vec::new();
    let mut battle_validated = Vec::new();
    let mut first_role_validation_error = None;
    for address in possible_managers {
        match read_manager_values(reader, address) {
            Ok(manager) => {
                validated.push((address, manager.battle_id, manager.round));
                if manager.battle_id == 0 || manager.round == 0 {
                    continue;
                }
                let positions = match read_role_position_map(reader, manager.position_table) {
                    Ok(positions) => positions,
                    Err(error) if error.kind == crate::model::ErrorKind::RootDenied => {
                        return Err(error)
                    }
                    Err(_) => continue,
                };
                match read_slots(reader, address, &manager, &positions) {
                    Ok(_) => battle_validated.push(address),
                    Err(error) if error.kind == crate::model::ErrorKind::RootDenied => {
                        return Err(error)
                    }
                    Err(error) => {
                        if first_role_validation_error.is_none() {
                            first_role_validation_error = Some(error.message);
                        }
                    }
                }
            }
            Err(error) if error.kind == crate::model::ErrorKind::RootDenied => return Err(error),
            Err(_) => {}
        }
    }
    if let Some(address) = select_manager_candidate(&validated, &battle_validated) {
        return Ok(address);
    }
    if !battle_validated.is_empty() || validated.is_empty() {
        return Err(ProbeError::read(format!(
            "cold manager discovery matched {} validated tables",
            validated.len()
        )));
    }
    if validated
        .iter()
        .any(|candidate| candidate.1 != 0 && candidate.2 > 0)
    {
        let detail = first_role_validation_error
            .map(|error| format!(": {error}"))
            .unwrap_or_default();
        return Err(ProbeError::read(format!(
            "cold manager discovery found active manager candidates without validated battle roles{detail}"
        )));
    }
    if validated.len() != 1 {
        return Err(ProbeError::read(format!(
            "cold manager discovery matched {} validated tables",
            validated.len()
        )));
    }
    Ok(validated[0].0)
}

fn select_manager_candidate(
    candidates: &[(u64, u64, u32)],
    battle_validated: &[u64],
) -> Option<u64> {
    match battle_validated {
        [address] => Some(*address),
        [] if candidates.len() == 1 && candidates[0].1 == 0 => Some(candidates[0].0),
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use super::{
        collect_role_ins_owner_nodes, index_table_headers_from_blob, select_manager_candidate,
        table_owns_candidate_node, tagged_gc_pointer, GCTAB_SIZE, GCT_TAB, LJ_TSTR,
    };
    use crate::model::MemoryRange;
    use std::collections::HashSet;

    #[test]
    fn unique_active_manager_wins_over_idle_candidates() {
        assert_eq!(
            select_manager_candidate(&[(10, 0, 0), (20, 1234, 1)], &[20]),
            Some(20)
        );
    }

    #[test]
    fn active_manager_without_validated_roles_is_rejected() {
        assert_eq!(select_manager_candidate(&[(20, 1234, 1)], &[]), None);
    }

    #[test]
    fn unrelated_table_shapes_do_not_fill_related_header_index() {
        let base = 0x1000_0000;
        let table_count = super::MAX_TABLE_HEADERS + 1;
        let header_bytes = table_count * GCTAB_SIZE;
        let node_start = base + header_bytes as u64;
        let mut blob = vec![0; header_bytes];
        for index in 0..table_count {
            let offset = index * GCTAB_SIZE;
            blob[offset + 9] = GCT_TAB;
            blob[offset + 40..offset + 48].copy_from_slice(&node_start.to_le_bytes());
            blob[offset + 52..offset + 56].copy_from_slice(&0u32.to_le_bytes());
        }
        let ranges = [MemoryRange {
            start: base,
            end: node_start + super::NODE_STRIDE as u64,
        }];
        let mut indexed = Vec::new();

        let result = index_table_headers_from_blob(
            &blob,
            base,
            base + header_bytes as u64,
            &ranges,
            &[],
            &mut indexed,
        );

        assert!(result.is_ok());
        assert!(indexed.is_empty());

        let mut related_indexed = Vec::new();
        let related_result = index_table_headers_from_blob(
            &blob,
            base,
            base + header_bytes as u64,
            &ranges,
            &[node_start],
            &mut related_indexed,
        );

        assert!(related_result.is_err());
        assert_eq!(related_indexed.len(), super::MAX_TABLE_HEADERS + 1);
    }

    #[test]
    fn m_roleins_owner_requires_aligned_reference_and_full_map_span() {
        let base = 0x2000_0000;
        let map_end = base + 0x100;
        let role_key = base + 0x180;
        let pointer = tagged_gc_pointer(role_key, LJ_TSTR).to_le_bytes();
        let mut blob = vec![0; 0x108];
        let valid_owner = map_end - super::NODE_STRIDE as u64;
        let outside_owner = map_end - 16;
        let unaligned_value = base + 0x41;
        for address in [valid_owner + 8, outside_owner + 8, unaligned_value] {
            let offset = (address - base) as usize;
            blob[offset..offset + 8].copy_from_slice(&pointer);
        }
        let ranges = [MemoryRange {
            start: base,
            end: map_end,
        }];
        let tagged_keys = HashSet::from([tagged_gc_pointer(role_key, LJ_TSTR)]);
        let mut candidates = HashSet::new();

        collect_role_ins_owner_nodes(&blob, base, &tagged_keys, &ranges, &mut candidates).unwrap();

        assert_eq!(candidates, HashSet::from([valid_owner]));
    }

    #[test]
    fn related_header_requires_owner_stride_and_keeps_chunk_overlap() {
        let base = 0x3000_0000;
        let header_address = base + 56;
        let node_start = base + 0xC0;
        let node_end = node_start + (2 * super::NODE_STRIDE) as u64;
        let mut blob = vec![0; 0x100];
        let header_offset = (header_address - base) as usize;
        blob[header_offset + 9] = GCT_TAB;
        blob[header_offset + 40..header_offset + 48].copy_from_slice(&node_start.to_le_bytes());
        blob[header_offset + 52..header_offset + 56].copy_from_slice(&1u32.to_le_bytes());
        let ranges = [MemoryRange {
            start: base,
            end: node_end,
        }];
        let mut indexed = Vec::new();

        index_table_headers_from_blob(
            &blob,
            base,
            base + 64,
            &ranges,
            &[node_start + super::NODE_STRIDE as u64],
            &mut indexed,
        )
        .unwrap();

        assert_eq!(indexed.len(), 1);
        assert_eq!(indexed[0].address, header_address);
        assert!(table_owns_candidate_node(
            node_start,
            node_end,
            &[node_start + super::NODE_STRIDE as u64]
        ));
        assert!(!table_owns_candidate_node(
            node_start,
            node_end,
            &[node_start + 8]
        ));
        assert!(!table_owns_candidate_node(
            node_start,
            node_end,
            &[node_end - 8]
        ));
    }

    #[test]
    fn large_globals_mask_is_allowed_only_by_the_attribute_bound() {
        let base = 0x4000_0000;
        let node = base + GCTAB_SIZE as u64;
        let mut header = vec![0u8; GCTAB_SIZE];
        header[9] = GCT_TAB;
        header[40..48].copy_from_slice(&node.to_le_bytes());
        header[52..56].copy_from_slice(&0x1fffu32.to_le_bytes());
        let ranges = [MemoryRange {
            start: base,
            end: node + (8192 * super::NODE_STRIDE) as u64,
        }];
        assert!(
            super::validate_table_header(base, &header, &ranges, super::MAX_HASH_MASK).is_none()
        );
        assert!(super::validate_table_header(base, &header, &ranges, 0x1fff).is_some());
    }
}

fn scan_key_objects(
    reader: &MemoryReader,
    ranges: &[MemoryRange],
    progress: &mut impl FnMut(&str, u64, u64) -> Result<(), ProbeError>,
) -> Result<HashMap<String, Vec<u64>>, ProbeError> {
    let mut key_objects: HashMap<String, Vec<u64>> = SCAN_KEYS
        .iter()
        .map(|key| ((*key).to_owned(), Vec::new()))
        .collect();
    let total = ranges.iter().fold(0u64, |sum, range| {
        sum.saturating_add(range.end.saturating_sub(range.start))
    });
    let mut done = 0u64;
    for range in ranges {
        if range.start % PAGE_SIZE != 0 || range.end % PAGE_SIZE != 0 {
            return Err(ProbeError::read(
                "anonymous map boundaries are not page aligned",
            ));
        }
        let mut core_start = range.start;
        while core_start < range.end {
            let core_end = (core_start + CHUNK_SIZE).min(range.end);
            let overlap_end = (core_end + GCTAB_SIZE as u64 + PAGE_SIZE - 1) & !(PAGE_SIZE - 1);
            let read_end = overlap_end.min(range.end);
            if read_end <= core_start || (read_end - core_start) % PAGE_SIZE != 0 {
                return Err(ProbeError::read(
                    "cold-discovery scan window is not page aligned",
                ));
            }
            let blob = reader.read_direct(core_start, (read_end - core_start) as usize)?;
            done = done.saturating_add(core_end - core_start);
            progress("tables", done, total)?;
            for offset in find_all(&blob, SCAN_KEY_PREFIX) {
                let Some(key) = SCAN_KEYS
                    .iter()
                    .find(|key| blob[offset..].starts_with(key.as_bytes()))
                else {
                    continue;
                };
                let bytes = key.as_bytes();
                if offset < GCSTR_SIZE {
                    continue;
                }
                let address = core_start + offset as u64 - GCSTR_SIZE as u64;
                if address < core_start || address >= core_end {
                    continue;
                }
                let object_offset = offset - GCSTR_SIZE;
                if blob[object_offset + 9] != GCSTR_GCT {
                    continue;
                }
                let length = u32::from_le_bytes(
                    blob[object_offset + 20..object_offset + 24]
                        .try_into()
                        .map_err(|_| {
                            ProbeError::read("cold-discovery GC string header is truncated")
                        })?,
                );
                if length as usize != bytes.len() {
                    continue;
                }
                let items = key_objects
                    .get_mut(*key)
                    .ok_or_else(|| ProbeError::read("manager field-key index is incomplete"))?;
                items.push(address);
                if items.len() > MAX_KEY_OBJECTS {
                    return Err(ProbeError::read(format!(
                        "GC string index for {key} exceeded its bound"
                    )));
                }
            }
            core_start = core_end;
        }
    }
    Ok(key_objects)
}

fn scan_role_ins_owner_nodes(
    reader: &MemoryReader,
    ranges: &[MemoryRange],
    role_ins_keys: &[u64],
    progress: &mut impl FnMut(&str, u64, u64) -> Result<(), ProbeError>,
) -> Result<Vec<u64>, ProbeError> {
    let tagged_keys: HashSet<u64> = role_ins_keys
        .iter()
        .map(|address| tagged_gc_pointer(*address, LJ_TSTR))
        .collect();
    let mut candidate_nodes = HashSet::new();
    let total = ranges.iter().fold(0u64, |sum, range| {
        sum.saturating_add(range.end.saturating_sub(range.start))
    });
    let mut done = 0u64;
    for range in ranges {
        if range.start % PAGE_SIZE != 0 || range.end % PAGE_SIZE != 0 {
            return Err(ProbeError::read(
                "anonymous map boundaries are not page aligned",
            ));
        }
        let mut previous_tail = Vec::new();
        let mut previous_end = None;
        let mut core_start = range.start;
        while core_start < range.end {
            let core_end = (core_start + CHUNK_SIZE).min(range.end);
            let blob = reader.read_direct(core_start, (core_end - core_start) as usize)?;
            done = done.saturating_add(core_end - core_start);
            progress("role_nodes", done, total)?;
            let mut scan_blob = Vec::with_capacity(previous_tail.len() + blob.len());
            if previous_end == Some(core_start) {
                scan_blob.extend_from_slice(&previous_tail);
            }
            scan_blob.extend_from_slice(&blob);
            let scan_start = core_start - (scan_blob.len() - blob.len()) as u64;
            collect_role_ins_owner_nodes(
                &scan_blob,
                scan_start,
                &tagged_keys,
                ranges,
                &mut candidate_nodes,
            )?;
            previous_tail = blob[blob.len().saturating_sub(7)..].to_vec();
            previous_end = Some(core_end);
            core_start = core_end;
        }
    }
    let mut owner_nodes: Vec<u64> = candidate_nodes.into_iter().collect();
    owner_nodes.sort_unstable();
    Ok(owner_nodes)
}

fn collect_role_ins_owner_nodes(
    blob: &[u8],
    blob_start: u64,
    tagged_keys: &HashSet<u64>,
    ranges: &[MemoryRange],
    candidate_nodes: &mut HashSet<u64>,
) -> Result<(), ProbeError> {
    let mut address = align_up(blob_start, 8)?;
    loop {
        let Some(offset) = address
            .checked_sub(blob_start)
            .and_then(|offset| usize::try_from(offset).ok())
        else {
            break;
        };
        let Some(end) = offset.checked_add(8) else {
            return Err(ProbeError::read("m_RoleIns reference scan offset overflow"));
        };
        let Some(raw) = blob.get(offset..end) else {
            break;
        };
        let value = u64::from_le_bytes(
            raw.try_into()
                .map_err(|_| ProbeError::read("m_RoleIns reference is truncated"))?,
        );
        if value >> 47 == (LJ_TSTR as u64 & GC64_TAG_MASK) && tagged_keys.contains(&value) {
            if let Some(owner_node) = address.checked_sub(8) {
                if owner_node % 8 == 0 && contains_sorted_range(ranges, owner_node, NODE_STRIDE) {
                    candidate_nodes.insert(owner_node);
                    if candidate_nodes.len() > MAX_CANDIDATE_NODES {
                        return Err(ProbeError::read(
                            "m_RoleIns owner-node index exceeded its bound",
                        ));
                    }
                }
            }
        }
        address = address
            .checked_add(8)
            .ok_or_else(|| ProbeError::read("m_RoleIns reference scan address overflow"))?;
    }
    Ok(())
}

fn scan_related_table_headers(
    reader: &MemoryReader,
    ranges: &[MemoryRange],
    owner_nodes: &[u64],
    progress: &mut impl FnMut(&str, u64, u64) -> Result<(), ProbeError>,
) -> Result<Vec<TableHeaderIndex>, ProbeError> {
    let total = ranges.iter().fold(0u64, |sum, range| {
        sum.saturating_add(range.end.saturating_sub(range.start))
    });
    let mut done = 0u64;
    let mut table_headers = Vec::new();
    for range in ranges {
        if range.start % PAGE_SIZE != 0 || range.end % PAGE_SIZE != 0 {
            return Err(ProbeError::read(
                "anonymous map boundaries are not page aligned",
            ));
        }
        let mut core_start = range.start;
        while core_start < range.end {
            let core_end = (core_start + CHUNK_SIZE).min(range.end);
            let overlap_end = (core_end + GCTAB_SIZE as u64 + PAGE_SIZE - 1) & !(PAGE_SIZE - 1);
            let read_end = overlap_end.min(range.end);
            if read_end <= core_start || (read_end - core_start) % PAGE_SIZE != 0 {
                return Err(ProbeError::read(
                    "cold-discovery scan window is not page aligned",
                ));
            }
            let blob = reader.read_direct(core_start, (read_end - core_start) as usize)?;
            done = done.saturating_add(core_end - core_start);
            progress("tables", done, total)?;
            index_table_headers_from_blob(
                &blob,
                core_start,
                core_end,
                ranges,
                owner_nodes,
                &mut table_headers,
            )?;
            core_start = core_end;
        }
    }
    Ok(table_headers)
}

fn index_table_headers_from_blob(
    blob: &[u8],
    core_start: u64,
    core_end: u64,
    ranges: &[MemoryRange],
    owner_nodes: &[u64],
    table_headers: &mut Vec<TableHeaderIndex>,
) -> Result<(), ProbeError> {
    let mut header_address = align_up(core_start, 8)?;
    while header_address < core_end {
        let offset = (header_address - core_start) as usize;
        if offset + GCTAB_SIZE <= blob.len() && blob[offset + 9] == GCT_TAB {
            if let Some(table) = validate_table_header(
                header_address,
                &blob[offset..offset + GCTAB_SIZE],
                ranges,
                MAX_HASH_MASK,
            ) {
                let node_bytes = (table.hash_mask as u64 + 1) * NODE_STRIDE as u64;
                let Some(node_end) = table.node_address.checked_add(node_bytes) else {
                    return Err(ProbeError::read("LuaJIT table node span overflow"));
                };
                if table_owns_candidate_node(table.node_address, node_end, owner_nodes) {
                    table_headers.push(TableHeaderIndex {
                        address: table.address,
                    });
                    if table_headers.len() > MAX_TABLE_HEADERS {
                        return Err(ProbeError::read(
                            "LuaJIT table-header index exceeded its bound",
                        ));
                    }
                }
            }
        }
        header_address += 8;
    }
    Ok(())
}

fn table_owns_candidate_node(node_start: u64, node_end: u64, owner_nodes: &[u64]) -> bool {
    let begin = owner_nodes.partition_point(|owner_node| *owner_node < node_start);
    owner_nodes[begin..]
        .iter()
        .take_while(|owner_node| **owner_node < node_end)
        .any(|owner_node| {
            owner_node
                .checked_add(NODE_STRIDE as u64)
                .is_some_and(|owner_end| {
                    owner_end <= node_end && (*owner_node - node_start) % NODE_STRIDE as u64 == 0
                })
        })
}

fn contains_sorted_range(ranges: &[MemoryRange], address: u64, size: usize) -> bool {
    let index = ranges.partition_point(|range| range.start <= address);
    index > 0 && ranges[index - 1].contains(address, size)
}

fn tagged_gc_pointer(address: u64, tag: u32) -> u64 {
    ((tag as u64) << 47) | (address & GC64_MASK)
}

fn find_all(haystack: &[u8], needle: &[u8]) -> Vec<usize> {
    if needle.is_empty() || needle.len() > haystack.len() {
        return Vec::new();
    }
    let mut result = Vec::new();
    let mut cursor = 0usize;
    while cursor + needle.len() <= haystack.len() {
        let Some(relative) = haystack[cursor..]
            .windows(needle.len())
            .position(|part| part == needle)
        else {
            break;
        };
        cursor += relative;
        result.push(cursor);
        cursor += 1;
    }
    result
}

fn align_up(value: u64, alignment: u64) -> Result<u64, ProbeError> {
    value
        .checked_add(alignment - 1)
        .map(|value| value & !(alignment - 1))
        .ok_or_else(|| ProbeError::read("address alignment overflow"))
}
