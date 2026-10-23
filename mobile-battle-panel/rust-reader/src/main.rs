mod attributes;
mod battle;
mod frame;
mod luajit;
mod manager_cache;
mod memory;
mod model;
mod procfs;

use crate::battle::{
    manager_ended, read_manager_battle_id, read_manager_values, read_role_position_map,
    read_slots_partial_with_attributes,
};
use crate::frame::{error_status, write_frame};
use crate::manager_cache::{load_candidate, store_candidate};
use crate::memory::MemoryReader;
use crate::model::{ErrorKind, Frame, ProbeError, ProcessIdentity, Status};
use crate::procfs::{
    find_process, identity_matches, read_anonymous_rw_private_maps, validate_package,
};
use std::io::{self, Write};
use std::path::PathBuf;
use std::thread;
use std::time::{Duration, Instant};

const DEFAULT_PACKAGE: &str = "com.gdoo.xxxy2026";
const POLL_INTERVAL: Duration = Duration::from_millis(1000);
const MAX_COLD_RETRY: Duration = Duration::from_secs(30);

#[derive(Clone, Debug)]
struct ManagerBinding {
    identity: ProcessIdentity,
    address: u64,
}

struct Config {
    package: String,
    cache_path: Option<PathBuf>,
}

fn main() {
    if let Err(error) = run() {
        eprintln!("enemy_reader stopped: {error}");
    }
}

fn run() -> Result<(), Box<dyn std::error::Error>> {
    let config = match parse_args() {
        Ok(config) => config,
        Err(error) => {
            eprintln!("{error}");
            return Ok(());
        }
    };
    validate_package(&config.package)?;
    let stdout = io::stdout();
    let mut output = io::BufWriter::new(stdout.lock());
    let mut previous_identity: Option<ProcessIdentity> = None;
    let mut binding: Option<ManagerBinding> = None;
    let mut cold_retry_at = Instant::now();
    let mut cold_retry_delay = Duration::from_secs(2);

    loop {
        match find_process(&config.package) {
            Ok(Some(identity)) => {
                if previous_identity.as_ref() != Some(&identity) {
                    if let Some(previous) = previous_identity.take() {
                        write_frame(&mut output, &Frame::empty(Status::Expired, Some(&previous)))?;
                    }
                    previous_identity = Some(identity.clone());
                    binding = None;
                    cold_retry_at = Instant::now();
                    cold_retry_delay = Duration::from_secs(2);
                }
                let persistent_candidate = config
                    .cache_path
                    .as_deref()
                    .and_then(|path| load_candidate(path, &config.package, &identity));
                let candidate = binding
                    .as_ref()
                    .filter(|value| value.identity == identity)
                    .map(|value| value.address)
                    .or(persistent_candidate);
                if binding.is_none() && Instant::now() < cold_retry_at {
                    write_frame(
                        &mut output,
                        &Frame::empty(Status::Locating, Some(&identity)),
                    )?;
                    thread::sleep(POLL_INTERVAL);
                    continue;
                }
                let mut validated_address = None;
                match sample_current(
                    &identity,
                    &config.package,
                    candidate,
                    &mut output,
                    &mut validated_address,
                ) {
                    Ok((frame, manager_address)) => {
                        binding = Some(ManagerBinding {
                            identity: identity.clone(),
                            address: manager_address,
                        });
                        cold_retry_delay = Duration::from_secs(2);
                        cold_retry_at = Instant::now();
                        if let Some(path) = config
                            .cache_path
                            .as_deref()
                            .filter(|_| persistent_candidate != Some(manager_address))
                        {
                            if let Err(error) =
                                store_candidate(path, &config.package, &identity, manager_address)
                            {
                                eprintln!("enemy_reader manager cache: {}", error.message);
                            }
                        }
                        write_frame(&mut output, &frame)?;
                    }
                    Err(error) => {
                        if let Some(address) = validated_address {
                            binding = Some(ManagerBinding {
                                identity: identity.clone(),
                                address,
                            });
                            cold_retry_delay = Duration::from_secs(2);
                            cold_retry_at = Instant::now();
                        } else if error.kind != ErrorKind::Expired {
                            cold_retry_at = Instant::now() + cold_retry_delay;
                            cold_retry_delay = (cold_retry_delay * 2).min(MAX_COLD_RETRY);
                        }
                        let frame = Frame::empty(error_status(error.kind), Some(&identity));
                        write_frame(&mut output, &frame)?;
                        let retry_code = if error.kind == ErrorKind::Expired {
                            "sample_changed"
                        } else if error.message.contains("matched 0 validated tables")
                            || error.message.contains("m_RoleIns was not found")
                        {
                            "manager_not_found"
                        } else if error.message.contains("matched ")
                            && error.message.contains("validated tables")
                        {
                            "manager_ambiguous"
                        } else if error
                            .message
                            .contains("active manager candidates without validated battle roles")
                        {
                            "battle_data_invalid"
                        } else if validated_address.is_some() {
                            "battle_data_invalid"
                        } else {
                            "read_error"
                        };
                        if error.kind != ErrorKind::RootDenied {
                            eprintln!("ENEMY_READER_STATUS|retry|0|0|{retry_code}");
                        }
                        eprintln!("enemy_reader sample: {}", error.message);
                    }
                }
            }
            Ok(None) => {
                if let Some(previous) = previous_identity.take() {
                    write_frame(&mut output, &Frame::empty(Status::Expired, Some(&previous)))?;
                }
                write_frame(&mut output, &Frame::empty(Status::NoGame, None))?;
            }
            Err(error) => {
                eprintln!("enemy_reader process lookup: {}", error.message);
                let frame = Frame::empty(error_status(error.kind), previous_identity.as_ref());
                write_frame(&mut output, &frame)?;
            }
        }
        thread::sleep(POLL_INTERVAL);
    }
}

fn parse_args() -> Result<Config, String> {
    let mut arguments = std::env::args().skip(1);
    let mut package = DEFAULT_PACKAGE.to_owned();
    let mut cache_path = None;
    while let Some(argument) = arguments.next() {
        match argument.as_str() {
            "--package" => {
                package = arguments
                    .next()
                    .ok_or_else(|| "--package requires an Android application ID".to_owned())?;
            }
            "--cache" => {
                let value = arguments
                    .next()
                    .ok_or_else(|| "--cache requires an app-private file path".to_owned())?;
                if value.is_empty() {
                    return Err("--cache path cannot be empty".to_owned());
                }
                cache_path = Some(PathBuf::from(value));
            }
            "--help" | "-h" => {
                eprintln!(
                    "usage: enemy_reader [--package APPLICATION_ID] [--cache APP_PRIVATE_PATH]"
                );
                return Err(String::new());
            }
            _ => return Err(format!("unknown argument: {argument}")),
        }
    }
    Ok(Config {
        package,
        cache_path,
    })
}

fn sample_current(
    identity: &ProcessIdentity,
    package: &str,
    candidate_manager: Option<u64>,
    output: &mut impl Write,
    validated_address: &mut Option<u64>,
) -> Result<(Frame, u64), ProbeError> {
    // Process memory is opened afresh per poll; a candidate manager is never
    // used until the identity and its current manager fields validate.
    let ranges = read_anonymous_rw_private_maps(identity.pid)?;
    let mut reader = MemoryReader::open(identity.pid, ranges.clone())?;
    let mut manager_address = None;
    let mut manager = None;
    let mut ended_candidate = false;
    if let Some(candidate) = candidate_manager {
        if let Ok(0) = read_manager_battle_id(&mut reader, candidate) {
            if !identity_matches(identity, package)? {
                return Err(ProbeError::expired(
                    "process identity changed while validating zero BattleId",
                ));
            }
            if read_anonymous_rw_private_maps(identity.pid)? != ranges {
                return Err(ProbeError::expired(
                    "anonymous memory maps changed while validating zero BattleId",
                ));
            }
            *validated_address = Some(candidate);
            manager_address = Some(candidate);
            ended_candidate = true;
        }
        if manager_address.is_none() {
            if let Ok(values) = read_manager_values(&mut reader, candidate) {
                if identity_matches(identity, package)? {
                    if read_anonymous_rw_private_maps(identity.pid)? != ranges {
                        return Err(ProbeError::expired(
                            "anonymous memory maps changed while validating cached manager fields",
                        ));
                    }
                    *validated_address = Some(candidate);
                    manager_address = Some(candidate);
                    manager = Some(values);
                }
            }
        }
    }
    if manager_address.is_none() {
        write_frame(output, &Frame::empty(Status::Locating, Some(identity)))
            .map_err(|error| ProbeError::from_io("write locating frame", error))?;
        reader.clear_cache();
        let mut last_progress = Instant::now();
        let mut progress = |stage: &str, done: u64, total: u64| {
            if last_progress.elapsed() >= Duration::from_secs(2) {
                eprintln!("ENEMY_READER_STATUS|{stage}|{done}|{total}");
                last_progress = Instant::now();
            }
            Ok(())
        };
        let discovered = luajit::discover_manager(&mut reader, &mut progress)?;
        if !identity_matches(identity, package)? {
            return Err(ProbeError::expired(
                "process identity changed during cold manager discovery",
            ));
        }
        if read_anonymous_rw_private_maps(identity.pid)? != ranges {
            return Err(ProbeError::expired(
                "anonymous memory maps changed during cold manager discovery",
            ));
        }
        let discovered_values = read_manager_values(&mut reader, discovered)?;
        if !identity_matches(identity, package)? {
            return Err(ProbeError::expired(
                "process identity changed while validating discovered manager fields",
            ));
        }
        if read_anonymous_rw_private_maps(identity.pid)? != ranges {
            return Err(ProbeError::expired(
                "anonymous memory maps changed while validating discovered manager fields",
            ));
        }
        *validated_address = Some(discovered);
        manager = Some(discovered_values);
        manager_address = Some(discovered);
    }
    let manager_address =
        manager_address.ok_or_else(|| ProbeError::read("manager address was not bound"))?;

    let ended = ended_candidate || manager.as_ref().is_some_and(manager_ended);
    let position_map_before = if ended {
        None
    } else {
        let manager = manager
            .as_ref()
            .ok_or_else(|| ProbeError::read("active manager values were not bound"))?;
        if manager.battle_id == 0 || manager.round == 0 {
            None
        } else {
            Some(read_role_position_map(&mut reader, manager.position_table)?)
        }
    };
    let mut attribute_witness = None;
    let mut frame = if ended {
        Frame::empty(Status::NotInBattle, Some(identity))
    } else {
        let manager = manager
            .as_ref()
            .ok_or_else(|| ProbeError::read("active manager values were not bound"))?;
        if manager.battle_id == 0 {
            Frame::empty(Status::NotInBattle, Some(identity))
        } else if manager.round == 0 {
            Frame::empty(Status::Locating, Some(identity))
        } else {
            let (slots, attributes) = read_slots_partial_with_attributes(
                &mut reader,
                manager_address,
                manager,
                position_map_before
                    .as_ref()
                    .ok_or_else(|| ProbeError::read("position mapping was not captured"))?,
            )?;
            let mut frame = Frame::empty(Status::Live, Some(identity));
            frame.battle_id = manager.battle_id;
            frame.round = manager.round;
            frame.slots = slots;
            attribute_witness = attributes;
            frame
        }
    };
    if !ended {
        if let Some(manager) = manager.as_ref() {
            frame.battle_id = manager.battle_id;
            frame.round = manager.round;
        }
    }

    reader.clear_cache();
    if !identity_matches(identity, package)? {
        return Err(ProbeError::expired(
            "process identity changed before sample validation completed",
        ));
    }
    if ended {
        if read_manager_battle_id(&mut reader, manager_address)? != 0 {
            return Err(ProbeError::expired(
                "BattleId changed during not-in-battle sample",
            ));
        }
    } else {
        let manager_before = manager
            .as_ref()
            .ok_or_else(|| ProbeError::read("active manager values were not bound"))?;
        let manager_after = read_manager_values(&mut reader, manager_address)?;
        if manager_after.battle_id != manager_before.battle_id
            || manager_after.round != manager_before.round
            || manager_after.player_id != manager_before.player_id
            || manager_after.role_table != manager_before.role_table
            || manager_after.position_table != manager_before.position_table
        {
            return Err(ProbeError::expired(
                "battle ID, round, or table references changed during the sample",
            ));
        }
        if manager_before.battle_id != 0 && manager_before.round > 0 {
            let position_map_after =
                read_role_position_map(&mut reader, manager_after.position_table)?;
            if Some(&position_map_after) != position_map_before.as_ref() {
                return Err(ProbeError::expired(
                    "role-to-position mapping changed during the sample",
                ));
            }
            if let Some(witness_before) = attribute_witness.as_ref() {
                let (_, witness_after) = read_slots_partial_with_attributes(
                    &mut reader,
                    manager_address,
                    &manager_after,
                    &position_map_after,
                )?;
                if witness_after.as_ref() != Some(witness_before) {
                    return Err(ProbeError::expired(
                        "attribute bindings changed during the sample",
                    ));
                }
            }
        }
    }
    if read_anonymous_rw_private_maps(identity.pid)? != ranges {
        return Err(ProbeError::expired(
            "anonymous memory maps changed during final sample validation",
        ));
    }
    if !identity_matches(identity, package)? {
        return Err(ProbeError::expired(
            "process identity changed during final sample validation",
        ));
    }
    Ok((frame, manager_address))
}
