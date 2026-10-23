#[path = "../../../license_lease.rs"]
mod license_lease;
mod luajit;
mod memory;
mod model;
mod procfs;

use model::{ErrorKind, ProbeError, ProcessIdentity};
use std::{
    env,
    fs::{self, File, OpenOptions},
    io::{self, Write},
    os::unix::fs::FileExt,
    path::{Path, PathBuf},
    sync::mpsc,
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};

#[derive(Clone)]
struct Journal {
    pid: u32,
    ticks: u64,
    exe: String,
    player: i64,
    legacy: bool,
    table: u64,
    address: u64,
    old: [u8; 8],
    new: [u8; 8],
}

const MEMORY_READ_RETRY_DELAYS_MS: [u64; 3] = [100, 300, 800];
const MISSING_CANDIDATE_RETRY_DELAYS_MS: [u64; 3] = [500, 1_000, 2_000];
const REFRESH_METRIC_WINDOW: Duration = Duration::from_secs(30);

struct RefreshTelemetry {
    window_started: Instant,
    counts: [u64; 3],
    totals_us: [u64; 3],
    maxima_us: [u64; 3],
    cache_fallbacks: u64,
    rebind_started: Option<Instant>,
    rebind_attempts: u64,
}

impl RefreshTelemetry {
    fn new() -> Self {
        Self {
            window_started: Instant::now(),
            counts: [0; 3],
            totals_us: [0; 3],
            maxima_us: [0; 3],
            cache_fallbacks: 0,
            rebind_started: None,
            rebind_attempts: 0,
        }
    }

    fn record_measurements(&mut self, measurements: &[(luajit::RefreshPath, Duration)]) {
        for &(path, elapsed) in measurements {
            let index = match path {
                luajit::RefreshPath::CacheHit => 0,
                luajit::RefreshPath::KnownTableScan => 1,
                luajit::RefreshPath::FullDiscovery => 2,
            };
            let elapsed_us = elapsed.as_micros().min(u64::MAX as u128) as u64;
            self.counts[index] = self.counts[index].saturating_add(1);
            self.totals_us[index] = self.totals_us[index].saturating_add(elapsed_us);
            self.maxima_us[index] = self.maxima_us[index].max(elapsed_us);
        }
    }

    fn record_lookup(
        &mut self,
        fallback: Option<(Duration, luajit::RefreshPath, luajit::CacheFailureReason)>,
        candidate_found: bool,
    ) {
        if let Some((elapsed, path, reason)) = fallback {
            let elapsed_us = elapsed.as_micros().min(u64::MAX as u128) as u64;
            let path_token = match path {
                luajit::RefreshPath::KnownTableScan => "known_table",
                luajit::RefreshPath::FullDiscovery => "full_discovery",
                luajit::RefreshPath::CacheHit => "known_table",
            };
            metric_fallback(elapsed_us, candidate_found, path_token, reason.token());
            self.cache_fallbacks = self.cache_fallbacks.saturating_add(1);
            if self.rebind_started.is_none() {
                self.rebind_started = Some(
                    Instant::now()
                        .checked_sub(elapsed)
                        .unwrap_or_else(Instant::now),
                );
                self.rebind_attempts = 1;
            } else {
                self.rebind_attempts = self.rebind_attempts.saturating_add(1);
            }
        } else if self.rebind_started.is_some() {
            self.rebind_attempts = self.rebind_attempts.saturating_add(1);
        }
        if candidate_found && self.rebind_started.is_some() {
            self.finish_rebind(true);
        }
        self.flush_window(false);
    }

    fn finish_rebind(&mut self, candidate_found: bool) {
        if let Some(started) = self.rebind_started.take() {
            let elapsed_ms = started.elapsed().as_millis().min(u64::MAX as u128) as u64;
            metric(
                "candidate_rebind",
                &[elapsed_ms, self.rebind_attempts, u64::from(candidate_found)],
            );
            self.rebind_attempts = 0;
        }
    }

    fn finish(&mut self) {
        self.finish_rebind(false);
        self.flush_window(true);
    }

    fn flush_window(&mut self, force: bool) {
        if !force && self.window_started.elapsed() < REFRESH_METRIC_WINDOW {
            return;
        }
        if self.counts.iter().any(|&count| count > 0) {
            metric(
                "refresh_window",
                &[
                    self.counts[0],
                    self.counts[1],
                    self.counts[2],
                    self.cache_fallbacks,
                    average(self.totals_us[0], self.counts[0]),
                    self.maxima_us[0],
                    average(self.totals_us[1], self.counts[1]),
                    self.maxima_us[1],
                    average(self.totals_us[2], self.counts[2]),
                    self.maxima_us[2],
                ],
            );
        }
        self.window_started = Instant::now();
        self.counts = [0; 3];
        self.totals_us = [0; 3];
        self.maxima_us = [0; 3];
        self.cache_fallbacks = 0;
    }
}

fn average(total: u64, count: u64) -> u64 {
    if count == 0 {
        0
    } else {
        total / count
    }
}

fn metric(name: &str, values: &[u64]) {
    if !matches!(name, "refresh_window" | "candidate_rebind") {
        return;
    }
    print!("METRIC\t{name}");
    for value in values {
        print!("\t{value}");
    }
    println!();
    let _ = io::stdout().flush();
}

fn metric_fallback(elapsed_us: u64, candidate_found: bool, path: &str, reason: &str) {
    let path = match path {
        "known_table" | "full_discovery" => path,
        _ => return,
    };
    let reason = match reason {
        "table_unavailable"
        | "cached_key_unreadable"
        | "cached_node_unreadable"
        | "node_out_of_bounds"
        | "hash_chain_corrupt"
        | "key_missing_or_duplicate"
        | "key_changed"
        | "field_value_invalid"
        | "local_player_binding_changed"
        | "player_binding_changed"
        | "mount_binding_changed"
        | "coordinates_invalid"
        | "speed_value_invalid" => reason,
        _ => return,
    };
    println!(
        "METRIC\tcache_fallback\t{elapsed_us}\t{}\t{path}\t{reason}",
        u8::from(candidate_found)
    );
    let _ = io::stdout().flush();
}

fn missing_candidate_retry_delay_ms(retries: u32) -> u64 {
    MISSING_CANDIDATE_RETRY_DELAYS_MS
        [(retries as usize).min(MISSING_CANDIDATE_RETRY_DELAYS_MS.len() - 1)]
}

fn state(code: &str, detail: &str) {
    let detail: String = detail
        .chars()
        .map(|c| {
            if c.is_ascii_alphanumeric() || matches!(c, '_' | '-') {
                c
            } else {
                '_'
            }
        })
        .take(80)
        .collect();
    println!("STATE\t{code}\t{detail}");
    let _ = io::stdout().flush();
}

fn args() -> Result<(String, f64, PathBuf, bool), ProbeError> {
    let mut package = None;
    let mut speed = None;
    let mut dir = None;
    let mut recover_only = false;
    let mut args = env::args().skip(1);
    while let Some(key) = args.next() {
        let value = args
            .next()
            .ok_or_else(|| ProbeError::read("missing argument value"))?;
        match key.as_str() {
            "--package" => package = Some(value),
            "--target-speed" => speed = value.parse::<f64>().ok(),
            "--state-dir" => dir = Some(PathBuf::from(value)),
            "--recover-only" => recover_only = parse_recover_only(&value)?,
            _ => return Err(ProbeError::read("unknown argument")),
        }
    }
    let package = package.ok_or_else(|| ProbeError::read("--package is required"))?;
    procfs::validate_package(&package)?;
    if !matches!(package.as_str(), "com.gdoo.dhxy" | "com.gdoo.xxxy2026") {
        return Err(ProbeError::read(
            "package is not an approved exact mount-hold target",
        ));
    }
    let speed = speed
        .filter(|v| v.is_finite() && *v > 0.0)
        .ok_or_else(|| ProbeError::read("invalid target speed"))?;
    let dir = dir.ok_or_else(|| ProbeError::read("--state-dir is required"))?;
    if !valid_state_dir(&dir) {
        return Err(ProbeError::read(
            "--state-dir is not the suite mount-hold module directory",
        ));
    }
    Ok((package, speed, dir, recover_only))
}

fn parse_recover_only(value: &str) -> Result<bool, ProbeError> {
    match value {
        "true" => Ok(true),
        "false" => Ok(false),
        _ => Err(ProbeError::read("--recover-only must be true or false")),
    }
}

fn valid_state_dir(path: &Path) -> bool {
    matches!(
        path.to_str(),
        Some("/data/user/0/com.xnas.wpe.suite/files/modules/mounthold")
            | Some("/data/data/com.xnas.wpe.suite/files/modules/mounthold")
    )
}

fn hex(bytes: &[u8]) -> String {
    bytes.iter().map(|b| format!("{b:02x}")).collect()
}

fn unhex(s: &str) -> Result<Vec<u8>, ProbeError> {
    if s.len() % 2 != 0 || !s.is_ascii() {
        return Err(ProbeError::read("bad journal hex"));
    }
    (0..s.len())
        .step_by(2)
        .map(|i| {
            u8::from_str_radix(&s[i..i + 2], 16).map_err(|_| ProbeError::read("bad journal hex"))
        })
        .collect()
}

fn raw(s: &str) -> Result<[u8; 8], ProbeError> {
    unhex(s)?
        .try_into()
        .map_err(|_| ProbeError::read("bad journal TValue"))
}

fn save(path: &Path, j: &Journal, create: bool) -> Result<(), ProbeError> {
    let stamp = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_err(|e| ProbeError::read(format!("system clock error: {e}")))?
        .as_nanos();
    let tmp = path.with_extension(format!("journal.{}.{}.tmp", std::process::id(), stamp));
    let mut f = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&tmp)
        .map_err(|e| ProbeError::from_io("create journal temp", e))?;
    writeln!(
        f,
        "role-speed-hold-v2\n{}\n{}\n{}\n{}\n{:x}\n{:x}\n{}\n{}\n{}",
        j.pid,
        j.ticks,
        hex(j.exe.as_bytes()),
        j.player,
        j.table,
        j.address,
        hex(&j.old),
        hex(&j.new),
        false
    )
    .map_err(|e| ProbeError::from_io("write journal", e))?;
    f.sync_all()
        .map_err(|e| ProbeError::from_io("sync journal", e))?;
    if create && path.exists() {
        let _ = fs::remove_file(&tmp);
        return Err(ProbeError::read("journal already exists"));
    }
    if create {
        fs::hard_link(&tmp, path)
            .map_err(|e| ProbeError::from_io("create journal exclusively", e))?;
        fs::remove_file(&tmp).map_err(|e| ProbeError::from_io("remove journal temp", e))?;
    } else {
        fs::rename(&tmp, path).map_err(|e| ProbeError::from_io("replace journal", e))?;
    }
    if let Some(parent) = path.parent() {
        File::open(parent)
            .and_then(|file| file.sync_all())
            .map_err(|e| ProbeError::from_io("sync journal directory", e))?;
    }
    Ok(())
}

fn load(path: &Path) -> Result<Journal, ProbeError> {
    let text = fs::read_to_string(path).map_err(|e| ProbeError::from_io("read journal", e))?;
    let mut lines = text.lines();
    let schema = lines
        .next()
        .ok_or_else(|| ProbeError::read("unknown journal schema"))?;
    let legacy = schema == "mount-hold-v1";
    if schema != "mount-hold-v1" && schema != "role-speed-hold-v2" {
        return Err(ProbeError::read("unknown journal schema"));
    }
    let pid = lines
        .next()
        .and_then(|v| v.parse().ok())
        .ok_or_else(|| ProbeError::read("bad journal pid"))?;
    let ticks = lines
        .next()
        .and_then(|v| v.parse().ok())
        .ok_or_else(|| ProbeError::read("bad journal start ticks"))?;
    let exe = String::from_utf8(unhex(
        lines
            .next()
            .ok_or_else(|| ProbeError::read("missing journal executable"))?,
    )?)
    .map_err(|_| ProbeError::read("bad journal executable"))?;
    let player = lines
        .next()
        .and_then(|v| v.parse().ok())
        .ok_or_else(|| ProbeError::read("bad journal player"))?;
    if legacy {
        let mount = lines
            .next()
            .and_then(|v| v.parse::<i64>().ok())
            .ok_or_else(|| ProbeError::read("bad journal mount"))?;
        if mount <= 0 {
            return Err(ProbeError::read("bad journal mount"));
        }
    }
    let table = u64::from_str_radix(
        lines
            .next()
            .ok_or_else(|| ProbeError::read("missing journal table"))?,
        16,
    )
    .map_err(|_| ProbeError::read("bad journal table"))?;
    let address = u64::from_str_radix(
        lines
            .next()
            .ok_or_else(|| ProbeError::read("missing journal address"))?,
        16,
    )
    .map_err(|_| ProbeError::read("bad journal address"))?;
    let old = raw(lines
        .next()
        .ok_or_else(|| ProbeError::read("missing journal old value"))?)?;
    let new = raw(lines
        .next()
        .ok_or_else(|| ProbeError::read("missing journal new value"))?)?;
    if !legacy && lines.next() != Some("false") {
        return Err(ProbeError::read("bad journal legacy flag"));
    }
    if lines.next().is_some()
        || pid == 0
        || ticks == 0
        || table == 0
        || address == 0
        || player <= 0
        || exe.is_empty()
    {
        return Err(ProbeError::read("invalid journal binding"));
    }
    Ok(Journal {
        pid,
        ticks,
        exe,
        player,
        legacy,
        table,
        address,
        old,
        new,
    })
}

fn sync_parent(path: &Path) -> Result<(), ProbeError> {
    let parent = path
        .parent()
        .ok_or_else(|| ProbeError::read("journal path has no parent"))?;
    File::open(parent)
        .and_then(|file| file.sync_all())
        .map_err(|e| ProbeError::from_io("sync journal archive directory", e))
}

fn archive_journal_to(path: &Path, archive: &Path) -> Result<(), ProbeError> {
    let mut created = false;
    let result = (|| {
        let mut source =
            File::open(path).map_err(|e| ProbeError::from_io("open expired journal", e))?;
        let mut destination = OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(archive)
            .map_err(|e| ProbeError::from_io("create expired journal archive", e))?;
        created = true;
        io::copy(&mut source, &mut destination)
            .map_err(|e| ProbeError::from_io("copy expired journal", e))?;
        destination
            .sync_all()
            .map_err(|e| ProbeError::from_io("sync expired journal archive", e))?;
        sync_parent(archive)?;
        fs::remove_file(path).map_err(|e| ProbeError::from_io("remove archived journal", e))?;
        sync_parent(path)
    })();
    if result.is_err() && created && path.exists() {
        let _ = fs::remove_file(archive);
    }
    result
}

fn archive_expired_journal(path: &Path) -> Result<(), ProbeError> {
    // ponytail: expired journals accumulate until a user-approved retention policy exists.
    let stamp = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_err(|e| ProbeError::read(format!("system clock error: {e}")))?
        .as_nanos();
    let archive = path.with_file_name(format!(
        "mount_hold.journal.expired.{}.{}",
        std::process::id(),
        stamp
    ));
    archive_journal_to(path, &archive)
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum SavedProcessStatus {
    MatchesSelectedPackage,
    AliveForDifferentPackage,
    GoneOrReplaced,
}

fn classify_saved_process(
    selected_matches: bool,
    exact_identity_exists: bool,
) -> SavedProcessStatus {
    match (selected_matches, exact_identity_exists) {
        (true, _) => SavedProcessStatus::MatchesSelectedPackage,
        (false, true) => SavedProcessStatus::AliveForDifferentPackage,
        (false, false) => SavedProcessStatus::GoneOrReplaced,
    }
}

fn identity(j: &Journal) -> ProcessIdentity {
    ProcessIdentity {
        pid: j.pid,
        start_ticks: j.ticks,
        executable: j.exe.clone(),
    }
}

fn read_raw(id: &ProcessIdentity, package: &str, address: u64) -> Result<[u8; 8], ProbeError> {
    if !procfs::identity_matches(id, package)? {
        return Err(ProbeError::expired("process identity changed"));
    }
    let file = File::open(format!("/proc/{}/mem", id.pid))
        .map_err(|e| ProbeError::from_io("open target memory", e))?;
    let mut value = [0; 8];
    file.read_exact_at(&mut value, address)
        .map_err(|e| ProbeError::from_io("read target TValue", e))?;
    Ok(value)
}

const MAX_CONSECUTIVE_WRITE_VERIFICATION_MISSES: u32 = 3;

fn should_write_target(
    current: &[u8; 8],
    baseline: &[u8; 8],
    target: &[u8; 8],
) -> Result<bool, ProbeError> {
    if current == target {
        Ok(false)
    } else if current == baseline {
        Ok(true)
    } else {
        Err(ProbeError::read("held TValue changed externally"))
    }
}

fn verify_hold_write(
    observed: &[u8; 8],
    baseline: &[u8; 8],
    target: &[u8; 8],
    consecutive_misses: &mut u32,
) -> Result<bool, ProbeError> {
    if observed == target {
        *consecutive_misses = 0;
        return Ok(true);
    }
    if observed != baseline {
        return Err(ProbeError::read(
            "hold write verification found an unknown TValue",
        ));
    }
    *consecutive_misses = (*consecutive_misses).saturating_add(1);
    if *consecutive_misses >= MAX_CONSECUTIVE_WRITE_VERIFICATION_MISSES {
        return Err(ProbeError::read(
            "hold write verification failed after 3 consecutive attempts",
        ));
    }
    Ok(false)
}

// This external read/compare/write is not atomic against target threads.
// Identity validation and readback detect some races, but cannot prevent an
// intervening target write. A true compare-exchange needs target-side execution.
fn checked_write(
    id: &ProcessIdentity,
    package: &str,
    address: u64,
    expected: &[u8; 8],
    replacement: &[u8; 8],
) -> Result<[u8; 8], ProbeError> {
    if !procfs::identity_matches(id, package)? {
        return Err(ProbeError::expired("process identity changed before write"));
    }
    let file = OpenOptions::new()
        .read(true)
        .write(true)
        .open(format!("/proc/{}/mem", id.pid))
        .map_err(|e| ProbeError::from_io("open target memory for write", e))?;
    let mut current = [0; 8];
    file.read_exact_at(&mut current, address)
        .map_err(|e| ProbeError::from_io("conditional write read", e))?;
    if &current != expected {
        return Err(ProbeError::read("conditional write value mismatch"));
    }
    if !procfs::identity_matches(id, package)? {
        return Err(ProbeError::expired(
            "process identity changed at write boundary",
        ));
    }
    file.write_all_at(replacement, address)
        .map_err(|e| ProbeError::from_io("write TValue", e))?;
    read_raw(id, package, address)
}

fn write_hold_target(
    id: &ProcessIdentity,
    package: &str,
    address: u64,
    baseline: &[u8; 8],
    target: &[u8; 8],
    consecutive_misses: &mut u32,
) -> Result<bool, ProbeError> {
    let observed = match checked_write(id, package, address, baseline, target) {
        Ok(value) => value,
        Err(error) if error.message == "conditional write value mismatch" => {
            return retry_raced_hold_write(consecutive_misses)
        }
        Err(error) => return Err(error),
    };
    match verify_hold_write(&observed, baseline, target, consecutive_misses) {
        Ok(verified) => Ok(verified),
        Err(error) if error.message == "hold write verification found an unknown TValue" => {
            retry_raced_hold_write(consecutive_misses)
        }
        Err(error) => Err(error),
    }
}

fn retry_raced_hold_write(consecutive_misses: &mut u32) -> Result<bool, ProbeError> {
    *consecutive_misses = consecutive_misses.saturating_add(1);
    if *consecutive_misses >= MAX_CONSECUTIVE_WRITE_VERIFICATION_MISSES {
        Err(ProbeError::read(
            "hold write raced external speed changes after 3 attempts",
        ))
    } else {
        Ok(false)
    }
}

fn recovery_mismatch(j: &Journal, c: &luajit::Candidate) -> Option<&'static str> {
    if c.player != j.player {
        Some("journal player changed; retained")
    } else if j.legacy && (c.table != j.table || c.speed_addr != j.address) {
        Some("legacy journal address changed; retained")
    } else if c.speed_raw != j.old && c.speed_raw != j.new {
        Some("journal speed value changed; retained")
    } else {
        None
    }
}

fn journal_for_candidate(
    j: &Journal,
    c: &luajit::Candidate,
    target: f64,
) -> Result<Journal, ProbeError> {
    if c.player != j.player || (j.legacy && (c.table != j.table || c.speed_addr != j.address)) {
        return Err(ProbeError::expired("player binding changed"));
    }
    let (old, new) = if c.speed_raw == j.new {
        (j.old, j.new)
    } else {
        let new = luajit::speed_encode(target, &c.speed_raw)?;
        if new == c.speed_raw {
            return Err(ProbeError::read(
                "external speed already equals target; baseline is unknown",
            ));
        }
        (c.speed_raw, new)
    };
    Ok(Journal {
        table: c.table,
        address: c.speed_addr,
        old,
        new,
        ..j.clone()
    })
}

fn should_fallback_to_full_discovery(kind: ErrorKind) -> bool {
    !matches!(kind, ErrorKind::RootDenied | ErrorKind::RetryableMemoryRead)
}

fn candidate(j: &Journal, package: &str) -> Result<luajit::Candidate, ProbeError> {
    let id = identity(j);
    if !procfs::identity_matches(&id, package)? {
        return Err(ProbeError::expired("journal process changed; retained"));
    }
    let maps = procfs::read_anonymous_rw_private_maps(id.pid)?;
    let mut reader = memory::MemoryReader::open(id.pid, maps)?;
    match luajit::refresh(&mut reader, j.table) {
        Ok(candidate) => Ok(candidate),
        Err(error) if should_fallback_to_full_discovery(error.kind) => {
            reader.clear_cache();
            luajit::discover(&mut reader)
        }
        Err(error) => Err(error),
    }
}

struct CandidateLookup {
    result: Result<luajit::Candidate, ProbeError>,
    measurements: Vec<(luajit::RefreshPath, Duration)>,
    fallback: Option<(Duration, luajit::RefreshPath, luajit::CacheFailureReason)>,
}

fn hold_candidate(
    reader: &mut memory::MemoryReader,
    active: Option<&Journal>,
    field_cache: &mut Option<luajit::FieldCache>,
) -> CandidateLookup {
    let Some(journal) = active else {
        let started = Instant::now();
        let result = luajit::discover_seeded(reader).map(|(candidate, cache)| {
            *field_cache = cache;
            candidate
        });
        return CandidateLookup {
            result,
            measurements: vec![(luajit::RefreshPath::FullDiscovery, started.elapsed())],
            fallback: None,
        };
    };
    let had_cache = field_cache.is_some();
    let refresh_started = Instant::now();
    match luajit::refresh_cached(reader, journal.table, field_cache) {
        Ok((candidate, path, reason)) => {
            let elapsed = refresh_started.elapsed();
            CandidateLookup {
                result: Ok(candidate),
                measurements: vec![(path, elapsed)],
                fallback: reason.map(|reason| (elapsed, path, reason)),
            }
        }
        Err((error, reason)) if should_fallback_to_full_discovery(error.kind) => {
            reader.clear_cache();
            match luajit::refresh_cached(reader, journal.table, field_cache) {
                Ok((candidate, _, _)) => {
                    let elapsed = refresh_started.elapsed();
                    return CandidateLookup {
                        result: Ok(candidate),
                        measurements: vec![(luajit::RefreshPath::KnownTableScan, elapsed)],
                        fallback: if had_cache {
                            reason.map(|reason| {
                                (elapsed, luajit::RefreshPath::KnownTableScan, reason)
                            })
                        } else {
                            None
                        },
                    };
                }
                Err((error, _)) if !should_fallback_to_full_discovery(error.kind) => {
                    let elapsed = refresh_started.elapsed();
                    return CandidateLookup {
                        result: Err(error),
                        measurements: vec![(luajit::RefreshPath::KnownTableScan, elapsed)],
                        fallback: if had_cache {
                            reason.map(|reason| {
                                (elapsed, luajit::RefreshPath::KnownTableScan, reason)
                            })
                        } else {
                            None
                        },
                    };
                }
                Err(_) => {}
            }
            let known_elapsed = refresh_started.elapsed();
            reader.clear_cache();
            let discovery_started = Instant::now();
            let result = luajit::discover_seeded(reader).map(|(candidate, cache)| {
                *field_cache = cache;
                candidate
            });
            let discovery_elapsed = discovery_started.elapsed();
            let fallback = if had_cache {
                reason.map(|reason| {
                    (
                        known_elapsed.saturating_add(discovery_elapsed),
                        luajit::RefreshPath::FullDiscovery,
                        reason,
                    )
                })
            } else {
                None
            };
            let _ = error;
            CandidateLookup {
                result,
                measurements: vec![
                    (luajit::RefreshPath::KnownTableScan, known_elapsed),
                    (luajit::RefreshPath::FullDiscovery, discovery_elapsed),
                ],
                fallback,
            }
        }
        Err((error, reason)) => {
            let elapsed = refresh_started.elapsed();
            CandidateLookup {
                result: Err(error),
                measurements: vec![(luajit::RefreshPath::KnownTableScan, elapsed)],
                fallback: if had_cache {
                    reason.map(|reason| (elapsed, luajit::RefreshPath::KnownTableScan, reason))
                } else {
                    None
                },
            }
        }
    }
}

fn recovery_candidate(
    j: &Journal,
    package: &str,
    rx: &mpsc::Receiver<()>,
) -> Result<luajit::Candidate, ProbeError> {
    for attempt in 0..5 {
        match candidate(j, package) {
            Ok(c) => {
                if !procfs::identity_matches(&identity(j), package)? {
                    return Err(ProbeError::expired("journal process changed; retained"));
                }
                if let Some(reason) = recovery_mismatch(j, &c) {
                    return Err(ProbeError::read(reason));
                }
                return Ok(c);
            }
            Err(error) if error.message == "role candidate not found" && attempt < 4 => {
                state("LOCATING", "waiting_for_local_player");
                match rx.recv_timeout(Duration::from_millis(100)) {
                    Ok(()) => {
                        return Err(ProbeError::read("recovery stopped; journal retained"));
                    }
                    Err(mpsc::RecvTimeoutError::Timeout) => {}
                    Err(mpsc::RecvTimeoutError::Disconnected) => {
                        // A disconnected stop channel is not a stop request, but it must not
                        // turn the bounded recovery retries into a hot loop.
                        std::thread::sleep(Duration::from_millis(100));
                    }
                }
            }
            Err(error) => return Err(error),
        }
    }
    Err(ProbeError::read(
        "recovery candidate unstable; journal retained",
    ))
}

#[cfg(test)]
fn recovery_stop_requested(wait: Result<(), mpsc::RecvTimeoutError>) -> bool {
    matches!(wait, Ok(()))
}

// The table address is journaled separately from the speed TValue node.

fn recover(
    path: &Path,
    j: &Journal,
    package: &str,
    rx: &mpsc::Receiver<()>,
) -> Result<(), ProbeError> {
    let id = identity(j);
    let mut current_journal = j.clone();
    let c = recovery_candidate(&current_journal, package, rx)?;
    if !procfs::identity_matches(&id, package)? {
        return Err(ProbeError::expired("journal process changed; retained"));
    }
    if let Some(reason) = recovery_mismatch(j, &c) {
        return Err(ProbeError::read(reason));
    }
    if c.table != j.table || c.speed_addr != j.address {
        if c.speed_raw != j.old && c.speed_raw != j.new {
            return Err(ProbeError::read("journal value diverged; retained"));
        }
        current_journal.table = c.table;
        current_journal.address = c.speed_addr;
        save(path, &current_journal, false)?;
    }
    let verified = loop {
        let mut stable = None;
        for _ in 0..5 {
            let c = recovery_candidate(&current_journal, package, rx)?;
            if !procfs::identity_matches(&id, package)? {
                return Err(ProbeError::expired(
                    "journal process changed during recovery; retained",
                ));
            }
            if let Some(reason) = recovery_mismatch(j, &c) {
                return Err(ProbeError::read(reason));
            }
            if c.table == current_journal.table && c.speed_addr == current_journal.address {
                stable = Some(c);
                break;
            }
            current_journal.table = c.table;
            current_journal.address = c.speed_addr;
            save(path, &current_journal, false)?;
        }
        if let Some(c) = stable {
            break c;
        }
        return Err(ProbeError::read(
            "recovery binding unstable; journal retained",
        ));
    };
    if verified.speed_raw == j.new {
        if checked_write(&id, package, current_journal.address, &j.new, &j.old)? != j.old {
            return Err(ProbeError::read("restore write readback mismatch"));
        }
    }
    fs::remove_file(path).map_err(|e| ProbeError::from_io("remove recovered journal", e))
}

fn read_stop_command<R: io::BufRead>(reader: R) -> bool {
    for line in reader.lines() {
        match line {
            Ok(value) if value.trim() == "stop" => return true,
            Ok(_) => continue,
            Err(_) => return false,
        }
    }
    false
}

fn hold_stop_requested(result: Result<(), mpsc::TryRecvError>) -> bool {
    matches!(result, Ok(()) | Err(mpsc::TryRecvError::Disconnected))
}

fn run(package: String, target: f64, dir: PathBuf, recover_only: bool) -> Result<(), ProbeError> {
    fs::create_dir_all(&dir).map_err(|e| ProbeError::from_io("create state directory", e))?;
    let journal = dir.join("mount_hold.journal");
    let mut journal_restored = false;
    let (tx, rx) = mpsc::channel();
    std::thread::spawn(move || {
        let stdin = io::stdin();
        if read_stop_command(stdin.lock()) {
            let _ = tx.send(());
        }
    });
    if journal.exists() {
        state("BLOCKED", "recovering_old_journal");
        let old = load(&journal)?;
        let saved_identity = identity(&old);
        let status = match procfs::identity_matches(&saved_identity, &package)? {
            true => SavedProcessStatus::MatchesSelectedPackage,
            false => classify_saved_process(false, procfs::identity_exists(&saved_identity)?),
        };
        match status {
            SavedProcessStatus::MatchesSelectedPackage => {
                recover(&journal, &old, &package, &rx)?;
                journal_restored = true;
                state("RECOVERED", "old_journal_restored");
            }
            SavedProcessStatus::AliveForDifferentPackage => {
                return Err(ProbeError::expired(
                    "journal process is alive for a different package; retained",
                ));
            }
            SavedProcessStatus::GoneOrReplaced => {
                archive_expired_journal(&journal)?;
                state("ARCHIVED", "expired_journal_preserved");
            }
        }
        if recover_only {
            if journal_restored {
                state("RECOVERED", "old_journal_restored");
            }
            return Ok(());
        }
    } else if recover_only {
        state("RECOVERED", "stopped_without_changes");
        return Ok(());
    }
    license_lease::require_module_lease(&dir).map_err(|message| ProbeError::read(&message))?;
    state("STARTING", "waiting_for_exact_package");
    let mut active: Option<Journal> = None;
    let mut field_cache = None;
    let mut refresh_telemetry = RefreshTelemetry::new();
    let mut awaiting_package = false;
    let mut awaiting_local_player = false;
    let mut missing_candidate_retries = 0u32;
    let mut transient_memory_read_retries = 0usize;
    let mut consecutive_write_verification_misses = 0u32;
    let mut holding_reported = false;
    let result = (|| {
        loop {
            if hold_stop_requested(rx.try_recv()) {
                break;
            }
            if license_lease::require_module_lease(&dir).is_err() {
                state("STOPPING", "activation_lease_expired");
                break;
            }
            let id = match procfs::find_process(&package)? {
                Some(id) => id,
                None => {
                    if active.is_some() {
                        return Err(ProbeError::expired("bound process disappeared"));
                    }
                    awaiting_local_player = false;
                    missing_candidate_retries = 0;
                    transient_memory_read_retries = 0;
                    if !awaiting_package {
                        state("LOCATING", "exact_package_not_running");
                        awaiting_package = true;
                    }
                    std::thread::sleep(Duration::from_millis(100));
                    continue;
                }
            };
            awaiting_package = false;
            if let Some(j) = active.as_ref() {
                if id != identity(j) {
                    return Err(ProbeError::expired("bound process changed"));
                }
            }
            let maps = procfs::read_anonymous_rw_private_maps(id.pid)?;
            let mut reader = memory::MemoryReader::open(id.pid, maps)?;
            let lookup = hold_candidate(&mut reader, active.as_ref(), &mut field_cache);
            refresh_telemetry.record_measurements(&lookup.measurements);
            refresh_telemetry.record_lookup(lookup.fallback, lookup.result.is_ok());
            let c = match lookup.result {
                Ok(candidate) => {
                    awaiting_local_player = false;
                    missing_candidate_retries = 0;
                    transient_memory_read_retries = 0;
                    candidate
                }
                Err(error)
                    if error.kind == ErrorKind::RetryableMemoryRead
                        && transient_memory_read_retries < MEMORY_READ_RETRY_DELAYS_MS.len() =>
                {
                    holding_reported = false;
                    let delay_ms = MEMORY_READ_RETRY_DELAYS_MS[transient_memory_read_retries];
                    transient_memory_read_retries += 1;
                    state("LOCATING", "retrying_transient_memory_read");
                    match rx.recv_timeout(Duration::from_millis(delay_ms)) {
                        Ok(()) | Err(mpsc::RecvTimeoutError::Disconnected) => break,
                        Err(mpsc::RecvTimeoutError::Timeout) => continue,
                    }
                }
                Err(error) if error.message == "role candidate not found" => {
                    transient_memory_read_retries = 0;
                    if !awaiting_local_player {
                        state("LOCATING", "waiting_for_local_player");
                        awaiting_local_player = true;
                    }
                    holding_reported = false;
                    let delay_ms = missing_candidate_retry_delay_ms(missing_candidate_retries);
                    missing_candidate_retries = missing_candidate_retries.saturating_add(1);
                    match rx.recv_timeout(Duration::from_millis(delay_ms)) {
                        Ok(()) | Err(mpsc::RecvTimeoutError::Disconnected) => break,
                        Err(mpsc::RecvTimeoutError::Timeout) => continue,
                    }
                }
                Err(error) => return Err(error),
            };
            if let Some(j) = active.as_mut() {
                if !procfs::identity_matches(&id, &package)? {
                    return Err(ProbeError::expired("process identity changed"));
                }
                if c.player != j.player
                    || (j.legacy && (c.table != j.table || c.speed_addr != j.address))
                {
                    return Err(ProbeError::expired("player binding changed"));
                }
                if c.table != j.table
                    || c.speed_addr != j.address
                    || (c.speed_raw != j.old && c.speed_raw != j.new)
                {
                    let next = journal_for_candidate(j, &c, target)?;
                    save(&journal, &next, false)?;
                    *j = next;
                    if should_write_target(&c.speed_raw, &j.old, &j.new)? {
                        if write_hold_target(
                            &id,
                            &package,
                            j.address,
                            &j.old,
                            &j.new,
                            &mut consecutive_write_verification_misses,
                        )? {
                            if !holding_reported {
                                state("ACTIVE", "holding_speed");
                                holding_reported = true;
                            }
                        } else if !holding_reported {
                            state("LOCATING", "retrying_hold_write_verification");
                        }
                    } else {
                        consecutive_write_verification_misses = 0;
                        if !holding_reported {
                            state("ACTIVE", "holding_speed");
                            holding_reported = true;
                        }
                    }
                } else if should_write_target(&c.speed_raw, &j.old, &j.new)? {
                    if write_hold_target(
                        &id,
                        &package,
                        j.address,
                        &j.old,
                        &j.new,
                        &mut consecutive_write_verification_misses,
                    )? {
                        if !holding_reported {
                            state("ACTIVE", "holding_speed");
                            holding_reported = true;
                        }
                    } else if !holding_reported {
                        state("LOCATING", "retrying_hold_write_verification");
                    }
                } else {
                    consecutive_write_verification_misses = 0;
                    if !holding_reported {
                        state("ACTIVE", "holding_speed");
                        holding_reported = true;
                    }
                }
            } else {
                let new = luajit::speed_encode(target, &c.speed_raw)?;
                if new == c.speed_raw {
                    return Err(ProbeError::read(
                        "current speed already equals target; normal baseline cannot be inferred",
                    ));
                }
                let j = Journal {
                    pid: id.pid,
                    ticks: id.start_ticks,
                    exe: id.executable.clone(),
                    player: c.player,
                    legacy: false,
                    table: c.table,
                    address: c.speed_addr,
                    old: c.speed_raw,
                    new,
                };
                save(&journal, &j, true)?;
                active = Some(j);
                let j = active.as_ref().unwrap();
                if write_hold_target(
                    &id,
                    &package,
                    j.address,
                    &j.old,
                    &j.new,
                    &mut consecutive_write_verification_misses,
                )? {
                    state("ACTIVE", "holding_speed");
                    holding_reported = true;
                } else {
                    state("LOCATING", "retrying_hold_write_verification");
                }
            }
            std::thread::sleep(Duration::from_millis(100));
        }
        Ok(())
    })();
    let active_error = result.as_ref().err().map(|error| error.message.clone());
    refresh_telemetry.finish();
    state("STOPPING", "attempting_safe_restore");
    if let Some(j) = active {
        match recover(&journal, &j, &package, &rx) {
            Ok(()) => state("RECOVERED", "speed_restored"),
            Err(error) => {
                state("BLOCKED", "journal_retained");
                let message = active_error.map_or_else(
                    || error.message.clone(),
                    |active| format!("active_{active}__recovery_{}", error.message),
                );
                return Err(ProbeError::new(error.kind, message));
            }
        }
    } else {
        state("RECOVERED", "stopped_without_changes");
    }
    result
}

fn report_error(error: ProbeError) {
    eprintln!("mount_hold_worker: {error}");
    state("ERROR", &format!("{:?}_{error}", error.kind));
}

fn main() {
    state("STARTING", "worker_start");
    match args() {
        Ok((package, speed, dir, recover_only)) => {
            if let Err(error) = run(package, speed, dir, recover_only) {
                report_error(error);
            }
        }
        Err(error) => report_error(error),
    }
}

#[cfg(test)]
mod tests {
    use super::{
        archive_journal_to, classify_saved_process, hold_stop_requested, journal_for_candidate,
        load, missing_candidate_retry_delay_ms, parse_recover_only, read_stop_command,
        recovery_mismatch, recovery_stop_requested, retry_raced_hold_write, save,
        should_fallback_to_full_discovery, should_write_target, valid_state_dir, verify_hold_write,
        ErrorKind, Journal, SavedProcessStatus,
    };
    use std::{io, sync::mpsc};

    #[test]
    fn recover_only_is_strict_and_distinguishes_start_from_recovery() {
        assert_eq!(parse_recover_only("true").unwrap(), true);
        assert_eq!(parse_recover_only("false").unwrap(), false);
        assert!(parse_recover_only("1").is_err());
        assert!(parse_recover_only("TRUE").is_err());
    }

    #[test]
    fn missing_candidate_retry_delay_starts_fast_and_saturates() {
        assert_eq!(
            (0..5)
                .map(missing_candidate_retry_delay_ms)
                .collect::<Vec<_>>(),
            vec![500, 1_000, 2_000, 2_000, 2_000]
        );
    }

    #[test]
    fn discovery_fallback_uses_error_kind_not_message_text() {
        assert!(should_fallback_to_full_discovery(ErrorKind::ReadError));
        assert!(should_fallback_to_full_discovery(ErrorKind::Expired));
        assert!(!should_fallback_to_full_discovery(ErrorKind::RootDenied));
        assert!(!should_fallback_to_full_discovery(
            ErrorKind::RetryableMemoryRead
        ));
    }

    #[test]
    fn recovery_only_stops_after_an_explicit_request() {
        assert!(recovery_stop_requested(Ok(())));
        assert!(!recovery_stop_requested(Err(
            std::sync::mpsc::RecvTimeoutError::Timeout
        )));
        assert!(!recovery_stop_requested(Err(
            std::sync::mpsc::RecvTimeoutError::Disconnected
        )));
    }

    #[test]
    fn input_eof_is_not_an_explicit_stop_but_stops_holding() {
        assert!(!read_stop_command(io::Cursor::new(b"")));
        assert!(!read_stop_command(io::Cursor::new(b"ignored\n")));
        assert!(read_stop_command(io::Cursor::new(b"ignored\nstop\n")));
        assert!(hold_stop_requested(Ok(())));
        assert!(hold_stop_requested(Err(mpsc::TryRecvError::Disconnected)));
        assert!(!hold_stop_requested(Err(mpsc::TryRecvError::Empty)));
        assert!(!recovery_stop_requested(Err(
            mpsc::RecvTimeoutError::Disconnected
        )));
    }

    #[test]
    fn state_directory_accepts_only_the_suite_mount_hold_paths() {
        assert!(valid_state_dir(Path::new(
            "/data/user/0/com.xnas.wpe.suite/files/modules/mounthold"
        )));
        assert!(valid_state_dir(Path::new(
            "/data/data/com.xnas.wpe.suite/files/modules/mounthold"
        )));
        for rejected in [
            "/data/user/0/com.xnas.wpe.suite/files/modules/mounthold/child",
            "/data/user/0/com.xnas.wpe.suite/files/modules/mounthold/../battleloop",
            "/data/user/0/com.xnas.wpe.suite/files",
            "/data/user/0/com.xnas.wpe.battleskip/files/modules/mounthold",
            "relative/modules/mounthold",
        ] {
            assert!(!valid_state_dir(Path::new(rejected)), "accepted {rejected}");
        }
    }
    use crate::luajit::Candidate;
    use crate::{memory::MemoryReader, model::MemoryRange};
    use std::{
        fs,
        fs::OpenOptions,
        os::unix::fs::FileExt,
        path::{Path, PathBuf},
        time::{SystemTime, UNIX_EPOCH},
    };

    fn synthetic_memory() -> (PathBuf, MemoryReader, u64) {
        const SIZE: usize = 4 * 1024 * 1024;
        const TABLE: u64 = 0x1000;
        const NODES: u64 = 0x2000;
        const KEYS: u64 = 0x3000;
        let dir = temp_dir();
        let path = dir.join("memory.bin");
        fs::write(&path, vec![0u8; SIZE]).unwrap();
        let file = OpenOptions::new()
            .read(true)
            .write(true)
            .open(&path)
            .unwrap();
        let names = [
            "m_PlayerId",
            "m_IsLocalPlayer",
            "m_RoleMoveSpeed",
            "m_Gx",
            "m_Gy",
        ];
        let values = [
            ((0xffff_fff2u64) << 47 | 123).to_le_bytes(),
            ((0xffff_fffdu64) << 47).to_le_bytes(),
            1200.0f64.to_bits().to_le_bytes(),
            1.0f64.to_bits().to_le_bytes(),
            2.0f64.to_bits().to_le_bytes(),
        ];
        let mut header = [0u8; 64];
        header[9] = 11;
        header[40..48].copy_from_slice(&NODES.to_le_bytes());
        header[52..56].copy_from_slice(&7u32.to_le_bytes());
        file.write_at(&header, TABLE).unwrap();
        for (index, name) in names.iter().enumerate() {
            let key = KEYS + index as u64 * 0x100;
            let mut gc = [0u8; 24];
            gc[9] = 4;
            gc[12..16].copy_from_slice(&(index as u32).to_le_bytes());
            gc[20..24].copy_from_slice(&(name.len() as u32).to_le_bytes());
            file.write_at(&gc, key).unwrap();
            file.write_at(name.as_bytes(), key + 24).unwrap();
            let node = NODES + index as u64 * 24;
            let key_tvalue = ((0xffff_fffbu64) << 47) | key;
            let mut bytes = [0u8; 24];
            bytes[..8].copy_from_slice(&values[index]);
            bytes[8..16].copy_from_slice(&key_tvalue.to_le_bytes());
            file.write_at(&bytes, node).unwrap();
        }
        let reader = MemoryReader::from_test_file(
            file,
            vec![MemoryRange {
                start: 0,
                end: SIZE as u64,
            }],
        );
        (dir, reader, NODES + 48)
    }

    fn local_value(is_local: bool) -> [u8; 8] {
        ((if is_local {
            0xffff_fffdu64
        } else {
            0xffff_fffeu64
        }) << 47)
            .to_le_bytes()
    }

    fn write_local(path: &PathBuf, is_local: bool) {
        OpenOptions::new()
            .write(true)
            .open(path)
            .unwrap()
            .write_at(&local_value(is_local), 0x2000 + 24)
            .unwrap();
    }

    fn synthetic_journal(candidate: &Candidate) -> Journal {
        Journal {
            pid: 1,
            ticks: 2,
            exe: String::new(),
            player: candidate.player,
            legacy: false,
            table: candidate.table,
            address: candidate.speed_addr,
            old: candidate.speed_raw,
            new: crate::luajit::speed_encode(5000.0, &candidate.speed_raw).unwrap(),
        }
    }

    #[test]
    fn known_table_retry_uses_fresh_pages_and_persistent_failure_discovers() {
        let (dir, mut reader, _) = synthetic_memory();
        let (seed, cache) = crate::luajit::discover_seeded(&mut reader).unwrap();
        let mut field_cache = cache;
        let journal = synthetic_journal(&seed);

        let path = dir.join("memory.bin");
        reader.clear_cache();
        write_local(&path, false);
        assert_eq!(reader.read(0x2000 + 24, 8).unwrap(), local_value(false));
        write_local(&path, true);

        let lookup = super::hold_candidate(&mut reader, Some(&journal), &mut field_cache);
        assert!(lookup.result.is_ok());
        assert_eq!(lookup.measurements.len(), 1);
        assert_eq!(
            lookup.measurements[0].0,
            crate::luajit::RefreshPath::KnownTableScan
        );
        assert_eq!(
            lookup.fallback.map(|(_, path, reason)| (path, reason)),
            Some((
                crate::luajit::RefreshPath::KnownTableScan,
                crate::luajit::CacheFailureReason::LocalPlayerBindingChanged,
            ))
        );
        assert!(field_cache.is_some());

        let (dir2, mut stale_reader, _) = synthetic_memory();
        let (seed2, cache2) = crate::luajit::discover_seeded(&mut stale_reader).unwrap();
        let mut stale_cache = cache2;
        let journal2 = synthetic_journal(&seed2);
        let path2 = dir2.join("memory.bin");
        stale_reader.clear_cache();
        write_local(&path2, false);
        let _ = stale_reader.read(0x2000 + 24, 8).unwrap();
        let lookup = super::hold_candidate(&mut stale_reader, Some(&journal2), &mut stale_cache);
        assert!(lookup.result.is_err());
        assert_eq!(
            lookup
                .measurements
                .iter()
                .map(|(path, _)| *path)
                .collect::<Vec<_>>(),
            vec![
                crate::luajit::RefreshPath::KnownTableScan,
                crate::luajit::RefreshPath::FullDiscovery,
            ]
        );
        assert_eq!(
            lookup.fallback.map(|(_, path, reason)| (path, reason)),
            Some((
                crate::luajit::RefreshPath::FullDiscovery,
                crate::luajit::CacheFailureReason::LocalPlayerBindingChanged,
            ))
        );
        assert!(stale_cache.is_none());
        fs::remove_dir_all(dir).unwrap();
        fs::remove_dir_all(dir2).unwrap();
    }

    fn temp_dir() -> PathBuf {
        let stamp = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        let dir =
            std::env::temp_dir().join(format!("mount-hold-test-{}-{stamp}", std::process::id()));
        fs::create_dir(&dir).unwrap();
        dir
    }

    #[test]
    fn saved_process_is_archivable_only_when_exact_identity_is_gone() {
        assert_eq!(
            classify_saved_process(true, false),
            SavedProcessStatus::MatchesSelectedPackage
        );
        assert_eq!(
            classify_saved_process(false, true),
            SavedProcessStatus::AliveForDifferentPackage
        );
        assert_eq!(
            classify_saved_process(false, false),
            SavedProcessStatus::GoneOrReplaced
        );
    }

    #[test]
    fn expired_journal_archive_preserves_bytes_before_removing_active_name() {
        let dir = temp_dir();
        let active = dir.join("mount_hold.journal");
        let archive = dir.join("mount_hold.journal.expired.test");
        let contents = b"opaque journal bytes\nwith exact contents\0";
        fs::write(&active, contents).unwrap();

        archive_journal_to(&active, &archive).unwrap();

        assert!(!active.exists());
        assert_eq!(fs::read(&archive).unwrap(), contents);
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn expired_journal_archive_collision_preserves_both_files() {
        let dir = temp_dir();
        let active = dir.join("mount_hold.journal");
        let archive = dir.join("mount_hold.journal.expired.test");
        let contents = b"active journal";
        let existing = b"existing archive";
        fs::write(&active, contents).unwrap();
        fs::write(&archive, existing).unwrap();

        assert!(archive_journal_to(&active, &archive).is_err());

        assert_eq!(fs::read(&active).unwrap(), contents);
        assert_eq!(fs::read(&archive).unwrap(), existing);
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn v2_journal_roundtrips_and_rejects_malformed_legacy_flag() {
        let dir = temp_dir();
        let path = dir.join("journal");
        let journal = Journal {
            pid: 1,
            ticks: 2,
            exe: "/game".into(),
            player: 3,
            legacy: false,
            table: 5,
            address: 6,
            old: [1; 8],
            new: [2; 8],
        };
        save(&path, &journal, true).unwrap();
        let loaded = load(&path).unwrap();
        assert!(!loaded.legacy);
        assert_eq!(
            (loaded.pid, loaded.player, loaded.table, loaded.address),
            (1, 3, 5, 6)
        );
        assert_eq!((loaded.old, loaded.new), ([1; 8], [2; 8]));
        let text = fs::read_to_string(&path)
            .unwrap()
            .replace("\nfalse", "\ntrue");
        fs::write(&path, text).unwrap();
        assert_eq!(
            load(&path).err().unwrap().message,
            "bad journal legacy flag"
        );
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn v1_journal_validates_mount_but_legacy_rebind_requires_same_address() {
        let dir = temp_dir();
        let path = dir.join("journal");
        fs::write(
            &path,
            "mount-hold-v1\n1\n2\n2f67616d65\n3\n4\n5\n6\n0101010101010101\n0202020202020202\n",
        )
        .unwrap();
        let journal = load(&path).unwrap();
        assert!(journal.legacy);
        let candidate = |table, speed_addr| Candidate {
            table,
            player: 3,
            speed_addr,
            speed_raw: [1; 8],
        };
        assert_eq!(recovery_mismatch(&journal, &candidate(5, 6)), None);
        assert_eq!(
            recovery_mismatch(&journal, &candidate(50, 60)),
            Some("legacy journal address changed; retained")
        );
        fs::write(
            &path,
            "mount-hold-v1\n1\n2\n2f67616d65\n3\n0\n5\n6\n0101010101010101\n0202020202020202\n",
        )
        .unwrap();
        assert_eq!(load(&path).err().unwrap().message, "bad journal mount");
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn recovery_rebinds_only_with_same_player_and_known_speed() {
        let journal = Journal {
            pid: 1,
            ticks: 2,
            exe: String::new(),
            player: 3,
            legacy: false,
            table: 5,
            address: 6,
            old: [1; 8],
            new: [2; 8],
        };
        let candidate = |player, speed_raw| Candidate {
            table: 50,
            player,
            speed_addr: 60,
            speed_raw,
        };

        assert_eq!(
            recovery_mismatch(&journal, &candidate(3, journal.old)),
            None
        );
        assert_eq!(
            recovery_mismatch(&journal, &candidate(3, journal.new)),
            None
        );
        assert_eq!(
            recovery_mismatch(&journal, &candidate(9, journal.old)),
            Some("journal player changed; retained")
        );
        assert_eq!(
            recovery_mismatch(&journal, &candidate(3, [3; 8])),
            Some("journal speed value changed; retained")
        );
    }

    #[test]
    fn external_speed_updates_refresh_only_the_bound_journal_baseline() {
        let old = 1200.0f64.to_bits().to_le_bytes();
        let target = 5000.0;
        let journal = Journal {
            pid: 1,
            ticks: 2,
            exe: String::new(),
            player: 3,
            legacy: false,
            table: 5,
            address: 6,
            old,
            new: crate::luajit::speed_encode(target, &old).unwrap(),
        };
        let current = 1400.0f64.to_bits().to_le_bytes();
        let candidate = Candidate {
            table: 50,
            player: 3,
            speed_addr: 60,
            speed_raw: current,
        };

        let next = journal_for_candidate(&journal, &candidate, target).unwrap();
        assert_eq!(next.old, current);
        assert_eq!(
            next.new,
            crate::luajit::speed_encode(target, &current).unwrap()
        );
        assert_eq!((next.player, next.table, next.address), (3, 50, 60));

        let other_role_state = Candidate {
            ..candidate.clone()
        };
        assert!(journal_for_candidate(&journal, &other_role_state, target).is_ok());
        let invalid = Candidate {
            speed_raw: [0xff; 8],
            ..candidate
        };
        assert!(journal_for_candidate(&journal, &invalid, target).is_err());
    }

    #[test]
    fn concurrent_hold_write_races_are_bounded() {
        let mut misses = 0;
        assert!(!retry_raced_hold_write(&mut misses).unwrap());
        assert!(!retry_raced_hold_write(&mut misses).unwrap());
        assert!(retry_raced_hold_write(&mut misses).is_err());
    }

    #[test]
    fn hold_retries_only_known_baseline_and_stops_after_three_misses() {
        let baseline = [1; 8];
        let target = [2; 8];
        assert!(should_write_target(&baseline, &baseline, &target).unwrap());
        assert!(!should_write_target(&target, &baseline, &target).unwrap());
        assert!(should_write_target(&[3; 8], &baseline, &target).is_err());

        let mut misses = 0;
        assert!(!verify_hold_write(&baseline, &baseline, &target, &mut misses).unwrap());
        assert!(!verify_hold_write(&baseline, &baseline, &target, &mut misses).unwrap());
        assert!(verify_hold_write(&baseline, &baseline, &target, &mut misses).is_err());
        assert!(verify_hold_write(&target, &baseline, &target, &mut misses).unwrap());
        assert_eq!(misses, 0);
    }
}
