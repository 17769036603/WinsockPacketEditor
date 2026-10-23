use crate::attributes::{read_attribute_speeds, AttributeWitness};
use crate::luajit::{
    field_map, numeric_i64, read_gcstr, read_table, read_table_array, read_table_nodes,
    tagged_pointer_value, unwrap_value, TableKey, Value, LJ_TSTR, LJ_TTAB, LJ_TUDATA,
};
use crate::memory::MemoryReader;
use crate::model::{ProbeError, Slot};
use std::collections::{HashMap, HashSet};

const MANAGER_FIELDS: [&str; 6] = [
    "m_BattleId",
    "m_CurRoundNum",
    "m_PlayerId",
    "m_RoleIns",
    "m_SpareRoleIns",
    "m_RoleIdPosDict",
];
const ROLE_ID_FIELD: &str = "m_RoleId";
const LOCAL_FIELD: &str = "m_IsLocalPlayer";
const ALIVE_FIELD: &str = "m_IsAlive";
const HP_FIELD: &str = "m_Hp";
const MP_FIELD: &str = "m_Mp";
const SPEED_FIELD: &str = "m_Speed";
const POSITION_FIELD: &str = "m_Pos";
const BATTLEFIELD_FIELD: &str = "m_Battlefield";
const ROLE_IDENTITY_FIELDS: [&str; 5] = [
    ROLE_ID_FIELD,
    LOCAL_FIELD,
    ALIVE_FIELD,
    POSITION_FIELD,
    BATTLEFIELD_FIELD,
];

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct ManagerValues {
    pub battle_id: u64,
    pub round: u32,
    pub player_id: i64,
    pub role_table: u64,
    pub position_table: u64,
}

#[derive(Clone, Debug)]
struct RoleSnapshot {
    role_id: i64,
    position: i64,
    local: bool,
    alive: bool,
    hp: i64,
    mp: i64,
    speed: Option<i64>,
}

pub fn read_manager_values(
    reader: &mut MemoryReader,
    manager_address: u64,
) -> Result<ManagerValues, ProbeError> {
    let fields = field_map(reader, manager_address)?;
    if MANAGER_FIELDS
        .iter()
        .any(|field| !fields.contains_key(*field))
    {
        return Err(ProbeError::read(
            "validated manager no longer contains all required fields",
        ));
    }
    let battle_id = read_battle_id_field(reader, &fields["m_BattleId"])?;
    let round_value = unwrap_required(reader, &fields["m_CurRoundNum"], "m_CurRoundNum")?;
    let round = value_u64(&round_value)
        .filter(|value| *value <= u32::MAX as u64)
        .ok_or_else(|| ProbeError::read("manager.m_CurRoundNum is outside u32 range"))?
        as u32;
    let player_value = unwrap_required(reader, &fields["m_PlayerId"], "m_PlayerId")?;
    let player_id = numeric_i64(&player_value)
        .filter(|value| *value > 0)
        .ok_or_else(|| ProbeError::read("manager.m_PlayerId is not a positive integer"))?;
    let role_table = tagged_pointer_value(&fields["m_RoleIns"], LJ_TTAB)
        .ok_or_else(|| ProbeError::read("manager.m_RoleIns is not a LuaJIT table"))?;
    let position_table = tagged_pointer_value(&fields["m_RoleIdPosDict"], LJ_TTAB)
        .ok_or_else(|| ProbeError::read("manager.m_RoleIdPosDict is not a LuaJIT table"))?;
    read_table(reader, role_table)?;
    read_table(reader, position_table)?;
    Ok(ManagerValues {
        battle_id,
        round,
        player_id,
        role_table,
        position_table,
    })
}

pub fn read_manager_battle_id(
    reader: &mut MemoryReader,
    manager_address: u64,
) -> Result<u64, ProbeError> {
    let fields = field_map(reader, manager_address)?;
    let value = fields
        .get("m_BattleId")
        .ok_or_else(|| ProbeError::read("validated manager no longer contains m_BattleId"))?;
    read_battle_id_field(reader, value)
}

fn read_battle_id_field(reader: &mut MemoryReader, field: &Value) -> Result<u64, ProbeError> {
    match unwrap_value(reader, field, 0)? {
        Some(value) => value_u64(&value),
        None => parse_battle_id_string(reader, field)?,
    }
    .ok_or_else(|| ProbeError::read("manager.m_BattleId is not an unsigned integer"))
}

pub fn manager_ended(manager: &ManagerValues) -> bool {
    manager.battle_id == 0
}
fn value_u64(value: &Value) -> Option<u64> {
    match value {
        Value::Integer(number) if *number >= 0 => Some(*number as u64),
        Value::Number(number)
            if number.is_finite()
                && number.fract() == 0.0
                && *number >= 0.0
                && *number < 18_446_744_073_709_551_616.0 =>
        {
            Some(*number as u64)
        }
        _ => None,
    }
}

fn unwrap_required(
    reader: &mut MemoryReader,
    value: &Value,
    field: &str,
) -> Result<Value, ProbeError> {
    unwrap_value(reader, value, 0)?
        .ok_or_else(|| ProbeError::read(format!("{field} does not contain a scalar TValue")))
}

pub fn read_role_position_map(
    reader: &mut MemoryReader,
    position_table_address: u64,
) -> Result<HashMap<i64, i64>, ProbeError> {
    let table = read_table(reader, position_table_address)?;
    let mut by_role = HashMap::new();
    let mut by_position = HashMap::new();
    for node in read_table_nodes(reader, &table)? {
        if matches!(&node.value, Value::Nil) {
            continue;
        }
        let role_id = table_key_number(node.key.as_ref())
            .filter(|value| *value > 0)
            .ok_or_else(|| ProbeError::read("position dictionary contains an invalid role ID"))?;
        let position_value = unwrap_value(reader, &node.value, 0)?
            .ok_or_else(|| ProbeError::read("position dictionary value is not numeric"))?;
        let position = numeric_i64(&position_value)
            .filter(|value| *value > 0)
            .ok_or_else(|| ProbeError::read("position dictionary contains an invalid position"))?;
        if let Some(previous) = by_role.insert(role_id, position) {
            if previous != position {
                return Err(ProbeError::read(
                    "one role ID maps to more than one raw position",
                ));
            }
        }
        if let Some(previous) = by_position.insert(position, role_id) {
            if previous != role_id {
                return Err(ProbeError::read(
                    "multiple role IDs map to the same raw position",
                ));
            }
        }
    }
    Ok(by_role)
}

fn table_key_number(key: Option<&TableKey>) -> Option<i64> {
    crate::luajit::table_key_number(key)
}

fn userdata_environment(reader: &mut MemoryReader, userdata: u64) -> Result<u64, ProbeError> {
    if !reader.contains(userdata, 24) {
        return Err(ProbeError::read(
            "BattleRole userdata header is outside validated maps",
        ));
    }
    let header = reader.read(userdata, 24)?;
    if header[9] != 12 {
        return Err(ProbeError::read(
            "m_RoleIns entry is not a LuaJIT userdata object",
        ));
    }
    let environment = u64::from_le_bytes(
        header[16..24]
            .try_into()
            .map_err(|_| ProbeError::read("userdata environment field is truncated"))?,
    );
    read_table(reader, environment)?;
    Ok(environment)
}

fn userdata_index(
    reader: &mut MemoryReader,
    role_table_address: u64,
) -> Result<HashMap<i64, u64>, ProbeError> {
    let table = read_table(reader, role_table_address)?;
    let mut by_position = HashMap::new();
    for (position, value) in read_table_array(reader, &table)?.iter().enumerate() {
        if let Some(userdata) = tagged_pointer_value(value, LJ_TUDATA) {
            by_position.insert(position as i64, userdata);
        }
    }
    for node in read_table_nodes(reader, &table)? {
        let Some(position) = table_key_number(node.key.as_ref()) else {
            continue;
        };
        if position <= 0 {
            continue;
        }
        let Some(userdata) = tagged_pointer_value(&node.value, LJ_TUDATA) else {
            continue;
        };
        if let Some(previous) = by_position.insert(position, userdata) {
            if previous != userdata {
                return Err(ProbeError::read(
                    "m_RoleIns contains duplicate userdata for one position",
                ));
            }
        }
    }
    Ok(by_position)
}

fn validate_role_identity_fields(
    fields: &HashMap<String, Value>,
    expected_position: i64,
    expected_role_id: i64,
) -> Result<(), ProbeError> {
    let missing: Vec<&str> = ROLE_IDENTITY_FIELDS
        .iter()
        .copied()
        .filter(|field| !fields.contains_key(*field))
        .collect();
    if missing.is_empty() {
        Ok(())
    } else {
        Err(ProbeError::read(format!(
            "mapped BattleRole at raw position {expected_position} (expected role_id {expected_role_id}) is missing identity fields: {}",
            missing.join(", ")
        )))
    }
}

fn local_marker_matches_player(role_id: i64, local_id: i64, local: bool) -> bool {
    local == (role_id == local_id)
}

#[derive(Debug)]
enum RoleReadError {
    Probe(ProbeError),
    IdentityMismatch,
}

fn tolerate_identity_mismatch(allow_opposing: bool, own_side: bool) -> bool {
    allow_opposing && !own_side
}

impl From<ProbeError> for RoleReadError {
    fn from(error: ProbeError) -> Self {
        Self::Probe(error)
    }
}

fn read_role(
    reader: &mut MemoryReader,
    userdata: u64,
    expected_position: i64,
    expected_role_id: i64,
    local_id: i64,
    manager_address: u64,
) -> Result<RoleSnapshot, RoleReadError> {
    let environment = userdata_environment(reader, userdata)?;
    let fields = field_map(reader, environment)?;
    validate_role_identity_fields(&fields, expected_position, expected_role_id)?;
    let role_value = unwrap_required(reader, &fields[ROLE_ID_FIELD], ROLE_ID_FIELD)?;
    let role_id = numeric_i64(&role_value)
        .ok_or_else(|| ProbeError::read("BattleRole.m_RoleId is not numeric"))?;
    let local_value = unwrap_required(reader, &fields[LOCAL_FIELD], LOCAL_FIELD)?;
    let Value::Boolean(local) = local_value else {
        return Err(ProbeError::read("BattleRole.m_IsLocalPlayer is not boolean").into());
    };
    let alive_value = unwrap_required(reader, &fields[ALIVE_FIELD], ALIVE_FIELD)?;
    let Value::Boolean(alive) = alive_value else {
        return Err(ProbeError::read("BattleRole.m_IsAlive is not boolean").into());
    };
    let position_value = unwrap_required(reader, &fields[POSITION_FIELD], POSITION_FIELD)?;
    let position = numeric_i64(&position_value)
        .ok_or_else(|| ProbeError::read("BattleRole.m_Pos is not numeric"))?;
    let battlefield = tagged_pointer_value(&fields[BATTLEFIELD_FIELD], LJ_TTAB)
        .ok_or_else(|| ProbeError::read("BattleRole.m_Battlefield is not a LuaJIT table"))?;
    if role_id != expected_role_id
        || position != expected_position
        || battlefield != manager_address
        || !local_marker_matches_player(role_id, local_id, local)
    {
        return Err(RoleReadError::IdentityMismatch);
    }

    let hp = read_role_resource(reader, &fields, HP_FIELD, position, role_id, alive)?;
    let mp = read_role_resource(reader, &fields, MP_FIELD, position, role_id, alive)?;
    let speed = role_speed_if_alive(alive, || read_optional_role_speed(reader, &fields))?;
    Ok(RoleSnapshot {
        role_id,
        position,
        local,
        alive,
        hp,
        mp,
        speed,
    })
}

fn read_role_resource(
    reader: &mut MemoryReader,
    fields: &HashMap<String, Value>,
    field: &str,
    raw_position: i64,
    role_id: i64,
    alive: bool,
) -> Result<i64, ProbeError> {
    let value = fields.get(field).ok_or_else(|| {
        ProbeError::read(format!(
            "BattleRole is missing {field} at raw position {raw_position} (roleId {role_id}, alive {alive})"
        ))
    })?;
    let value = unwrap_required(reader, value, field)?;
    numeric_i64(&value)
        .ok_or_else(|| ProbeError::read(format!("BattleRole.{field} is not an i64 integer")))
}

fn read_optional_role_speed(
    reader: &mut MemoryReader,
    fields: &HashMap<String, Value>,
) -> Result<Option<i64>, ProbeError> {
    let Some(value) = fields.get(SPEED_FIELD) else {
        return optional_role_speed_value(None);
    };
    let value = unwrap_required(reader, value, SPEED_FIELD)?;
    optional_role_speed_value(Some(value))
}

fn optional_role_speed_value(value: Option<Value>) -> Result<Option<i64>, ProbeError> {
    let Some(value) = value else {
        return Ok(None);
    };
    numeric_i64(&value)
        .map(Some)
        .ok_or_else(|| ProbeError::read("BattleRole.m_Speed is not an i64 integer"))
}

fn role_speed_if_alive(
    alive: bool,
    read_speed: impl FnOnce() -> Result<Option<i64>, ProbeError>,
) -> Result<Option<i64>, ProbeError> {
    if alive {
        read_speed()
    } else {
        Ok(None)
    }
}

pub fn read_slots(
    reader: &mut MemoryReader,
    manager_address: u64,
    manager: &ManagerValues,
    position_by_role: &HashMap<i64, i64>,
) -> Result<[Slot; 20], ProbeError> {
    read_slots_with_identity_tolerance(reader, manager_address, manager, position_by_role, false)
}

pub fn read_slots_partial_with_attributes(
    reader: &mut MemoryReader,
    manager_address: u64,
    manager: &ManagerValues,
    position_by_role: &HashMap<i64, i64>,
) -> Result<([Slot; 20], Option<AttributeWitness>), ProbeError> {
    let mut slots = read_slots_with_identity_tolerance(
        reader,
        manager_address,
        manager,
        position_by_role,
        true,
    )?;
    let missing_own = slots[..10].iter().any(|slot| slot.state == 4);
    if !missing_own {
        return Ok((slots, None));
    }
    let local_position = *position_by_role
        .get(&manager.player_id)
        .ok_or_else(|| ProbeError::read("local player position disappeared"))?;
    let own_main_ids: HashSet<i64> = position_by_role
        .iter()
        .filter_map(|(id, pos)| {
            ((1..=5).contains(&local_position) && (1..=5).contains(pos)
                || (10001..=10005).contains(&local_position) && (10001..=10005).contains(pos))
            .then_some(*id)
        })
        .collect();
    let own_pet_ids: HashSet<i64> = position_by_role
        .iter()
        .filter_map(|(id, pos)| {
            ((1..=5).contains(&local_position) && (101..=105).contains(pos)
                || (10001..=10005).contains(&local_position) && (10101..=10105).contains(pos))
            .then_some(*id)
        })
        .collect();
    let data = userdata_index(reader, manager.role_table)?;
    let local_userdata = *data
        .get(&local_position)
        .ok_or_else(|| ProbeError::read("local role userdata disappeared"))?;
    let witness = match read_attribute_speeds(
        reader,
        local_userdata,
        manager.player_id,
        &own_main_ids,
        &own_pet_ids,
    ) {
        Ok(value) => value,
        Err(_) => return Ok((slots, None)),
    };
    for slot in &mut slots[..10] {
        if slot.state != 4 {
            continue;
        }
        let Some(role_id) = position_by_role
            .iter()
            .find_map(|(id, pos)| (*pos == slot.position as i64).then_some(*id))
        else {
            continue;
        };
        let speed = cached_speed_for_slot(
            &witness,
            role_id,
            slot.position as i64,
            manager.player_id,
            local_position,
        );
        if let Some(speed) = speed {
            slot.state = 2;
            slot.sp = speed;
        }
    }
    Ok((slots, Some(witness)))
}

fn cached_speed_for_slot(
    witness: &AttributeWitness,
    role_id: i64,
    position: i64,
    player_id: i64,
    local_position: i64,
) -> Option<i64> {
    if ((1..=5).contains(&local_position) && (101..=105).contains(&position))
        || ((10001..=10005).contains(&local_position) && (10101..=10105).contains(&position))
    {
        witness.pet_speeds.get(&role_id).copied()
    } else if ((1..=5).contains(&local_position) && (1..=5).contains(&position))
        || ((10001..=10005).contains(&local_position) && (10001..=10005).contains(&position))
    {
        if role_id == player_id {
            witness.player_speeds.get(&role_id).copied()
        } else {
            witness.partner_speeds.get(&role_id).copied()
        }
    } else {
        None
    }
}

fn read_slots_with_identity_tolerance(
    reader: &mut MemoryReader,
    manager_address: u64,
    manager: &ManagerValues,
    position_by_role: &HashMap<i64, i64>,
    allow_opposing_identity_mismatch: bool,
) -> Result<[Slot; 20], ProbeError> {
    let mut local_positions: Vec<i64> = position_by_role
        .iter()
        .filter_map(|(role_id, position)| (*role_id == manager.player_id).then_some(*position))
        .collect();
    local_positions.sort_unstable();
    local_positions.dedup();
    if local_positions.len() != 1 {
        return Err(ProbeError::read(
            "local role does not have one unique raw position",
        ));
    }
    let local_position = local_positions[0];
    let (own_positions, opposing_positions): ([u32; 10], [u32; 10]) =
        if (1..=5).contains(&local_position) {
            (
                [1, 2, 3, 4, 5, 101, 102, 103, 104, 105],
                [
                    10001, 10002, 10003, 10004, 10005, 10101, 10102, 10103, 10104, 10105,
                ],
            )
        } else if (10001..=10005).contains(&local_position) {
            (
                [
                    10001, 10002, 10003, 10004, 10005, 10101, 10102, 10103, 10104, 10105,
                ],
                [1, 2, 3, 4, 5, 101, 102, 103, 104, 105],
            )
        } else {
            return Err(ProbeError::read(
                "local role is outside confirmed main-unit position families",
            ));
        };
    let userdata_by_position = userdata_index(reader, manager.role_table)?;
    let mut role_by_position = HashMap::new();
    for (role_id, position) in position_by_role {
        role_by_position.insert(*position, *role_id);
    }

    let local_userdata = userdata_by_position
        .get(&local_position)
        .ok_or_else(|| ProbeError::read("local role position has no userdata"))?;
    let local_role = read_role(
        reader,
        *local_userdata,
        local_position,
        manager.player_id,
        manager.player_id,
        manager_address,
    )
    .map_err(|error| match error {
        RoleReadError::Probe(error) => error,
        RoleReadError::IdentityMismatch => {
            ProbeError::read("BattleRole identity mismatch at local raw position")
        }
    })?;
    if !local_role.local
        || local_role.role_id != manager.player_id
        || local_role.position != local_position
    {
        return Err(ProbeError::read("local BattleRole validation failed"));
    }

    let mut slots = [Slot::default(); 20];
    for (side_index, positions) in [own_positions, opposing_positions].into_iter().enumerate() {
        for (index, position) in positions.into_iter().enumerate() {
            let slot_index = side_index * 10 + index;
            let Some(expected_role) = role_by_position.get(&(position as i64)).copied() else {
                slots[slot_index] = Slot {
                    position,
                    state: 1,
                    hp: 0,
                    mp: 0,
                    sp: 0,
                };
                continue;
            };
            let role = if position as i64 == local_position {
                local_role.clone()
            } else {
                let userdata = userdata_by_position
                    .get(&(position as i64))
                    .ok_or_else(|| {
                        ProbeError::read(format!(
                            "position dictionary maps role {expected_role} at {position} but no userdata exists"
                        ))
                    })?;
                match read_role(
                    reader,
                    *userdata,
                    position as i64,
                    expected_role,
                    manager.player_id,
                    manager_address,
                ) {
                    Ok(role) => role,
                    Err(RoleReadError::IdentityMismatch)
                        if tolerate_identity_mismatch(
                            allow_opposing_identity_mismatch,
                            side_index == 0,
                        ) =>
                    {
                        slots[slot_index] = Slot {
                            position,
                            state: 1,
                            ..Slot::default()
                        };
                        continue;
                    }
                    Err(RoleReadError::IdentityMismatch) => {
                        return Err(ProbeError::read(format!(
                            "BattleRole identity mismatch at raw position {position}"
                        )))
                    }
                    Err(RoleReadError::Probe(error)) => return Err(error),
                }
            };
            slots[slot_index] = if !role.alive {
                Slot {
                    position,
                    state: 3,
                    hp: 0,
                    mp: 0,
                    sp: 0,
                }
            } else if let Some(speed) = role.speed {
                Slot {
                    position,
                    state: 2,
                    hp: role.hp,
                    mp: role.mp,
                    sp: speed,
                }
            } else {
                // State 4 means HP/MP were validated but SP is unknown.
                Slot {
                    position,
                    state: 4,
                    hp: role.hp,
                    mp: role.mp,
                    sp: 0,
                }
            };
        }
    }
    Ok(slots)
}

#[cfg(test)]
mod tests {
    use super::{
        cached_speed_for_slot, local_marker_matches_player, manager_ended,
        optional_role_speed_value, role_speed_if_alive, tolerate_identity_mismatch,
        validate_role_identity_fields, ManagerValues,
    };
    use crate::attributes::AttributeWitness;
    use crate::luajit::Value;
    use std::collections::HashMap;

    #[test]
    fn missing_identity_fields_error_names_the_slot_role_and_fields() {
        let fields = HashMap::from([("m_RoleId".to_owned(), Value::Integer(42))]);
        let error = validate_role_identity_fields(&fields, 101, 42).unwrap_err();
        assert_eq!(
            error.message,
            "mapped BattleRole at raw position 101 (expected role_id 42) is missing identity fields: m_IsLocalPlayer, m_IsAlive, m_Pos, m_Battlefield"
        );
    }

    #[test]
    fn non_player_role_can_be_mapped_to_local_formation() {
        // m_IsLocalPlayer identifies the player avatar, not the formation side.
        assert!(local_marker_matches_player(77, 42, false));
    }

    #[test]
    fn identity_mismatch_tolerance_applies_only_to_opposing_slots() {
        assert!(tolerate_identity_mismatch(true, false));
        assert!(!tolerate_identity_mismatch(true, true));
        assert!(!tolerate_identity_mismatch(false, false));
    }

    #[test]
    fn only_zero_battle_id_is_ended() {
        let make_manager = |battle_id| ManagerValues {
            battle_id,
            round: 2,
            player_id: 1,
            role_table: 3,
            position_table: 4,
        };
        assert!(!manager_ended(&make_manager(9)));
        assert!(manager_ended(&make_manager(0)));
    }

    #[test]
    fn optional_speed_accepts_absence_and_integer_but_rejects_other_types() {
        assert_eq!(optional_role_speed_value(None).unwrap(), None);
        assert_eq!(
            optional_role_speed_value(Some(Value::Integer(73))).unwrap(),
            Some(73)
        );

        let error = optional_role_speed_value(Some(Value::Boolean(true))).unwrap_err();
        assert_eq!(error.message, "BattleRole.m_Speed is not an i64 integer");
    }

    #[test]
    fn cached_speed_requires_matching_own_slot_family() {
        let witness = AttributeWitness {
            root_chain: Vec::new(),
            player_speeds: HashMap::from([(42, 84)]),
            pet_speeds: HashMap::from([(77, 609)]),
            partner_speeds: HashMap::from([(88, 457)]),
            partner_speed_parts: Vec::new(),
        };
        assert_eq!(cached_speed_for_slot(&witness, 77, 103, 42, 3), Some(609));
        assert_eq!(cached_speed_for_slot(&witness, 77, 3, 42, 3), None);
        assert_eq!(cached_speed_for_slot(&witness, 88, 3, 42, 3), Some(457));
        assert_eq!(cached_speed_for_slot(&witness, 88, 10003, 42, 3), None);
        assert_eq!(
            cached_speed_for_slot(&witness, 88, 10003, 42, 10003),
            Some(457)
        );
    }

    #[test]
    fn dead_role_skips_speed_read_but_alive_role_rejects_malformed_speed() {
        let dead_speed = role_speed_if_alive(false, || {
            optional_role_speed_value(Some(Value::Boolean(true)))
        })
        .unwrap();
        assert_eq!(dead_speed, None);

        let error = role_speed_if_alive(true, || {
            optional_role_speed_value(Some(Value::Boolean(true)))
        })
        .unwrap_err();
        assert_eq!(error.message, "BattleRole.m_Speed is not an i64 integer");
    }
}

pub fn parse_battle_id_string(
    reader: &mut MemoryReader,
    value: &Value,
) -> Result<Option<u64>, ProbeError> {
    let Some(address) = tagged_pointer_value(value, LJ_TSTR) else {
        return Ok(None);
    };
    let text = read_gcstr(reader, address)?;
    if !text.is_ascii() || text.is_empty() || !text.bytes().all(|byte| byte.is_ascii_digit()) {
        return Ok(None);
    }
    Ok(text.parse::<u64>().ok())
}
