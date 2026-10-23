use crate::model::{ErrorKind, MemoryRange, ProbeError, ProcessIdentity};
use std::fs;
use std::path::Path;

pub fn validate_package(package: &str) -> Result<(), ProbeError> {
    let mut segments = package.split('.');
    let valid_segment = |segment: &str| {
        let mut bytes = segment.bytes();
        matches!(bytes.next(), Some(b'A'..=b'Z' | b'a'..=b'z' | b'_'))
            && bytes.all(|byte| byte.is_ascii_alphanumeric() || byte == b'_')
    };
    if package.trim() != package
        || package.is_empty()
        || !valid_segment(segments.next().unwrap_or_default())
        || segments.count() == 0
        || package.split('.').any(|segment| !valid_segment(segment))
    {
        return Err(ProbeError::read("invalid Android package name"));
    }
    Ok(())
}

pub fn find_process(package: &str) -> Result<Option<ProcessIdentity>, ProbeError> {
    let entries =
        fs::read_dir("/proc").map_err(|error| ProbeError::from_io("read /proc", error))?;
    let mut found: Option<ProcessIdentity> = None;
    for entry in entries {
        let entry = match entry {
            Ok(entry) => entry,
            Err(_) => continue,
        };
        let name = entry.file_name();
        let Some(pid) = name.to_str().and_then(|value| value.parse::<u32>().ok()) else {
            continue;
        };
        if pid == 0 {
            continue;
        }
        let cmdline_path = entry.path().join("cmdline");
        let cmdline = match fs::read(&cmdline_path) {
            Ok(bytes) => bytes,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => continue,
            Err(_) => continue,
        };
        let first_argument = cmdline.split(|byte| *byte == 0).next().unwrap_or_default();
        if first_argument != package.as_bytes() {
            continue;
        }
        match read_identity_for_pid(pid, package) {
            Ok(identity) => {
                if found.is_some() {
                    return Err(ProbeError::read(
                        "more than one process has the requested package command line",
                    ));
                }
                found = Some(identity);
            }
            Err(error) if error.kind == ErrorKind::Expired => continue,
            Err(error) => return Err(error),
        }
    }
    Ok(found)
}

pub fn read_identity_for_pid(pid: u32, package: &str) -> Result<ProcessIdentity, ProbeError> {
    let process_path = Path::new("/proc").join(pid.to_string());
    let cmdline = fs::read(process_path.join("cmdline")).map_err(|error| {
        if error.kind() == std::io::ErrorKind::NotFound {
            ProbeError::expired("process disappeared while binding identity")
        } else {
            ProbeError::from_io("read process cmdline", error)
        }
    })?;
    let first_argument = cmdline.split(|byte| *byte == 0).next().unwrap_or_default();
    if first_argument != package.as_bytes() {
        return Err(ProbeError::expired(
            "process command line no longer matches the requested package",
        ));
    }

    let stat = fs::read_to_string(process_path.join("stat")).map_err(|error| {
        if error.kind() == std::io::ErrorKind::NotFound {
            ProbeError::expired("process disappeared while reading start ticks")
        } else {
            ProbeError::from_io("read process stat", error)
        }
    })?;
    let close_paren = stat
        .rfind(')')
        .ok_or_else(|| ProbeError::read("malformed /proc process stat"))?;
    let fields: Vec<&str> = stat[close_paren + 1..].split_whitespace().collect();
    let start_ticks = fields
        .get(19)
        .and_then(|value| value.parse::<u64>().ok())
        .filter(|value| *value > 0)
        .ok_or_else(|| ProbeError::read("could not parse process start ticks"))?;

    let executable = fs::read_link(process_path.join("exe")).map_err(|error| {
        if error.kind() == std::io::ErrorKind::NotFound {
            ProbeError::expired("process disappeared while reading executable")
        } else {
            ProbeError::from_io("read process executable", error)
        }
    })?;
    let executable = executable.to_string_lossy().into_owned();
    if executable.is_empty() {
        return Err(ProbeError::read("process executable path is empty"));
    }
    let verify_cmdline = fs::read(process_path.join("cmdline")).map_err(|error| {
        if error.kind() == std::io::ErrorKind::NotFound {
            ProbeError::expired("process disappeared while verifying identity")
        } else {
            ProbeError::from_io("re-read process cmdline", error)
        }
    })?;
    let verify_argument = verify_cmdline
        .split(|byte| *byte == 0)
        .next()
        .unwrap_or_default();
    let verify_stat = fs::read_to_string(process_path.join("stat")).map_err(|error| {
        if error.kind() == std::io::ErrorKind::NotFound {
            ProbeError::expired("process disappeared while verifying start ticks")
        } else {
            ProbeError::from_io("re-read process stat", error)
        }
    })?;
    let verify_close_paren = verify_stat
        .rfind(')')
        .ok_or_else(|| ProbeError::read("malformed /proc process stat during verification"))?;
    let verify_fields: Vec<&str> = verify_stat[verify_close_paren + 1..]
        .split_whitespace()
        .collect();
    let verify_start_ticks = verify_fields
        .get(19)
        .and_then(|value| value.parse::<u64>().ok());
    let verify_executable = fs::read_link(process_path.join("exe")).map_err(|error| {
        if error.kind() == std::io::ErrorKind::NotFound {
            ProbeError::expired("process disappeared while verifying executable")
        } else {
            ProbeError::from_io("re-read process executable", error)
        }
    })?;
    if verify_argument != package.as_bytes()
        || verify_start_ticks != Some(start_ticks)
        || verify_executable.to_string_lossy().as_ref() != executable.as_str()
    {
        return Err(ProbeError::expired(
            "process identity changed while it was being bound",
        ));
    }
    Ok(ProcessIdentity {
        pid,
        start_ticks,
        executable,
    })
}

pub fn identity_matches(expected: &ProcessIdentity, package: &str) -> Result<bool, ProbeError> {
    match read_identity_for_pid(expected.pid, package) {
        Ok(actual) => Ok(actual == *expected),
        Err(error) if error.kind == ErrorKind::Expired => Ok(false),
        Err(error) => Err(error),
    }
}

pub fn read_anonymous_rw_private_maps(pid: u32) -> Result<Vec<MemoryRange>, ProbeError> {
    let raw = fs::read_to_string(format!("/proc/{pid}/maps")).map_err(|error| {
        if error.kind() == std::io::ErrorKind::NotFound {
            ProbeError::expired("process disappeared while reading maps")
        } else {
            ProbeError::from_io("read process maps", error)
        }
    })?;
    let mut ranges = Vec::new();
    for line in raw.lines() {
        let mut parts = line.split_whitespace();
        let Some(address_range) = parts.next() else {
            continue;
        };
        let Some(permissions) = parts.next() else {
            continue;
        };
        let Some((start, end)) = address_range.split_once('-') else {
            continue;
        };
        let (Ok(start), Ok(end)) = (u64::from_str_radix(start, 16), u64::from_str_radix(end, 16))
        else {
            continue;
        };
        let _offset = parts.next();
        let _device = parts.next();
        let _inode = parts.next();
        let has_path = parts.next().is_some();
        if end <= start
            || has_path
            || !permissions.as_bytes().contains(&b'r')
            || !permissions.as_bytes().contains(&b'w')
            || !permissions.as_bytes().contains(&b'p')
            || permissions.as_bytes().contains(&b's')
        {
            continue;
        }
        ranges.push(MemoryRange { start, end });
    }
    ranges.sort_by_key(|range| range.start);
    if ranges.is_empty() {
        return Err(ProbeError::read(
            "no readable writable private anonymous mappings were found",
        ));
    }
    Ok(ranges)
}
