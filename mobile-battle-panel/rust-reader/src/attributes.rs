use crate::luajit::{
    numeric_i64, read_attribute_globals, read_table_array, read_table_nodes, tagged_pointer_value,
    unwrap_value, GcTable, TableKey, Value, LJ_TFUNC, LJ_TTAB,
};
use crate::memory::MemoryReader;
use crate::model::ProbeError;
use std::collections::{HashMap, HashSet};

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct AttributeWitness {
    pub root_chain: Vec<(String, u64)>,
    pub player_speeds: HashMap<i64, i64>,
    pub pet_speeds: HashMap<i64, i64>,
    pub partner_speeds: HashMap<i64, i64>,
    pub partner_speed_parts: Vec<(i64, i64)>,
}

const GLOBALS_HASH_MASK_MAX: u32 = 0x1fff;

fn fields(
    reader: &mut MemoryReader,
    table: &GcTable,
) -> Result<HashMap<String, Value>, ProbeError> {
    let mut out = HashMap::new();
    for node in read_table_nodes(reader, table)? {
        if let Some(TableKey::String(key)) = node.key {
            out.insert(key, node.value);
        }
    }
    Ok(out)
}

fn field_table(
    reader: &mut MemoryReader,
    parent: &GcTable,
    name: &str,
) -> Result<GcTable, ProbeError> {
    let value = fields(reader, parent)?
        .remove(name)
        .ok_or_else(|| ProbeError::read(format!("attribute root is missing {name}")))?;
    let address = tagged_pointer_value(&value, LJ_TTAB)
        .ok_or_else(|| ProbeError::read(format!("attribute root {name} is not a table")))?;
    crate::luajit::read_table(reader, address)
}

fn scalar(reader: &mut MemoryReader, value: &Value) -> Result<i64, ProbeError> {
    let value = unwrap_value(reader, value, 0)?
        .ok_or_else(|| ProbeError::read("attribute is not a scalar TValue"))?;
    numeric_i64(&value).ok_or_else(|| ProbeError::read("attribute is not an integer"))
}

fn property_sp(reader: &mut MemoryReader, owner: &GcTable) -> Result<i64, ProbeError> {
    let dict = field_table(reader, owner, "PropertyValueDict")?;
    let sp = fields(reader, &dict)?
        .remove("SP")
        .ok_or_else(|| ProbeError::read("PropertyValueDict.SP is absent"))?;
    scalar(reader, &sp)
}

fn indexed_values(reader: &mut MemoryReader, table: &GcTable) -> Result<Vec<Value>, ProbeError> {
    let mut values = read_table_array(reader, table)?;
    for node in read_table_nodes(reader, table)? {
        if matches!(node.key, Some(TableKey::Number(_))) {
            values.push(node.value);
        }
    }
    Ok(values)
}

fn object_id(reader: &mut MemoryReader, object: &GcTable, field: &str) -> Result<i64, ProbeError> {
    let value = fields(reader, object)?
        .remove(field)
        .ok_or_else(|| ProbeError::read(format!("attribute object is missing {field}")))?;
    scalar(reader, &value)
}

fn insert_unique(speeds: &mut HashMap<i64, i64>, id: i64, value: i64) -> Result<(), ProbeError> {
    if speeds.insert(id, value).is_some() {
        return Err(ProbeError::read("duplicate exact-ID attribute binding"));
    }
    Ok(())
}

fn add_partner_speed(total: i64, value: i64) -> Result<i64, ProbeError> {
    total
        .checked_add(value)
        .ok_or_else(|| ProbeError::read("partner speed sum overflow"))
}

fn read_cache(
    reader: &mut MemoryReader,
    local_userdata: u64,
    player_id: i64,
    own_main_ids: &HashSet<i64>,
    own_pet_ids: &HashSet<i64>,
) -> Result<AttributeWitness, ProbeError> {
    if !reader.contains(local_userdata, 24) {
        return Err(ProbeError::read(
            "local role userdata is outside current maps",
        ));
    }
    let userdata = reader.read(local_userdata, 24)?;
    if userdata[9] != 12 {
        return Err(ProbeError::read("local role is not userdata"));
    }
    let role_env = u64::from_le_bytes(
        userdata[16..24]
            .try_into()
            .map_err(|_| ProbeError::read("userdata env is truncated"))?,
    );
    let role_table = crate::luajit::read_table(reader, role_env)?;
    let role_fields = fields(reader, &role_table)?;
    let init = role_fields
        .get("initData")
        .and_then(|v| tagged_pointer_value(v, LJ_TFUNC))
        .ok_or_else(|| ProbeError::read("current BattleRole.initData closure is unavailable"))?;
    if !reader.contains(init, 48) {
        return Err(ProbeError::read("initData closure is outside current maps"));
    }
    let closure = reader.read(init, 48)?;
    if closure[9] != 8 || closure[10] != 0 {
        return Err(ProbeError::read("initData is not a Lua closure"));
    }
    let globals = u64::from_le_bytes(
        closure[16..24]
            .try_into()
            .map_err(|_| ProbeError::read("closure env is truncated"))?,
    );
    let global_table = read_attribute_globals(reader, globals)?;
    if global_table.hash_mask > GLOBALS_HASH_MASK_MAX {
        return Err(ProbeError::read("globals exceed dedicated bound"));
    }
    let class_ins = field_table(reader, &global_table, "ClassIns")?;
    let mut chain = vec![
        ("role_userdata".to_owned(), local_userdata),
        ("role_env".to_owned(), role_env),
        ("initData".to_owned(), init),
        ("globals".to_owned(), globals),
        ("ClassIns".to_owned(), class_ins.address),
    ];
    let mut player_speeds = HashMap::new();
    let mut pet_speeds = HashMap::new();
    let mut partner_speeds = HashMap::new();
    let mut partner_speed_parts = Vec::new();

    if let Ok(local_instance) = field_table(reader, &class_ins, "LocalPlayerMgr.Ins") {
        if let Ok(current_player) = field_table(reader, &local_instance, "m_LocalPlayer") {
            if object_id(reader, &current_player, "m_PlayerId").ok() == Some(player_id) {
                if let Ok(speed) = property_sp(reader, &current_player) {
                    player_speeds.insert(player_id, speed);
                    chain.extend([
                        ("LocalPlayerMgr.Ins".to_owned(), local_instance.address),
                        (
                            "LocalPlayerMgr.Ins.m_LocalPlayer".to_owned(),
                            current_player.address,
                        ),
                    ]);
                    property_refs(
                        reader,
                        &current_player,
                        "LocalPlayerMgr.Ins.m_LocalPlayer",
                        &mut chain,
                    );
                }
            }
        }
    }

    if let Ok(pets_instance) = field_table(reader, &class_ins, "PetMgr.Ins") {
        if let Ok(pets) = field_table(reader, &pets_instance, "m_Pets") {
            for node in read_table_nodes(reader, &pets).unwrap_or_default() {
                let (Some(TableKey::Number(key)), Some(address)) =
                    (node.key, tagged_pointer_value(&node.value, LJ_TTAB))
                else {
                    continue;
                };
                let Some(key_id) = numeric_i64(&Value::Number(key)) else {
                    continue;
                };
                if !own_pet_ids.contains(&key_id) {
                    continue;
                }
                let Ok(pet) = crate::luajit::read_table(reader, address) else {
                    continue;
                };
                if object_id(reader, &pet, "m_PetId").ok() == Some(key_id) {
                    let Ok(speed) = property_sp(reader, &pet) else {
                        continue;
                    };
                    insert_unique(&mut pet_speeds, key_id, speed)?;
                    let path = format!("PetMgr.Ins.m_Pets[{key_id}]");
                    chain.extend([
                        ("PetMgr.Ins".to_owned(), pets_instance.address),
                        ("PetMgr.Ins.m_Pets".to_owned(), pets.address),
                        (path.clone(), pet.address),
                    ]);
                    property_refs(reader, &pet, &path, &mut chain);
                }
            }
        }
    }

    if let Ok(partners_instance) = field_table(reader, &class_ins, "PartnerMgr.Ins") {
        if let Ok(partners) = field_table(reader, &partners_instance, "m_PartnerList") {
            for value in indexed_values(reader, &partners)? {
                let Some(address) = tagged_pointer_value(&value, LJ_TTAB) else {
                    continue;
                };
                let partner = crate::luajit::read_table(reader, address)?;
                let id = match fields(reader, &partner)?.get("id") {
                    Some(v) => scalar(reader, v)?,
                    None => continue,
                };
                if id == player_id || !own_main_ids.contains(&id) {
                    continue;
                }
                let props = fields(reader, &partner)?
                    .get("FiedPro")
                    .and_then(|v| tagged_pointer_value(v, LJ_TTAB));
                let Some(props) = props else {
                    continue;
                };
                let props = crate::luajit::read_table(reader, props)?;
                let mut total = 0i64;
                let mut found = false;
                for prop in indexed_values(reader, &props)? {
                    let Some(prop_address) = tagged_pointer_value(&prop, LJ_TTAB) else {
                        continue;
                    };
                    let prop = crate::luajit::read_table(reader, prop_address)?;
                    let f = fields(reader, &prop)?;
                    if f.get("type")
                        .is_some_and(|v| scalar(reader, v).ok() == Some(52))
                    {
                        total = add_partner_speed(
                            total,
                            f.get("value")
                                .ok_or_else(|| ProbeError::read("partner type 52 missing value"))
                                .and_then(|v| scalar(reader, v))?,
                        )?;
                        found = true;
                    }
                }
                if found {
                    insert_unique(&mut partner_speeds, id, total)?;
                    let path = format!("PartnerMgr.Ins.m_PartnerList[{id}]");
                    chain.extend([
                        ("PartnerMgr.Ins".to_owned(), partners_instance.address),
                        ("PartnerMgr.Ins.m_PartnerList".to_owned(), partners.address),
                        (path.clone(), partner.address),
                        (format!("{path}.FiedPro"), props.address),
                    ]);
                    for (prop_address, part) in type52_values(reader, &props)? {
                        chain.push((format!("{path}.FiedPro.type52"), prop_address));
                        partner_speed_parts.push((id, part));
                    }
                }
            }
        }
    }
    partner_speed_parts.sort_unstable();
    chain.sort_by(|a, b| a.0.cmp(&b.0).then(a.1.cmp(&b.1)));
    Ok(AttributeWitness {
        root_chain: chain,
        player_speeds,
        pet_speeds,
        partner_speeds,
        partner_speed_parts,
    })
}

fn property_refs(
    reader: &mut MemoryReader,
    owner: &GcTable,
    path: &str,
    chain: &mut Vec<(String, u64)>,
) {
    if let Ok(dict) = field_table(reader, owner, "PropertyValueDict") {
        chain.push((format!("{path}.PropertyValueDict"), dict.address));
        if let Ok(values) = fields(reader, &dict) {
            if let Some(value) = values.get("SP") {
                if let Some(address) = tagged_pointer_value(value, LJ_TTAB) {
                    chain.push((format!("{path}.PropertyValueDict.SP"), address));
                }
            }
        }
    }
}

fn type52_values(
    reader: &mut MemoryReader,
    properties: &GcTable,
) -> Result<Vec<(u64, i64)>, ProbeError> {
    let mut refs = Vec::new();
    for value in indexed_values(reader, properties)? {
        let Some(address) = tagged_pointer_value(&value, LJ_TTAB) else {
            continue;
        };
        let table = crate::luajit::read_table(reader, address)?;
        let f = fields(reader, &table)?;
        if f.get("type")
            .is_some_and(|v| scalar(reader, v).ok() == Some(52))
        {
            let value = f
                .get("value")
                .ok_or_else(|| ProbeError::read("partner type 52 missing value"))
                .and_then(|v| scalar(reader, v))?;
            refs.push((address, value));
        }
    }
    refs.sort_by_key(|item| item.0);
    Ok(refs)
}

pub fn read_attribute_speeds(
    reader: &mut MemoryReader,
    local_userdata: u64,
    player_id: i64,
    own_main_ids: &HashSet<i64>,
    own_pet_ids: &HashSet<i64>,
) -> Result<AttributeWitness, ProbeError> {
    read_cache(reader, local_userdata, player_id, own_main_ids, own_pet_ids)
}

#[cfg(test)]
mod tests {
    use super::{add_partner_speed, insert_unique, read_cache};
    use crate::memory::MemoryReader;
    use crate::model::MemoryRange;
    use std::collections::HashMap;
    use std::collections::HashSet;
    use std::fs;

    const TAG_TABLE: u32 = 0xfffffff4;
    const TAG_STRING: u32 = 0xfffffffb;
    const TAG_FUNCTION: u32 = 0xfffffff7;
    const TAG_NUMBER: i64 = 0xfffffff2u32 as i32 as i64;

    struct Fixture {
        bytes: Vec<u8>,
        next: usize,
    }
    impl Fixture {
        fn new() -> Self {
            Self {
                bytes: vec![0; 0x10000],
                next: 0x1000,
            }
        }
        fn alloc(&mut self, size: usize) -> u64 {
            let at = (self.next + 7) & !7;
            self.next = at + size;
            at as u64
        }
        fn string(&mut self, value: &str) -> u64 {
            let at = self.alloc(24 + value.len()) as usize;
            self.bytes[at + 9] = 4;
            self.bytes[at + 20..at + 24].copy_from_slice(&(value.len() as u32).to_le_bytes());
            self.bytes[at + 24..at + 24 + value.len()].copy_from_slice(value.as_bytes());
            at as u64
        }
        fn table(&mut self, entries: &[(&str, u64)]) -> u64 {
            let keyed: Vec<(u64, u64)> = entries
                .iter()
                .map(|(key, value)| (tagged(self.string(key), TAG_STRING), *value))
                .collect();
            self.table_raw(&keyed)
        }
        fn table_raw(&mut self, entries: &[(u64, u64)]) -> u64 {
            let count = entries.len().max(1).next_power_of_two();
            let node = self.alloc(count * 24);
            let address = self.alloc(64);
            let h = address as usize;
            self.bytes[h + 9] = 11;
            self.bytes[h + 40..h + 48].copy_from_slice(&node.to_le_bytes());
            self.bytes[h + 52..h + 56].copy_from_slice(&((count - 1) as u32).to_le_bytes());
            for (i, (key, value)) in entries.iter().enumerate() {
                let n = node as usize + i * 24;
                self.bytes[n..n + 8].copy_from_slice(&value.to_le_bytes());
                self.bytes[n + 8..n + 16].copy_from_slice(&key.to_le_bytes());
            }
            address
        }
        fn array(&mut self, values: &[u64]) -> u64 {
            let array = self.alloc(values.len().max(1) * 8);
            let node = self.alloc(24);
            let table = self.alloc(64);
            let h = table as usize;
            self.bytes[h + 9] = 11;
            self.bytes[h + 16..h + 24].copy_from_slice(&array.to_le_bytes());
            self.bytes[h + 40..h + 48].copy_from_slice(&node.to_le_bytes());
            self.bytes[h + 48..h + 52].copy_from_slice(&(values.len() as u32).to_le_bytes());
            self.bytes[h + 52..h + 56].copy_from_slice(&0u32.to_le_bytes());
            for (index, value) in values.iter().enumerate() {
                let at = array as usize + index * 8;
                self.bytes[at..at + 8].copy_from_slice(&value.to_le_bytes());
            }
            table
        }
        fn integer(value: i32) -> u64 {
            ((TAG_NUMBER << 47) as u64) | value as u32 as u64
        }
        fn wrapper(&mut self, value: i32) -> u64 {
            let array = self.alloc(24);
            let node = self.alloc(24);
            let table = self.alloc(64);
            let h = table as usize;
            self.bytes[h + 9] = 11;
            self.bytes[h + 16..h + 24].copy_from_slice(&array.to_le_bytes());
            self.bytes[h + 40..h + 48].copy_from_slice(&node.to_le_bytes());
            self.bytes[h + 48..h + 52].copy_from_slice(&3u32.to_le_bytes());
            self.bytes[h + 52..h + 56].copy_from_slice(&0u32.to_le_bytes());
            let a = array as usize;
            self.bytes[a..a + 8].copy_from_slice(&0xffff800000000000u64.to_le_bytes());
            self.bytes[a + 8..a + 16].copy_from_slice(&Self::integer(value).to_le_bytes());
            self.bytes[a + 16..a + 24].copy_from_slice(&0xffff800000000000u64.to_le_bytes());
            table
        }
    }
    fn tagged(address: u64, tag: u32) -> u64 {
        ((tag as u64) << 47) | address
    }

    #[test]
    fn real_table_parser_follows_exact_localplayer_ins_key_and_sp_wrapper() {
        let mut f = Fixture::new();
        let wrapped = f.wrapper(84);
        let properties = f.table(&[("SP", tagged(wrapped, TAG_TABLE))]);
        let player = f.table(&[
            ("m_PlayerId", Fixture::integer(901190)),
            ("PropertyValueDict", tagged(properties, TAG_TABLE)),
        ]);
        let local = f.table(&[("m_LocalPlayer", tagged(player, TAG_TABLE))]);
        let pet_value = f.wrapper(609);
        let pet_props = f.table(&[("SP", tagged(pet_value, TAG_TABLE))]);
        let pet = f.table(&[
            ("m_PetId", Fixture::integer(610000381)),
            ("PropertyValueDict", tagged(pet_props, TAG_TABLE)),
        ]);
        let mismatched_pet = f.table(&[
            ("m_PetId", Fixture::integer(999)),
            ("PropertyValueDict", tagged(pet_props, TAG_TABLE)),
        ]);
        let invalid_pet_props = f.table(&[("HP", Fixture::integer(50))]);
        let invalid_pet = f.table(&[
            ("m_PetId", Fixture::integer(610000383)),
            ("PropertyValueDict", tagged(invalid_pet_props, TAG_TABLE)),
        ]);
        let pets = f.table_raw(&[
            (Fixture::integer(610000381), tagged(pet, TAG_TABLE)),
            (
                Fixture::integer(610000382),
                tagged(mismatched_pet, TAG_TABLE),
            ),
            (Fixture::integer(610000383), tagged(invalid_pet, TAG_TABLE)),
        ]);
        let pet_mgr = f.table(&[("m_Pets", tagged(pets, TAG_TABLE))]);
        let partner_prop = f.table(&[
            ("type", Fixture::integer(52)),
            ("value", Fixture::integer(457)),
        ]);
        let partner_other = f.table(&[
            ("type", Fixture::integer(53)),
            ("value", Fixture::integer(99)),
        ]);
        let partner_properties = f.array(&[
            tagged(partner_prop, TAG_TABLE),
            tagged(partner_other, TAG_TABLE),
        ]);
        let partner = f.table(&[
            ("id", Fixture::integer(210002549)),
            ("FiedPro", tagged(partner_properties, TAG_TABLE)),
        ]);
        let enemy_partner = f.table(&[
            ("id", Fixture::integer(903625)),
            ("FiedPro", tagged(partner_properties, TAG_TABLE)),
        ]);
        let partner_list = f.array(&[tagged(partner, TAG_TABLE), tagged(enemy_partner, TAG_TABLE)]);
        let partner_mgr = f.table(&[("m_PartnerList", tagged(partner_list, TAG_TABLE))]);
        let classins = f.table(&[
            ("LocalPlayerMgr.Ins", tagged(local, TAG_TABLE)),
            ("PetMgr.Ins", tagged(pet_mgr, TAG_TABLE)),
            ("PartnerMgr.Ins", tagged(partner_mgr, TAG_TABLE)),
        ]);
        let globals = f.table(&[("ClassIns", tagged(classins, TAG_TABLE))]);
        let role_env = f.table(&[("initData", tagged(0x7000, TAG_FUNCTION))]);
        let closure = 0x7000usize;
        f.bytes[closure + 9] = 8;
        f.bytes[closure + 10] = 0;
        f.bytes[closure + 16..closure + 24].copy_from_slice(&globals.to_le_bytes());
        let userdata = 0x7100usize;
        f.bytes[userdata + 9] = 12;
        f.bytes[userdata + 16..userdata + 24].copy_from_slice(&(role_env.to_le_bytes()));
        let path =
            std::env::temp_dir().join(format!("sp-attributes-fixture-{}.bin", std::process::id()));
        fs::write(&path, &f.bytes).unwrap();
        let mut reader = MemoryReader::from_fixture(
            &path,
            vec![MemoryRange {
                start: 0x1000,
                end: 0x10000,
            }],
        );
        let witness = read_cache(
            &mut reader,
            userdata as u64,
            901190,
            &HashSet::from([901190, 210002549]),
            &HashSet::from([610000381, 610000382, 610000383]),
        )
        .unwrap();
        assert_eq!(witness.player_speeds.get(&901190), Some(&84));
        assert_eq!(witness.pet_speeds.get(&610000381), Some(&609));
        assert!(!witness.pet_speeds.contains_key(&610000382));
        assert!(!witness.pet_speeds.contains_key(&610000383));
        assert_eq!(witness.partner_speeds.get(&210002549), Some(&457));
        assert!(!witness.partner_speeds.contains_key(&903625));
        assert!(witness
            .root_chain
            .iter()
            .any(|(key, address)| key == "LocalPlayerMgr.Ins" && *address == local));
        drop(reader);
        fs::remove_file(path).unwrap();
    }
    #[test]
    fn duplicate_exact_ids_are_rejected() {
        let mut m = HashMap::new();
        insert_unique(&mut m, 7, 12).unwrap();
        assert!(insert_unique(&mut m, 7, 13).is_err());
    }
    #[test]
    fn partner_speed_sum_rejects_overflow() {
        assert!(add_partner_speed(i64::MAX, 1).is_err());
        assert_eq!(add_partner_speed(200, 52).unwrap(), 252);
    }
}
