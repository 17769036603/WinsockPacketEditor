use crate::{
    memory::{MemoryReader, PAGE_SIZE},
    model::ProbeError,
};
use std::collections::{HashMap, HashSet};

const MASK: u64 = (1 << 47) - 1;
const STR: u32 = 0xffff_fffb;
const NUM: u32 = 0xffff_fff2;
const TRUE: u32 = 0xffff_fffd;
const FALSE: u32 = 0xffff_fffe;
const NIL: u32 = 0xffff_ffff;
const TAB: u8 = 11;
const FIELDS: [&str; 5] = [
    "m_PlayerId",
    "m_IsLocalPlayer",
    "m_RoleMoveSpeed",
    "m_Gx",
    "m_Gy",
];
#[derive(Clone, Debug)]
pub enum V {
    I(i32),
    F(f64),
    B(bool),
    Nil,
    Other,
}

fn positive_integer(value: &V) -> Option<i64> {
    match value {
        V::I(value) if *value > 0 => Some(*value as i64),
        V::F(value)
            if value.is_finite() && value.fract() == 0.0 && *value > 0.0 && *value < 9.22e18 =>
        {
            Some(*value as i64)
        }
        _ => None,
    }
}

fn cached_player_binding_matches(value: &V, cached_player: i64) -> bool {
    match value {
        V::Nil => true,
        value => positive_integer(value) == Some(cached_player),
    }
}

#[derive(Clone, Copy, Debug)]
pub struct Table {
    pub address: u64,
    pub nodes: u64,
    pub mask: u32,
}
#[derive(Clone, Debug)]
pub struct Candidate {
    pub table: u64,
    pub player: i64,
    pub speed_addr: u64,
    pub speed_raw: [u8; 8],
}

#[derive(Clone, Debug)]
pub struct FieldCache {
    table: u64,
    nodes: u64,
    mask: u32,
    player: i64,
    keys: [u64; 5],
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum RefreshPath {
    CacheHit,
    KnownTableScan,
    FullDiscovery,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum CacheFailureReason {
    TableUnavailable,
    CachedKeyUnreadable,
    CachedNodeUnreadable,
    NodeOutOfBounds,
    HashChainCorrupt,
    KeyMissingOrDuplicate,
    KeyChanged,
    FieldValueInvalid,
    LocalPlayerBindingChanged,
    CoordinatesInvalid,
    SpeedValueInvalid,
}

impl CacheFailureReason {
    pub fn token(self) -> &'static str {
        match self {
            Self::TableUnavailable => "table_unavailable",
            Self::CachedKeyUnreadable => "cached_key_unreadable",
            Self::CachedNodeUnreadable => "cached_node_unreadable",
            Self::NodeOutOfBounds => "node_out_of_bounds",
            Self::HashChainCorrupt => "hash_chain_corrupt",
            Self::KeyMissingOrDuplicate => "key_missing_or_duplicate",
            Self::KeyChanged => "key_changed",
            Self::FieldValueInvalid => "field_value_invalid",
            Self::LocalPlayerBindingChanged => "local_player_binding_changed",
            Self::CoordinatesInvalid => "coordinates_invalid",
            Self::SpeedValueInvalid => "speed_value_invalid",
        }
    }
}

fn make_field_cache(t: Table, keys: [u64; 5], player: i64) -> FieldCache {
    FieldCache {
        table: t.address,
        nodes: t.nodes,
        mask: t.mask,
        player,
        keys,
    }
}

fn unique_key_in_chain(chain: &[u64], key: u64, max_hops: usize) -> bool {
    chain.len() <= max_hops && chain.iter().filter(|&&value| value == key).count() == 1
}

fn node_in_bounds(t: Table, node: u64) -> bool {
    let Some(end) = (t.mask as u64 + 1)
        .checked_mul(24)
        .and_then(|size| t.nodes.checked_add(size))
    else {
        return false;
    };
    node >= t.nodes && node < end && (node - t.nodes) % 24 == 0
}
fn u32at(b: &[u8], i: usize) -> u32 {
    u32::from_le_bytes(b[i..i + 4].try_into().unwrap())
}
fn u64at(b: &[u8], i: usize) -> u64 {
    u64::from_le_bytes(b[i..i + 8].try_into().unwrap())
}
pub fn decode(b: &[u8]) -> Result<V, ProbeError> {
    if b.len() != 8 {
        return Err(ProbeError::read("invalid TValue size"));
    }
    let w = u64at(b, 0);
    let tag = ((w as i64) >> 47) as u32;
    Ok(if tag == NUM {
        V::I(u32at(b, 0) as i32)
    } else if tag == TRUE {
        V::B(true)
    } else if tag == FALSE {
        V::B(false)
    } else if tag == NIL {
        V::Nil
    } else if w >> 51 != 0x1fff {
        let n = f64::from_bits(w);
        if n.is_finite() {
            V::F(n)
        } else {
            V::Other
        }
    } else {
        V::Other
    })
}
fn tagptr(b: &[u8], want: u32) -> Option<u64> {
    let w = u64at(b, 0);
    ((((w as i64) >> 47) as u32) == want).then_some(w & MASK)
}
fn table(r: &mut MemoryReader, a: u64) -> Option<Table> {
    if a % 8 != 0 || !r.contains(a, 64) {
        return None;
    }
    let h = r.read(a, 64).ok()?;
    table_from_header(r, a, &h)
}

fn table_from_header(r: &MemoryReader, a: u64, h: &[u8]) -> Option<Table> {
    if a % 8 != 0 || h.len() < 64 || !r.contains(a, 64) {
        return None;
    }
    if h[9] != TAB {
        return None;
    }
    let nodes = u64at(&h, 40);
    let mask = u32at(&h, 52);
    if nodes == 0
        || nodes % 8 != 0
        || mask > 0x1000
        || ((mask + 1) & mask) != 0
        || !r.contains(nodes, (mask as usize + 1) * 24)
    {
        return None;
    }
    Some(Table {
        address: a,
        nodes,
        mask,
    })
}

fn table_owns_node(t: Table, nodes: &[u64]) -> bool {
    let end = t.nodes + (t.mask as u64 + 1) * 24;
    let index = nodes.partition_point(|node| *node < t.nodes);
    nodes[index..]
        .iter()
        .take_while(|node| **node < end)
        .any(|node| (*node - t.nodes) % 24 == 0)
}

fn has_external_next(t: Table, array: &[u8]) -> bool {
    let end = t.nodes + (t.mask as u64 + 1) * 24;
    array.chunks_exact(24).any(|node| {
        let next = u64at(node, 16);
        next != 0 && (next < t.nodes || next >= end || (next - t.nodes) % 24 != 0)
    })
}
fn gcstr(r: &mut MemoryReader, a: u64) -> Option<String> {
    if !r.contains(a, 24) {
        return None;
    }
    let h = r.read(a, 24).ok()?;
    if h[9] != 4 {
        return None;
    }
    let n = u32at(&h, 20) as usize;
    if n > 128 || !r.contains(a + 24, n) {
        return None;
    }
    String::from_utf8(r.read(a + 24, n).ok()?).ok()
}
pub fn speed_encode(speed: f64, old: &[u8]) -> Result<[u8; 8], ProbeError> {
    if !speed.is_finite() || speed <= 0.0 {
        return Err(ProbeError::read("target speed must be finite and positive"));
    }
    match decode(old)? {
        V::I(_) => {
            if speed.fract() != 0.0 || speed >= 2147483648.0 {
                return Err(ProbeError::read("i32 speed requires an in-range integer"));
            }
            let w = ((NUM as u64) << 47) | (speed as i32 as u32 as u64);
            Ok(w.to_le_bytes())
        }
        V::F(_) => {
            let b = speed.to_bits().to_le_bytes();
            if matches!(decode(&b)?, V::F(_)) {
                Ok(b)
            } else {
                Err(ProbeError::read("target cannot be encoded as LuaJIT f64"))
            }
        }
        _ => Err(ProbeError::read("speed TValue type is unsupported")),
    }
}
pub fn discover(r: &mut MemoryReader) -> Result<Candidate, ProbeError> {
    discover_impl(r, false).map(|(candidate, _)| candidate)
}

pub fn discover_seeded(
    r: &mut MemoryReader,
) -> Result<(Candidate, Option<FieldCache>), ProbeError> {
    discover_impl(r, true)
}

fn discover_impl(
    r: &mut MemoryReader,
    seed_cache: bool,
) -> Result<(Candidate, Option<FieldCache>), ProbeError> {
    let ranges = r.ranges().to_vec();
    let mut keys: HashMap<&str, HashSet<u64>> =
        FIELDS.iter().map(|x| (*x, HashSet::new())).collect();
    let mut tables = Vec::new();
    for range in &ranges {
        if range.start % PAGE_SIZE != 0 || range.end % PAGE_SIZE != 0 {
            continue;
        }
        let mut a = range.start;
        while a < range.end {
            let end = (a + 2 * 1024 * 1024).min(range.end);
            let blob_end = end.saturating_add(152).min(range.end);
            let blob = r.read_direct(a, (blob_end - a) as usize)?;
            let core_len = (end - a) as usize;
            let mut off = 0;
            while off < core_len && off + 24 <= blob.len() {
                if blob[off + 9] == 4 {
                    let n = u32at(&blob, off + 20) as usize;
                    if n > 0 && n <= 128 && off + 24 + n <= blob.len() {
                        let s = &blob[off + 24..off + 24 + n];
                        if let Ok(name) = std::str::from_utf8(s) {
                            if FIELDS.contains(&name) {
                                keys.get_mut(name).unwrap().insert(a + off as u64);
                            }
                        }
                    }
                }
                if (a + off as u64) % 8 == 0 && off + 64 <= blob.len() && blob[off + 9] == TAB {
                    let addr = a + off as u64;
                    if let Some(t) = table_from_header(r, addr, &blob[off..off + 64]) {
                        tables.push(t)
                    }
                }
                off += 8
            }
            a = end
        }
    }
    if keys.values().any(HashSet::is_empty) {
        return Err(ProbeError::read("role candidate not found"));
    }
    let all_keys: HashSet<u64> = keys
        .values()
        .flat_map(|items| items.iter().copied())
        .collect();
    let mut owner_nodes = Vec::new();
    for range in &ranges {
        if range.start % PAGE_SIZE != 0 || range.end % PAGE_SIZE != 0 {
            continue;
        }
        let mut a = range.start;
        while a < range.end {
            let end = (a + 2 * 1024 * 1024).min(range.end);
            let blob_end = end.saturating_add(23).min(range.end);
            let blob = r.read_direct(a, (blob_end - a) as usize)?;
            let core_len = (end - a) as usize;
            let mut off = ((8 - (a % 8)) % 8) as usize;
            while off < core_len && off + 24 <= blob.len() {
                let key = tagptr(&blob[off + 8..off + 16], STR);
                if key.is_some_and(|key| all_keys.contains(&key)) {
                    owner_nodes.push(a + off as u64);
                }
                off += 8;
            }
            a = end;
        }
    }
    owner_nodes.sort_unstable();
    owner_nodes.dedup();
    tables.retain(|t| {
        if table_owns_node(*t, &owner_nodes) {
            return true;
        }
        let Ok(array) = r.read_direct(t.nodes, (t.mask as usize + 1) * 24) else {
            return true;
        };
        has_external_next(*t, &array)
    });
    let mut found = Vec::new();
    for t in tables {
        let mut vals: HashMap<&str, (V, u64, u64)> = HashMap::new();
        let mut bad = false;
        let mut seen = HashSet::new();
        for i in 0..=t.mask {
            let mut n = t.nodes + i as u64 * 24;
            let mut hops = 0;
            while n != 0 && hops <= t.mask + 1 {
                if !seen.insert(n) {
                    break;
                }
                let b = match r.read(n, 24) {
                    Ok(v) => v,
                    Err(_) => {
                        bad = true;
                        break;
                    }
                };
                let key = tagptr(&b[8..16], STR);
                if let Some(k) = key {
                    if let Some(name) = FIELDS
                        .iter()
                        .find(|name| keys.get(**name).is_some_and(|ks| ks.contains(&k)))
                    {
                        if gcstr(r, k).as_deref() != Some(*name) || vals.contains_key(name) {
                            bad = true;
                            break;
                        }
                        vals.insert(name, (decode(&b[..8])?, n, k));
                    }
                }
                n = u64at(&b, 16);
                hops += 1
            }
            if bad {
                break;
            }
        }
        if bad || vals.len() != 5 {
            continue;
        }
        let get = |s: &str| vals.get(s).map(|x| &x.0);
        let player = match get("m_PlayerId").and_then(positive_integer) {
            Some(player) => player,
            None => continue,
        };
        if !matches!(get("m_IsLocalPlayer"), Some(V::B(true))) {
            continue;
        }
        for name in ["m_Gx", "m_Gy"] {
            let valid = match get(name) {
                Some(V::I(_)) => true,
                Some(V::F(x)) => x.is_finite() && *x >= -2147483648.0 && *x <= 2147483647.0,
                _ => false,
            };
            if !valid {
                bad = true;
            }
        }
        let _speed = match get("m_RoleMoveSpeed") {
            Some(V::I(_) | V::F(_)) => {}
            _ => bad = true,
        };
        if bad {
            continue;
        }
        let (_, node, _) = vals["m_RoleMoveSpeed"];
        let raw: [u8; 8] = r
            .read(node, 8)?
            .try_into()
            .map_err(|_| ProbeError::read("invalid speed raw"))?;
        if !matches!(decode(&raw)?,V::I(x) if x>0)
            && !matches!(decode(&raw)?,V::F(x) if x.is_finite()&&x>0.0)
        {
            continue;
        }
        let candidate = Candidate {
            table: t.address,
            player,
            speed_addr: node,
            speed_raw: raw,
        };
        let cache = seed_cache.then(|| {
            make_field_cache(
                t,
                std::array::from_fn(|index| vals[FIELDS[index]].2),
                player,
            )
        });
        let cache = cache.and_then(|cache| validate_home_chains(r, t, &cache).ok().map(|_| cache));
        found.push((candidate, cache));
    }
    if found.is_empty() {
        return Err(ProbeError::read("role candidate not found"));
    }
    if found.len() > 1 {
        return Err(ProbeError::read("role candidate is ambiguous"));
    }
    Ok(found.remove(0))
}

pub fn refresh(r: &mut MemoryReader, table_addr: u64) -> Result<Candidate, ProbeError> {
    refresh_scan(r, table_addr).map(|(candidate, _)| candidate)
}

fn refresh_scan(
    r: &mut MemoryReader,
    table_addr: u64,
) -> Result<(Candidate, FieldCache), ProbeError> {
    let t = table(r, table_addr).ok_or_else(|| ProbeError::expired("cached mount table moved"))?;
    let mut fields: HashMap<String, (V, u64, u64)> = HashMap::new();
    let mut seen = HashSet::new();
    for i in 0..=t.mask {
        let mut n = t.nodes + i as u64 * 24;
        let mut hops = 0;
        while n != 0 && hops <= t.mask + 1 {
            if n % 8 != 0 {
                return Err(ProbeError::expired("unaligned mount hash node"));
            }
            if !seen.insert(n) {
                break;
            }
            let b = r.read(n, 24)?;
            if let Some(k) = tagptr(&b[8..16], STR) {
                if let Some(name) = gcstr(r, k) {
                    if FIELDS.contains(&name.as_str()) {
                        if fields.contains_key(&name) {
                            return Err(ProbeError::read("duplicate mount field key"));
                        }
                        fields.insert(name, (decode(&b[..8])?, n, k));
                    }
                }
            }
            n = u64at(&b, 16);
            hops += 1
        }
    }
    if fields.len() != 5 {
        return Err(ProbeError::expired("mount table fields changed"));
    }
    if !matches!(
        fields.get("m_IsLocalPlayer").map(|x| &x.0),
        Some(V::B(true))
    ) {
        return Err(ProbeError::expired("local player binding changed"));
    }
    let player = positive_integer(&fields["m_PlayerId"].0)
        .ok_or_else(|| ProbeError::expired("player id changed"))?;
    for name in ["m_Gx", "m_Gy"] {
        match &fields[name].0 {
            V::I(v) => {
                let _ = *v;
            }
            V::F(v) if v.is_finite() && *v >= -2147483648.0 && *v <= 2147483647.0 => {}
            _ => return Err(ProbeError::read("invalid mount coordinates")),
        }
    }
    let raw: [u8; 8] = r
        .read(fields["m_RoleMoveSpeed"].1, 8)?
        .try_into()
        .map_err(|_| ProbeError::read("invalid speed TValue"))?;
    if !matches!(decode(&raw)?,V::I(v) if v>0)
        && !matches!(decode(&raw)?,V::F(v) if v.is_finite()&&v>0.0)
    {
        return Err(ProbeError::read("invalid speed TValue"));
    }
    let candidate = Candidate {
        table: table_addr,
        player,
        speed_addr: fields["m_RoleMoveSpeed"].1,
        speed_raw: raw,
    };
    let keys = std::array::from_fn(|index| fields[FIELDS[index]].2);
    let next_cache = make_field_cache(t, keys, player);
    Ok((candidate, next_cache))
}

pub fn refresh_cached(
    r: &mut MemoryReader,
    table_addr: u64,
    cache: &mut Option<FieldCache>,
) -> Result<
    (Candidate, RefreshPath, Option<CacheFailureReason>),
    (ProbeError, Option<CacheFailureReason>),
> {
    let had_cache = cache.is_some();
    let mut failure_reason = None;
    if let Some(cached) = cache.as_ref() {
        if let Some(t) = table(r, table_addr) {
            if t.address == cached.table && t.nodes == cached.nodes && t.mask == cached.mask {
                match cached_candidate(r, t, cached) {
                    Ok(candidate) => return Ok((candidate, RefreshPath::CacheHit, None)),
                    Err(CacheFailureReason::LocalPlayerBindingChanged) => {
                        failure_reason = Some(CacheFailureReason::LocalPlayerBindingChanged);
                        for delay in [50, 100] {
                            std::thread::sleep(std::time::Duration::from_millis(delay));
                            r.clear_cache();
                            let Some(current) = table(r, table_addr) else {
                                break;
                            };
                            if current.address != cached.table
                                || current.nodes != cached.nodes
                                || current.mask != cached.mask
                            {
                                break;
                            }
                            match cached_candidate(r, current, cached) {
                                Ok(candidate) => {
                                    return Ok((candidate, RefreshPath::CacheHit, None));
                                }
                                Err(CacheFailureReason::LocalPlayerBindingChanged) => {}
                                Err(_) => break,
                            }
                        }
                    }
                    Err(reason) => failure_reason = Some(reason),
                }
            } else {
                failure_reason = Some(CacheFailureReason::TableUnavailable);
            }
        } else {
            failure_reason = Some(CacheFailureReason::TableUnavailable);
        }
        *cache = None;
    }
    let (candidate, next_cache) = refresh_scan(r, table_addr).map_err(|error| {
        (
            error,
            had_cache.then_some(failure_reason.unwrap_or(CacheFailureReason::FieldValueInvalid)),
        )
    })?;
    *cache = table(r, table_addr).and_then(|t| {
        validate_home_chains(r, t, &next_cache)
            .ok()
            .map(|_| next_cache)
    });
    Ok((
        candidate,
        RefreshPath::KnownTableScan,
        had_cache.then_some(failure_reason.unwrap_or(CacheFailureReason::FieldValueInvalid)),
    ))
}

fn validate_home_chains(
    r: &mut MemoryReader,
    t: Table,
    cache: &FieldCache,
) -> Result<(), ProbeError> {
    for (&key, field) in cache.keys.iter().zip(FIELDS.iter()) {
        let sid = u32at(&r.read(key, 16)?, 12);
        let home = sid & t.mask;
        let mut n = t.nodes + home as u64 * 24;
        let mut hops = 0;
        let mut seen = HashSet::new();
        let mut chain_keys = Vec::new();
        while n != 0 && hops <= t.mask + 1 {
            if !node_in_bounds(t, n) {
                return Err(ProbeError::expired("mount field node out of bounds"));
            }
            if !seen.insert(n) {
                return Err(ProbeError::expired("corrupt mount hash chain"));
            }
            let b = r.read(n, 24)?;
            if let Some(k) = tagptr(&b[8..16], STR) {
                chain_keys.push(k);
                if k == key {
                    if gcstr(r, k).as_deref() != Some(*field) {
                        return Err(ProbeError::expired("mount field key changed"));
                    }
                }
            }
            n = u64at(&b, 16);
            hops += 1;
        }
        if hops > t.mask + 1 || !unique_key_in_chain(&chain_keys, key, t.mask as usize + 2) {
            return Err(ProbeError::expired("mount field chain changed"));
        }
    }
    Ok(())
}

fn cached_candidate(
    r: &mut MemoryReader,
    t: Table,
    cache: &FieldCache,
) -> Result<Candidate, CacheFailureReason> {
    let mut vals = Vec::with_capacity(5);
    for (index, &key) in cache.keys.iter().enumerate() {
        let key_header = r
            .read(key, 16)
            .map_err(|_| CacheFailureReason::CachedKeyUnreadable)?;
        let sid = u32at(&key_header, 12);
        let bucket = (sid & t.mask) as u64;
        let mut n = t
            .nodes
            .checked_add(
                bucket
                    .checked_mul(24)
                    .ok_or(CacheFailureReason::NodeOutOfBounds)?,
            )
            .ok_or(CacheFailureReason::NodeOutOfBounds)?;
        let mut hops = 0;
        let mut found = None;
        let mut seen = HashSet::new();
        let mut chain_keys = Vec::new();
        while n != 0 && hops <= t.mask + 1 {
            if !node_in_bounds(t, n) {
                return Err(CacheFailureReason::NodeOutOfBounds);
            }
            if !seen.insert(n) {
                return Err(CacheFailureReason::HashChainCorrupt);
            }
            let b = r
                .read(n, 24)
                .map_err(|_| CacheFailureReason::CachedNodeUnreadable)?;
            if let Some(current) = tagptr(&b[8..16], STR) {
                chain_keys.push(current);
            }
            if tagptr(&b[8..16], STR) == Some(key) {
                r.read(key, 24)
                    .map_err(|_| CacheFailureReason::CachedKeyUnreadable)?;
                match gcstr(r, key) {
                    None => return Err(CacheFailureReason::CachedKeyUnreadable),
                    Some(name) if name != FIELDS[index] => {
                        return Err(CacheFailureReason::KeyChanged)
                    }
                    Some(_) => (),
                }
                if found.is_some() {
                    return Err(CacheFailureReason::KeyMissingOrDuplicate);
                }
                let value = decode(&b[..8]).map_err(|_| CacheFailureReason::FieldValueInvalid)?;
                found = Some((value, n));
            }
            n = u64at(&b, 16);
            hops += 1;
        }
        if hops > t.mask + 1 {
            return Err(CacheFailureReason::HashChainCorrupt);
        }
        if !unique_key_in_chain(&chain_keys, key, t.mask as usize + 2) {
            return Err(CacheFailureReason::KeyMissingOrDuplicate);
        }
        vals.push(found.ok_or(CacheFailureReason::KeyMissingOrDuplicate)?);
    }
    let get = |name: &str| {
        FIELDS
            .iter()
            .position(|field| *field == name)
            .and_then(|index| vals.get(index))
    };
    if !matches!(
        &get("m_IsLocalPlayer")
            .ok_or(CacheFailureReason::KeyMissingOrDuplicate)?
            .0,
        V::B(true)
    ) {
        return Err(CacheFailureReason::LocalPlayerBindingChanged);
    }
    let player_value = &get("m_PlayerId")
        .ok_or(CacheFailureReason::KeyMissingOrDuplicate)?
        .0;
    if !cached_player_binding_matches(player_value, cache.player) {
        return Err(CacheFailureReason::LocalPlayerBindingChanged);
    }
    for name in ["m_Gx", "m_Gy"] {
        match &get(name)
            .ok_or(CacheFailureReason::KeyMissingOrDuplicate)?
            .0
        {
            V::I(_) => (),
            V::F(v) if v.is_finite() && (-2147483648.0..=2147483647.0).contains(v) => (),
            _ => return Err(CacheFailureReason::CoordinatesInvalid),
        }
    }
    let speed_node = get("m_RoleMoveSpeed")
        .ok_or(CacheFailureReason::KeyMissingOrDuplicate)?
        .1;
    let speed_raw: [u8; 8] = r
        .read(speed_node, 8)
        .map_err(|_| CacheFailureReason::CachedNodeUnreadable)?
        .try_into()
        .map_err(|_| CacheFailureReason::SpeedValueInvalid)?;
    let speed_valid = match decode(&speed_raw).map_err(|_| CacheFailureReason::SpeedValueInvalid)? {
        V::I(value) => value > 0,
        V::F(value) => value.is_finite() && value > 0.0,
        _ => false,
    };
    if !speed_valid {
        return Err(CacheFailureReason::SpeedValueInvalid);
    }
    Ok(Candidate {
        table: t.address,
        player: cache.player,
        speed_addr: speed_node,
        speed_raw,
    })
}

#[cfg(test)]
mod cache_tests {
    use super::{
        has_external_next, make_field_cache, node_in_bounds, positive_integer, table_owns_node,
        unique_key_in_chain, CacheFailureReason, Table, V,
    };

    #[test]
    fn cache_failure_reason_tokens_are_fixed_and_complete() {
        let reasons = [
            (CacheFailureReason::TableUnavailable, "table_unavailable"),
            (
                CacheFailureReason::CachedKeyUnreadable,
                "cached_key_unreadable",
            ),
            (
                CacheFailureReason::CachedNodeUnreadable,
                "cached_node_unreadable",
            ),
            (CacheFailureReason::NodeOutOfBounds, "node_out_of_bounds"),
            (CacheFailureReason::HashChainCorrupt, "hash_chain_corrupt"),
            (
                CacheFailureReason::KeyMissingOrDuplicate,
                "key_missing_or_duplicate",
            ),
            (CacheFailureReason::KeyChanged, "key_changed"),
            (CacheFailureReason::FieldValueInvalid, "field_value_invalid"),
            (
                CacheFailureReason::LocalPlayerBindingChanged,
                "local_player_binding_changed",
            ),
            (
                CacheFailureReason::CoordinatesInvalid,
                "coordinates_invalid",
            ),
            (CacheFailureReason::SpeedValueInvalid, "speed_value_invalid"),
        ];
        assert!(reasons
            .iter()
            .all(|(reason, token)| reason.token() == *token));
    }

    #[test]
    fn discovery_seed_keeps_table_identity_and_exact_field_keys() {
        let table = Table {
            address: 0x1000,
            nodes: 0x2000,
            mask: 7,
        };
        let keys = [1, 2, 3, 4, 5];
        let cache = make_field_cache(table, keys, 123);
        assert_eq!(cache.table, table.address);
        assert_eq!(cache.nodes, table.nodes);
        assert_eq!(cache.mask, table.mask);
        assert_eq!(cache.player, 123);
        assert_eq!(cache.keys, keys);
    }

    #[test]
    fn positive_integer_accepts_integer_float_only() {
        assert_eq!(positive_integer(&V::F(42.0)), Some(42));
        assert_eq!(positive_integer(&V::F(42.5)), None);
        assert_eq!(positive_integer(&V::F(f64::INFINITY)), None);
        assert_eq!(positive_integer(&V::F(f64::NAN)), None);
    }

    #[test]
    fn reachable_key_is_unique_and_bad_chain_invalidates() {
        assert!(unique_key_in_chain(&[8, 7], 7, 4));
        assert!(!unique_key_in_chain(&[7, 7], 7, 4));
        assert!(!unique_key_in_chain(&[7, 8, 9], 7, 2));
        assert!(!unique_key_in_chain(&[8, 9], 7, 4));
    }

    #[test]
    fn cached_nodes_must_be_aligned_inside_the_node_array() {
        let table = Table {
            address: 0x1000,
            nodes: 0x2000,
            mask: 3,
        };
        assert!(node_in_bounds(table, 0x2000));
        assert!(node_in_bounds(table, 0x2048));
        assert!(!node_in_bounds(table, 0x2058));
        assert!(!node_in_bounds(table, 0x2060));
        assert!(!node_in_bounds(table, 0x2008));
        assert!(!node_in_bounds(table, 0x1ff8));
    }

    #[test]
    fn owner_filter_uses_sorted_nodes_and_exact_24_byte_stride() {
        let table = Table {
            address: 0x1000,
            nodes: 0x2000,
            mask: 3,
        };
        assert!(table_owns_node(table, &[0x1ff8, 0x2030, 0x3000]));
        assert!(table_owns_node(table, &[0x2008, 0x2030, 0x3000]));
        assert!(!table_owns_node(table, &[0x2008, 0x3000]));
        assert!(!table_owns_node(table, &[0x2060, 0x3000]));
    }

    #[test]
    fn external_or_misaligned_next_keeps_table_conservatively() {
        let table = Table {
            address: 0x1000,
            nodes: 0x2000,
            mask: 0,
        };
        let mut array = [0u8; 24];
        assert!(!has_external_next(table, &array));
        array[16..24].copy_from_slice(&0x3000u64.to_le_bytes());
        assert!(has_external_next(table, &array));
        array[16..24].copy_from_slice(&0x2008u64.to_le_bytes());
        assert!(has_external_next(table, &array));
    }
}

#[cfg(test)]
mod discovery_tests {
    use super::{discover_seeded, Candidate};
    use crate::{memory::MemoryReader, model::MemoryRange};
    use std::{
        fs::{self, File, OpenOptions},
        os::unix::fs::FileExt,
        path::PathBuf,
        time::{SystemTime, UNIX_EPOCH},
    };

    const SIZE: usize = 6 * 1024 * 1024;
    const CHUNK: u64 = 2 * 1024 * 1024;
    const NAMES: [&str; 5] = [
        "m_PlayerId",
        "m_IsLocalPlayer",
        "m_RoleMoveSpeed",
        "m_Gx",
        "m_Gy",
    ];

    struct Image {
        dir: PathBuf,
        path: PathBuf,
        reader: MemoryReader,
    }
    impl Drop for Image {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.dir);
        }
    }

    fn image() -> Image {
        let stamp = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        let dir =
            std::env::temp_dir().join(format!("mount-discovery-{}-{stamp}", std::process::id()));
        fs::create_dir(&dir).unwrap();
        let path = dir.join("memory.bin");
        let file = OpenOptions::new()
            .create_new(true)
            .read(true)
            .write(true)
            .open(&path)
            .unwrap();
        file.set_len(SIZE as u64).unwrap();
        Image {
            dir,
            path,
            reader: MemoryReader::from_test_file(
                file,
                vec![MemoryRange {
                    start: 0,
                    end: SIZE as u64,
                }],
            ),
        }
    }

    fn write_node(file: &File, node: u64, key: u64, value: u64) {
        let mut bytes = [0u8; 24];
        bytes[..8].copy_from_slice(&value.to_le_bytes());
        bytes[8..16].copy_from_slice(&(((0xffff_fffbu64) << 47) | key).to_le_bytes());
        file.write_at(&bytes, node).unwrap();
    }

    fn values(index: usize) -> u64 {
        match index {
            0 => (0xffff_fff2u64 << 47) | 123,
            1 => 0xffff_fffdu64 << 47,
            2 => 1200.0f64.to_bits(),
            3 => 1.0f64.to_bits(),
            _ => 2.0f64.to_bits(),
        }
    }

    fn candidate(file: &File, table: u64, nodes: u64, keys: u64, external: bool) -> Candidate {
        let mut header = [0u8; 64];
        header[9] = 11;
        header[40..48].copy_from_slice(&nodes.to_le_bytes());
        header[52..56].copy_from_slice(&7u32.to_le_bytes());
        file.write_at(&header, table).unwrap();
        for (index, name) in NAMES.iter().enumerate() {
            let key = keys + index as u64 * 0x100;
            let mut gc = [0u8; 24];
            gc[9] = 4;
            gc[12..16].copy_from_slice(&(index as u32).to_le_bytes());
            gc[20..24].copy_from_slice(&(name.len() as u32).to_le_bytes());
            file.write_at(&gc, key).unwrap();
            file.write_at(name.as_bytes(), key + 24).unwrap();
            let node = if external {
                0x300000 + index as u64 * 24
            } else {
                nodes + index as u64 * 24
            };
            write_node(file, node, key, values(index));
            if external {
                let bucket = nodes + index as u64 * 24;
                file.write_at(&node.to_le_bytes(), bucket + 16).unwrap();
            }
        }
        Candidate {
            table,
            player: 123,
            speed_addr: if external {
                0x300000 + 2 * 24
            } else {
                nodes + 2 * 24
            },
            speed_raw: values(2).to_le_bytes(),
        }
    }

    fn open_image(i: Image) -> (Image, File) {
        let file = OpenOptions::new()
            .read(true)
            .write(true)
            .open(&i.path)
            .unwrap();
        (i, file)
    }

    #[test]
    fn seeded_discovery_selects_only_the_unique_complete_mount_table() {
        let (mut i, file) = open_image(image());
        let expected = candidate(&file, 0x1000, 0x4000, 0x8000, false);
        // A structurally valid unrelated table has no mount fields.
        let mut unrelated = [0u8; 64];
        unrelated[9] = 11;
        unrelated[40..48].copy_from_slice(&0x5000u64.to_le_bytes());
        unrelated[52..56].copy_from_slice(&7u32.to_le_bytes());
        file.write_at(&unrelated, 0x2000).unwrap();
        let (found, cache) = discover_seeded(&mut i.reader).unwrap();
        assert_eq!(
            (found.table, found.player, found.speed_addr, found.speed_raw),
            (
                expected.table,
                expected.player,
                expected.speed_addr,
                expected.speed_raw
            )
        );
        assert!(cache.is_some());
    }

    #[test]
    fn seeded_discovery_rejects_a_missing_required_key() {
        let (mut i, file) = open_image(image());
        candidate(&file, 0x1000, 0x4000, 0x8000, false);
        file.write_at(&[0], 0x8400 + 9).unwrap();
        assert_eq!(
            discover_seeded(&mut i.reader).unwrap_err().message,
            "role candidate not found"
        );
    }

    #[test]
    fn five_field_role_discovery_and_refresh_need_no_ride_id() {
        let (mut i, file) = open_image(image());
        let expected = candidate(&file, 0x1000, 0x4000, 0x8000, false);
        let (found, mut cache) = discover_seeded(&mut i.reader).unwrap();
        assert_eq!(found.speed_addr, expected.speed_addr);
        let (cached, path, failure) =
            super::refresh_cached(&mut i.reader, found.table, &mut cache).unwrap();
        assert_eq!(path, super::RefreshPath::CacheHit);
        assert_eq!(failure, None);
        assert_eq!(cached.speed_addr, expected.speed_addr);
        assert_eq!(
            super::refresh(&mut i.reader, found.table).unwrap().player,
            123
        );
    }

    #[test]
    fn seeded_discovery_and_cache_accept_positive_integral_float_player_id() {
        let (mut i, file) = open_image(image());
        let expected = candidate(&file, 0x1000, 0x4000, 0x8000, false);
        file.write_at(&42.0f64.to_bits().to_le_bytes(), 0x4000)
            .unwrap();

        let (found, mut cache) = discover_seeded(&mut i.reader).unwrap();
        assert_eq!((found.player, found.speed_addr), (42, expected.speed_addr));
        let (cached, path, failure) =
            super::refresh_cached(&mut i.reader, found.table, &mut cache).unwrap();
        assert_eq!(path, super::RefreshPath::CacheHit);
        assert_eq!(failure, None);
        assert_eq!(cached.player, 42);
        assert_eq!(
            super::refresh(&mut i.reader, found.table).unwrap().player,
            42
        );
    }

    #[test]
    fn cached_local_role_compares_positive_player_id_but_tolerates_nil_and_checks_local_flag() {
        let (mut i, file) = open_image(image());
        candidate(&file, 0x1000, 0x4000, 0x8000, false);
        let (found, mut cache) = discover_seeded(&mut i.reader).unwrap();

        file.write_at(&values(0).to_le_bytes(), 0x4000).unwrap();
        i.reader.clear_cache();
        let (cached, path, failure) =
            super::refresh_cached(&mut i.reader, found.table, &mut cache).unwrap();
        assert_eq!(path, super::RefreshPath::CacheHit);
        assert_eq!(failure, None);
        assert_eq!(cached.player, found.player);

        file.write_at(&(0xffff_ffffu64 << 47).to_le_bytes(), 0x4000)
            .unwrap();
        i.reader.clear_cache();
        let (cached, path, failure) =
            super::refresh_cached(&mut i.reader, found.table, &mut cache).unwrap();
        assert_eq!(path, super::RefreshPath::CacheHit);
        assert_eq!(failure, None);
        assert_eq!(cached.player, found.player);

        file.write_at(&((0xffff_fff2u64 << 47) | 456).to_le_bytes(), 0x4000)
            .unwrap();
        i.reader.clear_cache();
        let (rescanned, path, failure) =
            super::refresh_cached(&mut i.reader, found.table, &mut cache).unwrap();
        assert_eq!(path, super::RefreshPath::KnownTableScan);
        assert_eq!(
            failure,
            Some(super::CacheFailureReason::LocalPlayerBindingChanged)
        );
        assert_eq!(rescanned.player, 456);

        i.reader.clear_cache();
        let (cached, path, failure) =
            super::refresh_cached(&mut i.reader, found.table, &mut cache).unwrap();
        assert_eq!(path, super::RefreshPath::CacheHit);
        assert_eq!(failure, None);
        assert_eq!(cached.player, 456);

        file.write_at(&(0xffff_fffeu64 << 47).to_le_bytes(), 0x4018)
            .unwrap();
        i.reader.clear_cache();
        assert!(matches!(
            super::refresh_cached(&mut i.reader, found.table, &mut cache),
            Err((error, _)) if error.message == "local player binding changed"
        ));
    }

    #[test]
    fn cached_local_flag_retry_accepts_same_candidate_after_flag_recovers() {
        let (mut i, file) = open_image(image());
        candidate(&file, 0x1000, 0x4000, 0x8000, false);
        let (found, mut cache) = discover_seeded(&mut i.reader).unwrap();
        file.write_at(&(0xffff_fffeu64 << 47).to_le_bytes(), 0x4018)
            .unwrap();
        i.reader.clear_cache();

        let writer = file.try_clone().unwrap();
        let updater = std::thread::spawn(move || {
            std::thread::sleep(std::time::Duration::from_millis(80));
            writer
                .write_at(&(0xffff_fffdu64 << 47).to_le_bytes(), 0x4018)
                .unwrap();
        });
        let (cached, path, failure) =
            super::refresh_cached(&mut i.reader, found.table, &mut cache).unwrap();
        updater.join().unwrap();
        assert_eq!(path, super::RefreshPath::CacheHit);
        assert_eq!(failure, None);
        assert_eq!(cached.speed_addr, found.speed_addr);
    }

    #[test]
    fn role_discovery_refresh_and_cache_ignore_absent_zero_or_invalid_ride_id() {
        for ride_id in [None, Some(0), Some(0xffff_fffbu64 << 47)] {
            let (mut i, file) = open_image(image());
            let expected = candidate(&file, 0x1000, 0x4000, 0x8000, false);
            if let Some(value) = ride_id {
                let key = 0x8500;
                let name = "RideId";
                let mut gc = [0u8; 24];
                gc[9] = 4;
                gc[12..16].copy_from_slice(&5u32.to_le_bytes());
                gc[20..24].copy_from_slice(&(name.len() as u32).to_le_bytes());
                file.write_at(&gc, key).unwrap();
                file.write_at(name.as_bytes(), key + 24).unwrap();
                write_node(&file, 0x4000 + 5 * 24, key, value);
            }

            let (found, mut cache) = discover_seeded(&mut i.reader).unwrap();
            assert_eq!((found.player, found.speed_addr), (123, expected.speed_addr));
            let (cached, path, failure) =
                super::refresh_cached(&mut i.reader, found.table, &mut cache).unwrap();
            assert_eq!(path, super::RefreshPath::CacheHit);
            assert_eq!(failure, None);
            assert_eq!(cached.speed_addr, expected.speed_addr);
            assert_eq!(
                super::refresh(&mut i.reader, found.table).unwrap().player,
                123
            );
        }
    }

    #[test]
    fn discovery_rejects_nonlocal_and_invalid_player_tables() {
        for (node, value) in [
            (0x4000 + 24, 0xffff_fffeu64 << 47),
            (0x4000, 0xffff_fff2u64 << 47),
        ] {
            let (mut i, file) = open_image(image());
            candidate(&file, 0x1000, 0x4000, 0x8000, false);
            file.write_at(&value.to_le_bytes(), node).unwrap();
            assert_eq!(
                discover_seeded(&mut i.reader).unwrap_err().message,
                "role candidate not found"
            );
        }
    }

    #[test]
    fn seeded_discovery_reports_two_valid_tables_as_ambiguous() {
        let (mut i, file) = open_image(image());
        candidate(&file, 0x1000, 0x4000, 0x8000, false);
        candidate(&file, 0x2000, 0x6000, 0x9000, false);
        assert_eq!(
            discover_seeded(&mut i.reader).unwrap_err().message,
            "role candidate is ambiguous"
        );
    }

    #[test]
    fn external_target_nodes_remain_candidates_and_participate_in_ambiguity() {
        let (mut i, file) = open_image(image());
        let expected = candidate(&file, 0x1000, 0x4000, 0x8000, true);
        let (found, cache) = discover_seeded(&mut i.reader).unwrap();
        assert_eq!(
            (found.table, found.speed_addr),
            (expected.table, expected.speed_addr)
        );
        assert!(cache.is_none());

        let (mut i, file) = open_image(image());
        candidate(&file, 0x1000, 0x4000, 0x8000, true);
        candidate(&file, 0x2000, 0x6000, 0x9000, false);
        assert_eq!(
            discover_seeded(&mut i.reader).unwrap_err().message,
            "role candidate is ambiguous"
        );
    }

    #[test]
    fn refresh_rejects_unaligned_hash_chain_next_pointer() {
        let (mut i, file) = open_image(image());
        candidate(&file, 0x1000, 0x4000, 0x8000, false);
        file.write_at(&0x5001u64.to_le_bytes(), 0x4000 + 16)
            .unwrap();
        assert_eq!(
            super::refresh(&mut i.reader, 0x1000).unwrap_err().message,
            "unaligned mount hash node"
        );
    }

    #[test]
    fn discovery_reads_gcstr_table_header_and_target_node_across_scan_chunks() {
        for (kind, object) in [(0, "key"), (1, "table"), (2, "node")] {
            let (mut i, file) = open_image(image());
            let table = 0x1000;
            let nodes = 0x4000;
            let keys = 0x8000;
            let expected = candidate(&file, table, nodes, keys, false);
            let boundary = CHUNK;
            match kind {
                0 => {
                    let name = NAMES[0];
                    let mut gc = [0u8; 24];
                    gc[9] = 4;
                    gc[12..16].copy_from_slice(&0u32.to_le_bytes());
                    gc[20..24].copy_from_slice(&(name.len() as u32).to_le_bytes());
                    file.write_at(&gc, boundary - 8).unwrap();
                    file.write_at(name.as_bytes(), boundary + 16).unwrap();
                    let old_node = nodes;
                    write_node(&file, old_node, boundary - 8, values(0));
                }
                1 => {
                    let mut header = [0u8; 64];
                    header[9] = 11;
                    header[40..48].copy_from_slice(&nodes.to_le_bytes());
                    header[52..56].copy_from_slice(&7u32.to_le_bytes());
                    file.write_at(&[0u8; 64], table).unwrap();
                    file.write_at(&header, boundary - 32).unwrap();
                }
                _ => {
                    let key = keys + 2 * 0x100;
                    let node = boundary - 8;
                    file.write_at(&[0u8; 24], nodes + 2 * 24).unwrap();
                    write_node(&file, node, key, values(2));
                    file.write_at(&node.to_le_bytes(), nodes + 2 * 24 + 16)
                        .unwrap();
                }
            }
            let result = discover_seeded(&mut i.reader);
            assert!(
                result.is_ok(),
                "{object} crossing chunk boundary: {result:?}"
            );
            let expected_table = if kind == 1 {
                boundary - 32
            } else {
                expected.table
            };
            assert_eq!(result.unwrap().0.table, expected_table);
        }
    }
}
