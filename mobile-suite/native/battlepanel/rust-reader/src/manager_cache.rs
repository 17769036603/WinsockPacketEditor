use crate::model::{ProbeError, ProcessIdentity};
use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

const CACHE_MAGIC: &str = "WPE-BATTLE-MANAGER-CACHE";
const CACHE_VERSION: u32 = 2;
const MAX_CACHE_BYTES: u64 = 4096;

/// A persisted manager address is only a candidate for this exact target identity.
/// Callers must re-read and validate manager fields before using the address.
#[derive(Clone, Debug)]
pub struct ManagerCacheEntry {
    pub package: String,
    pub identity: ProcessIdentity,
    pub address: u64,
}

pub fn load_candidate(path: &Path, package: &str, identity: &ProcessIdentity) -> Option<u64> {
    let metadata = fs::metadata(path).ok()?;
    if metadata.len() == 0 || metadata.len() > MAX_CACHE_BYTES {
        return None;
    }
    let contents = fs::read_to_string(path).ok()?;
    let entry = parse(&contents)?;
    (entry.package == package && &entry.identity == identity && entry.address > 0)
        .then_some(entry.address)
}

pub fn store_candidate(
    path: &Path,
    package: &str,
    identity: &ProcessIdentity,
    address: u64,
) -> Result<(), ProbeError> {
    if address == 0 {
        return Err(ProbeError::read(
            "refusing to persist an empty manager candidate",
        ));
    }
    let entry = ManagerCacheEntry {
        package: package.to_owned(),
        identity: identity.clone(),
        address,
    };
    let contents = serialize(&entry);
    let parent = path.parent().filter(|value| !value.as_os_str().is_empty());
    if let Some(parent) = parent {
        fs::create_dir_all(parent)
            .map_err(|error| ProbeError::from_io("create manager-cache directory", error))?;
    }
    let temporary = temporary_path(path)?;
    let mut file = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&temporary)
        .map_err(|error| ProbeError::from_io("create temporary manager cache", error))?;
    let write_result = file
        .write_all(contents.as_bytes())
        .map_err(|error| ProbeError::from_io("write manager cache", error))
        .and_then(|_| {
            file.sync_all()
                .map_err(|error| ProbeError::from_io("sync manager cache", error))
        });
    drop(file);
    let write_result = write_result.and_then(|_| {
        fs::rename(&temporary, path)
            .map_err(|error| ProbeError::from_io("atomically replace manager cache", error))
    });
    if write_result.is_err() {
        let _ = fs::remove_file(&temporary);
    }
    write_result
}

fn serialize(entry: &ManagerCacheEntry) -> String {
    format!(
        "{CACHE_MAGIC}\nversion={CACHE_VERSION}\npackage={}\npid={}\nstart_ticks={}\nexecutable={}\nmanager=0x{:x}\n",
        hex_encode(entry.package.as_bytes()),
        entry.identity.pid,
        entry.identity.start_ticks,
        hex_encode(entry.identity.executable.as_bytes()),
        entry.address
    )
}

fn parse(contents: &str) -> Option<ManagerCacheEntry> {
    let mut lines = contents.lines();
    if lines.next()? != CACHE_MAGIC {
        return None;
    }
    let version = field(lines.next()?, "version")?.parse::<u32>().ok()?;
    if version != CACHE_VERSION {
        return None;
    }
    let package = String::from_utf8(hex_decode(field(lines.next()?, "package")?)?).ok()?;
    let pid = field(lines.next()?, "pid")?.parse::<u32>().ok()?;
    let start_ticks = field(lines.next()?, "start_ticks")?.parse::<u64>().ok()?;
    let executable = String::from_utf8(hex_decode(field(lines.next()?, "executable")?)?).ok()?;
    let address_text = field(lines.next()?, "manager")?;
    let address = address_text
        .strip_prefix("0x")
        .and_then(|value| u64::from_str_radix(value, 16).ok())?;
    if lines.next().is_some() || pid == 0 || start_ticks == 0 || address == 0 {
        return None;
    }
    Some(ManagerCacheEntry {
        package,
        identity: ProcessIdentity {
            pid,
            start_ticks,
            executable,
        },
        address,
    })
}

fn field<'a>(line: &'a str, name: &str) -> Option<&'a str> {
    line.strip_prefix(name)?.strip_prefix('=')
}

fn hex_encode(bytes: &[u8]) -> String {
    let mut encoded = String::with_capacity(bytes.len() * 2);
    for byte in bytes {
        use std::fmt::Write as _;
        let _ = write!(&mut encoded, "{byte:02x}");
    }
    encoded
}

fn hex_decode(value: &str) -> Option<Vec<u8>> {
    if value.len() % 2 != 0 {
        return None;
    }
    let mut decoded = Vec::with_capacity(value.len() / 2);
    for pair in value.as_bytes().chunks_exact(2) {
        let hex = std::str::from_utf8(pair).ok()?;
        decoded.push(u8::from_str_radix(hex, 16).ok()?);
    }
    Some(decoded)
}

fn temporary_path(path: &Path) -> Result<PathBuf, ProbeError> {
    let parent = path.parent().unwrap_or_else(|| Path::new("."));
    let name = path
        .file_name()
        .ok_or_else(|| ProbeError::read("manager-cache path has no file name"))?
        .to_string_lossy();
    let nonce = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos();
    Ok(parent.join(format!(".{name}.tmp-{}-{nonce}", std::process::id())))
}
