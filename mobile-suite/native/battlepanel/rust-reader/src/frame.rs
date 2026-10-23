use crate::model::{Frame, Status};
use std::io::{self, Write};
use std::time::{SystemTime, UNIX_EPOCH};

const HEADER_SIZE: usize = 40;
const MAX_PAYLOAD_LEN: usize = 1024;
const SLOT_COUNT: usize = 20;
const SLOT_SIZE: usize = 32;
pub const PAYLOAD_LEN: usize = HEADER_SIZE + SLOT_COUNT * SLOT_SIZE;

pub fn write_frame(output: &mut impl Write, frame: &Frame) -> io::Result<()> {
    if frame.slots.len() != SLOT_COUNT || PAYLOAD_LEN > MAX_PAYLOAD_LEN {
        return Err(io::Error::new(
            io::ErrorKind::InvalidData,
            "frame slot count or payload length is invalid",
        ));
    }
    if frame.status == Status::Live && !has_expected_positions(&frame.slots) {
        return Err(io::Error::new(
            io::ErrorKind::InvalidData,
            "live frame positions do not contain both validated formations",
        ));
    }

    // EBP2 payload offsets and big-endian integer encoding follow PROTOCOL.md.
    let mut payload = [0u8; PAYLOAD_LEN];
    payload[0..4].copy_from_slice(b"EBP2");
    payload[4] = frame.status as u8;
    payload[5] = SLOT_COUNT as u8;
    payload[8..12].copy_from_slice(&frame.pid.to_be_bytes());
    payload[12..20].copy_from_slice(&frame.start_ticks.to_be_bytes());
    payload[20..28].copy_from_slice(&frame.battle_id.to_be_bytes());
    payload[28..32].copy_from_slice(&frame.round.to_be_bytes());
    let timestamp = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_millis()
        .min(u64::MAX as u128) as u64;
    payload[32..40].copy_from_slice(&timestamp.to_be_bytes());
    for (index, slot) in frame.slots.iter().enumerate() {
        let offset = HEADER_SIZE + index * SLOT_SIZE;
        payload[offset..offset + 4].copy_from_slice(&slot.position.to_be_bytes());
        payload[offset + 4] = slot.state;
        payload[offset + 8..offset + 16].copy_from_slice(&slot.hp.to_be_bytes());
        payload[offset + 16..offset + 24].copy_from_slice(&slot.mp.to_be_bytes());
        payload[offset + 24..offset + 32].copy_from_slice(&slot.sp.to_be_bytes());
    }
    let reserved_bytes_are_zero = payload[6..8].iter().all(|value| *value == 0)
        && (0..SLOT_COUNT).all(|index| {
            let offset = HEADER_SIZE + index * SLOT_SIZE;
            payload[offset + 5..offset + 8]
                .iter()
                .all(|value| *value == 0)
        });
    if !reserved_bytes_are_zero {
        return Err(io::Error::new(
            io::ErrorKind::InvalidData,
            "encoded frame reserved bytes are nonzero",
        ));
    }
    if payload.len() != PAYLOAD_LEN || payload.len() > MAX_PAYLOAD_LEN {
        return Err(io::Error::new(
            io::ErrorKind::InvalidData,
            "encoded frame payload length is invalid",
        ));
    }
    output.write_all(&(payload.len() as u32).to_be_bytes())?;
    output.write_all(&payload)?;
    output.flush()
}

fn has_expected_positions(slots: &[crate::model::Slot; SLOT_COUNT]) -> bool {
    let (own_main, opposing_main) = match slots[0].position {
        1 => (1, 10_001),
        10_001 => (10_001, 1),
        _ => return false,
    };
    for index in 0..5 {
        if slots[index].position != own_main + index as u32
            || slots[index + 5].position != own_main + 100 + index as u32
            || slots[index + 10].position != opposing_main + index as u32
            || slots[index + 15].position != opposing_main + 100 + index as u32
        {
            return false;
        }
    }
    true
}

pub fn error_status(kind: crate::model::ErrorKind) -> Status {
    match kind {
        crate::model::ErrorKind::RootDenied => Status::RootDenied,
        crate::model::ErrorKind::Expired => Status::Expired,
        crate::model::ErrorKind::ReadError => Status::ReadError,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn slots_for(own_main: u32, opposing_main: u32) -> [crate::model::Slot; SLOT_COUNT] {
        let mut slots = [crate::model::Slot::default(); SLOT_COUNT];
        for index in 0..5 {
            slots[index].position = own_main + index as u32;
            slots[index + 5].position = own_main + 100 + index as u32;
            slots[index + 10].position = opposing_main + index as u32;
            slots[index + 15].position = opposing_main + 100 + index as u32;
        }
        slots
    }

    #[test]
    fn writes_680_byte_ebp2_frames_and_rejects_mixed_position_families() {
        assert_eq!(PAYLOAD_LEN, 680);
        assert!(PAYLOAD_LEN <= MAX_PAYLOAD_LEN);
        assert!(has_expected_positions(&slots_for(1, 10_001)));
        assert!(has_expected_positions(&slots_for(10_001, 1)));

        let mut locating = Frame::empty(Status::Locating, None);
        let mut bytes = Vec::new();
        write_frame(&mut bytes, &locating).unwrap();
        assert_eq!(
            u32::from_be_bytes(bytes[0..4].try_into().unwrap()) as usize,
            PAYLOAD_LEN
        );
        assert_eq!(bytes.len(), PAYLOAD_LEN + 4);
        assert_eq!(&bytes[4..8], b"EBP2");
        assert_eq!(bytes[9], SLOT_COUNT as u8);
        assert_eq!(&bytes[10..12], &[0, 0]);
        assert_eq!(&bytes[49..52], &[0, 0, 0]);

        locating.status = Status::Live;
        locating.slots = slots_for(1, 10_001);
        locating.slots[10].position = 1;
        assert_eq!(
            write_frame(&mut Vec::new(), &locating).unwrap_err().kind(),
            io::ErrorKind::InvalidData
        );
    }
}
