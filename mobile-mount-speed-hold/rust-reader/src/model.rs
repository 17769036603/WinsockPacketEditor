use std::{fmt, io};

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
        size > 0
            && address
                .checked_add(size as u64)
                .is_some_and(|end| self.start <= address && end <= self.end)
    }
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum ErrorKind {
    RootDenied,
    ReadError,
    RetryableMemoryRead,
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
    pub fn from_process_memory_io(error: io::Error) -> Self {
        // Linux/Android EIO can occur when a mapped page changes during a read.
        let kind = if error.raw_os_error() == Some(5) {
            ErrorKind::RetryableMemoryRead
        } else if error.kind() == io::ErrorKind::PermissionDenied {
            ErrorKind::RootDenied
        } else {
            ErrorKind::ReadError
        };
        Self::new(kind, format!("read process memory: {error}"))
    }
    pub fn expired(message: impl Into<String>) -> Self {
        Self::new(ErrorKind::Expired, message)
    }
    pub fn read(message: impl Into<String>) -> Self {
        Self::new(ErrorKind::ReadError, message)
    }
}

impl fmt::Display for ProbeError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.message)
    }
}
impl std::error::Error for ProbeError {}

#[cfg(test)]
mod tests {
    use super::{ErrorKind, ProbeError};
    use std::io;

    #[test]
    fn process_memory_eio_is_retryable_but_permission_denial_is_not() {
        assert_eq!(
            ProbeError::from_process_memory_io(io::Error::from_raw_os_error(5)).kind,
            ErrorKind::RetryableMemoryRead
        );
        assert_eq!(
            ProbeError::from_process_memory_io(io::Error::from(io::ErrorKind::PermissionDenied))
                .kind,
            ErrorKind::RootDenied
        );
    }
}
