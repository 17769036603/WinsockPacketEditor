use std::fmt;
use std::io;

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct ProcessIdentity {
    pub pid: u32,
    pub start_ticks: u64,
    pub executable: String,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct MemoryRange {
    pub start: u64,
    pub end: u64,
}

impl MemoryRange {
    pub fn contains(&self, address: u64, size: usize) -> bool {
        if size == 0 {
            return false;
        }
        let Some(end) = address.checked_add(size as u64) else {
            return false;
        };
        self.start <= address && end <= self.end
    }
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum ErrorKind {
    /// The root-launched worker could not open or read the target proc entry.
    RootDenied,
    /// The proc entry or memory read exists but has invalid/incomplete data.
    ReadError,
    /// PID identity or validated battle bindings changed during one sample.
    Expired,
}

#[derive(Debug)]
pub struct ProbeError {
    pub kind: ErrorKind,
    pub message: String,
}

impl ProbeError {
    pub fn new(kind: ErrorKind, message: impl Into<String>) -> Self {
        Self {
            kind,
            message: message.into(),
        }
    }

    pub fn from_io(context: &str, error: io::Error) -> Self {
        let kind = if error.kind() == io::ErrorKind::PermissionDenied {
            ErrorKind::RootDenied
        } else {
            ErrorKind::ReadError
        };
        Self::new(kind, format!("{context}: {error}"))
    }

    pub fn expired(message: impl Into<String>) -> Self {
        Self::new(ErrorKind::Expired, message)
    }

    pub fn read(message: impl Into<String>) -> Self {
        Self::new(ErrorKind::ReadError, message)
    }
}

impl fmt::Display for ProbeError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str(&self.message)
    }
}

impl std::error::Error for ProbeError {}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
#[repr(u8)]
pub enum Status {
    NoGame = 0,
    Locating = 1,
    NotInBattle = 2,
    Live = 3,
    RootDenied = 4,
    ReadError = 5,
    Expired = 6,
}

#[derive(Clone, Copy, Debug, Default)]
pub struct Slot {
    pub position: u32,
    pub state: u8,
    pub hp: i64,
    pub mp: i64,
    pub sp: i64,
}

#[derive(Clone, Debug)]
pub struct Frame {
    pub status: Status,
    pub pid: u32,
    pub start_ticks: u64,
    pub battle_id: u64,
    pub round: u32,
    pub slots: [Slot; 20],
}

impl Frame {
    pub fn empty(status: Status, identity: Option<&ProcessIdentity>) -> Self {
        let mut frame = Self {
            status,
            pid: 0,
            start_ticks: 0,
            battle_id: 0,
            round: 0,
            slots: [Slot::default(); 20],
        };
        if let Some(identity) = identity {
            frame.pid = identity.pid;
            frame.start_ticks = identity.start_ticks;
        }
        frame
    }
}
