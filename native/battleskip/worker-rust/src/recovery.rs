use std::collections::HashMap;

pub const TARGET_PACKAGE: &str = "com.gdoo.dhxy";

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct ProcessIdentity {
    pub pid: u32,
    pub start_ticks: u64,
    pub exe: String,
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct RecoveryIdentity {
    pub pid: u32,
    pub start_ticks: u64,
    pub exe: String,
    pub boot_id: String,
}

impl RecoveryIdentity {
    pub fn from_process(process: &ProcessIdentity, boot_id: String) -> Self {
        Self {
            pid: process.pid,
            start_ticks: process.start_ticks,
            exe: process.exe.clone(),
            boot_id,
        }
    }

    pub fn serialize(&self) -> String {
        format!(
            "version=1\npackage={TARGET_PACKAGE}\npid={}\nstartTicks={}\nexe={}\nboot_id={}\n",
            self.pid, self.start_ticks, self.exe, self.boot_id
        )
    }
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub enum StoredIdentity {
    Missing,
    Unreadable,
    Corrupt,
    Valid(RecoveryIdentity),
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub enum BootObservation {
    Value(String),
    Unreadable,
    Invalid,
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub enum OriginalProcess {
    Gone,
    ReadError,
    StartTicksChanged(u64),
    SameIdentity {
        exe: String,
        cmdline_first_arg: Vec<u8>,
    },
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum ProcDirectoryObservation {
    Present,
    Missing,
    Unreadable,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum LockObservation {
    Acquired,
    Busy,
    Unavailable,
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub enum ProcessScan {
    Complete(Vec<ProcessIdentity>),
    Error,
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub enum Decision {
    Unknown(&'static str),
    Observe(RecoveryIdentity),
    Waiting(RecoveryIdentity),
    VerifiedNoTarget,
    VerifiedOldProcess(RecoveryIdentity),
}

pub fn is_valid_boot_id(value: &str) -> bool {
    if value.len() != 36 {
        return false;
    }
    value.bytes().enumerate().all(|(index, byte)| {
        if matches!(index, 8 | 13 | 18 | 23) {
            byte == b'-'
        } else {
            byte.is_ascii_hexdigit()
        }
    })
}

pub fn is_app_process_exe(value: &str) -> bool {
    matches!(
        value,
        "/system/bin/app_process" | "/system/bin/app_process32" | "/system/bin/app_process64"
    )
}

pub fn status_has_starting_state(contents: &str) -> bool {
    contents.lines().any(|line| {
        line.split_once('=').is_some_and(|(key, value)| {
            key.trim() == "state" && value.split_whitespace().next() == Some("starting")
        })
    })
}

pub fn missing_stat_result(directory: ProcDirectoryObservation) -> OriginalProcess {
    match directory {
        ProcDirectoryObservation::Missing => OriginalProcess::Gone,
        ProcDirectoryObservation::Present | ProcDirectoryObservation::Unreadable => {
            OriginalProcess::ReadError
        }
    }
}

pub fn parse_identity(contents: &str) -> Result<RecoveryIdentity, &'static str> {
    let mut fields = HashMap::new();
    for line in contents.lines() {
        let Some((key, value)) = line.split_once('=') else {
            return Err("malformed_line");
        };
        if key.is_empty() || value.is_empty() || value.contains('\r') {
            return Err("malformed_field");
        }
        if fields.insert(key, value).is_some() {
            return Err("duplicate_field");
        }
    }
    if fields.len() != 6
        || fields.get("version") != Some(&"1")
        || fields.get("package") != Some(&TARGET_PACKAGE)
    {
        return Err("unsupported_record");
    }

    let pid = fields
        .get("pid")
        .ok_or("missing_pid")?
        .parse::<u32>()
        .map_err(|_| "invalid_pid")?;
    let start_ticks = fields
        .get("startTicks")
        .ok_or("missing_start_ticks")?
        .parse::<u64>()
        .map_err(|_| "invalid_start_ticks")?;
    let exe = fields.get("exe").ok_or("missing_exe")?.to_string();
    let boot_id = fields.get("boot_id").ok_or("missing_boot_id")?.to_string();
    if pid == 0 || start_ticks == 0 {
        return Err("zero_process_identity");
    }
    if !is_app_process_exe(&exe) {
        return Err("invalid_exe");
    }
    if !is_valid_boot_id(&boot_id) {
        return Err("invalid_boot_id");
    }

    Ok(RecoveryIdentity {
        pid,
        start_ticks,
        exe,
        boot_id,
    })
}

pub fn decide(
    lock: LockObservation,
    stored: StoredIdentity,
    boot: BootObservation,
    original_process: OriginalProcess,
    scan: ProcessScan,
) -> Decision {
    match lock {
        LockObservation::Busy => return Decision::Unknown("worker_running"),
        LockObservation::Unavailable => return Decision::Unknown("worker_lock_unavailable"),
        LockObservation::Acquired => {}
    }

    match stored {
        StoredIdentity::Unreadable => Decision::Unknown("identity_unreadable"),
        StoredIdentity::Corrupt => Decision::Unknown("identity_corrupt"),
        StoredIdentity::Valid(identity) => match boot {
            BootObservation::Unreadable => Decision::Unknown("boot_id_unreadable"),
            BootObservation::Invalid => Decision::Unknown("boot_id_invalid"),
            BootObservation::Value(current) if !is_valid_boot_id(&current) => {
                Decision::Unknown("boot_id_invalid")
            }
            BootObservation::Value(current) if current != identity.boot_id => {
                Decision::VerifiedOldProcess(identity)
            }
            BootObservation::Value(_) => match original_process {
                OriginalProcess::Gone => Decision::VerifiedOldProcess(identity),
                OriginalProcess::ReadError => Decision::Unknown("original_process_unreadable"),
                OriginalProcess::StartTicksChanged(actual) if actual > 0 => {
                    Decision::VerifiedOldProcess(identity)
                }
                OriginalProcess::StartTicksChanged(_) => {
                    Decision::Unknown("original_process_identity_invalid")
                }
                OriginalProcess::SameIdentity { exe, .. } if exe != identity.exe => {
                    Decision::Unknown("original_process_exe_changed")
                }
                OriginalProcess::SameIdentity {
                    cmdline_first_arg, ..
                } if cmdline_first_arg != TARGET_PACKAGE.as_bytes() => {
                    Decision::Unknown("original_process_cmdline_changed")
                }
                OriginalProcess::SameIdentity { .. } => Decision::Waiting(identity),
            },
        },
        StoredIdentity::Missing => match scan {
            ProcessScan::Error => Decision::Unknown("process_scan_unreadable"),
            ProcessScan::Complete(processes) if processes.is_empty() => Decision::VerifiedNoTarget,
            ProcessScan::Complete(processes) if processes.len() > 1 => {
                Decision::Unknown("multiple_target_processes")
            }
            ProcessScan::Complete(processes) => {
                let process = &processes[0];
                if process.pid == 0 || process.start_ticks == 0 || !is_app_process_exe(&process.exe)
                {
                    return Decision::Unknown("process_identity_invalid");
                }
                match boot {
                    BootObservation::Unreadable => Decision::Unknown("boot_id_unreadable"),
                    BootObservation::Invalid => Decision::Unknown("boot_id_invalid"),
                    BootObservation::Value(boot_id) if !is_valid_boot_id(&boot_id) => {
                        Decision::Unknown("boot_id_invalid")
                    }
                    BootObservation::Value(boot_id) => {
                        Decision::Observe(RecoveryIdentity::from_process(process, boot_id))
                    }
                }
            }
        },
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const BOOT_A: &str = "01234567-89ab-cdef-0123-456789abcdef";
    const BOOT_B: &str = "11234567-89ab-cdef-0123-456789abcdef";
    const EXE: &str = "/system/bin/app_process64";

    fn identity() -> RecoveryIdentity {
        RecoveryIdentity {
            pid: 4242,
            start_ticks: 998877,
            exe: EXE.into(),
            boot_id: BOOT_A.into(),
        }
    }

    fn process() -> ProcessIdentity {
        ProcessIdentity {
            pid: 4242,
            start_ticks: 998877,
            exe: EXE.into(),
        }
    }

    #[test]
    fn identity_round_trip_rejects_duplicate_and_zero_fields() {
        let expected = identity();
        assert_eq!(parse_identity(&expected.serialize()), Ok(expected));
        assert!(parse_identity("version=1\nversion=1\n").is_err());
        assert!(parse_identity(
            &identity()
                .serialize()
                .replace("startTicks=998877", "startTicks=0")
        )
        .is_err());
    }

    #[test]
    fn pid_reuse_and_boot_change_prove_original_process_ended() {
        assert_eq!(
            decide(
                LockObservation::Acquired,
                StoredIdentity::Valid(identity()),
                BootObservation::Value(BOOT_A.into()),
                OriginalProcess::StartTicksChanged(123),
                ProcessScan::Error,
            ),
            Decision::VerifiedOldProcess(identity())
        );
        assert_eq!(
            decide(
                LockObservation::Acquired,
                StoredIdentity::Valid(identity()),
                BootObservation::Value(BOOT_B.into()),
                OriginalProcess::ReadError,
                ProcessScan::Error,
            ),
            Decision::VerifiedOldProcess(identity())
        );
    }

    #[test]
    fn live_or_unreadable_original_process_never_proves_end() {
        assert_eq!(
            decide(
                LockObservation::Acquired,
                StoredIdentity::Valid(identity()),
                BootObservation::Value(BOOT_A.into()),
                OriginalProcess::SameIdentity {
                    exe: EXE.into(),
                    cmdline_first_arg: TARGET_PACKAGE.as_bytes().to_vec(),
                },
                ProcessScan::Error,
            ),
            Decision::Waiting(identity())
        );
        assert_eq!(
            decide(
                LockObservation::Acquired,
                StoredIdentity::Valid(identity()),
                BootObservation::Value(BOOT_A.into()),
                OriginalProcess::ReadError,
                ProcessScan::Complete(Vec::new()),
            ),
            Decision::Unknown("original_process_unreadable")
        );
    }

    #[test]
    fn legacy_requires_complete_scan_and_only_records_a_unique_baseline() {
        assert_eq!(
            decide(
                LockObservation::Acquired,
                StoredIdentity::Missing,
                BootObservation::Value(BOOT_A.into()),
                OriginalProcess::ReadError,
                ProcessScan::Complete(vec![process()]),
            ),
            Decision::Observe(identity())
        );
        assert_eq!(
            decide(
                LockObservation::Acquired,
                StoredIdentity::Missing,
                BootObservation::Unreadable,
                OriginalProcess::ReadError,
                ProcessScan::Complete(Vec::new()),
            ),
            Decision::VerifiedNoTarget
        );
        assert_eq!(
            decide(
                LockObservation::Acquired,
                StoredIdentity::Missing,
                BootObservation::Value(BOOT_A.into()),
                OriginalProcess::ReadError,
                ProcessScan::Error,
            ),
            Decision::Unknown("process_scan_unreadable")
        );
    }

    #[test]
    fn busy_worker_and_corrupt_identity_fail_closed() {
        assert_eq!(
            decide(
                LockObservation::Busy,
                StoredIdentity::Missing,
                BootObservation::Value(BOOT_A.into()),
                OriginalProcess::Gone,
                ProcessScan::Complete(Vec::new()),
            ),
            Decision::Unknown("worker_running")
        );
        assert_eq!(
            decide(
                LockObservation::Acquired,
                StoredIdentity::Corrupt,
                BootObservation::Value(BOOT_A.into()),
                OriginalProcess::Gone,
                ProcessScan::Complete(Vec::new()),
            ),
            Decision::Unknown("identity_corrupt")
        );
    }

    #[test]
    fn missing_stat_proves_end_only_when_pid_directory_is_gone() {
        assert_eq!(
            missing_stat_result(ProcDirectoryObservation::Missing),
            OriginalProcess::Gone
        );
        assert_eq!(
            missing_stat_result(ProcDirectoryObservation::Present),
            OriginalProcess::ReadError
        );
        assert_eq!(
            missing_stat_result(ProcDirectoryObservation::Unreadable),
            OriginalProcess::ReadError
        );
    }

    #[test]
    fn starting_state_is_recognized_with_additional_fields() {
        assert!(status_has_starting_state(
            "state=starting\nreason=new_run\n"
        ));
        assert!(status_has_starting_state("state=starting reason=new_run\n"));
        assert!(!status_has_starting_state(
            "state=waiting_game\nreason=starting\n"
        ));
    }
}
