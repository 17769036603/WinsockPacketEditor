#[path = "../../../frida_diagnostics.rs"]
mod frida_diagnostics;

#[path = "../../../license_lease.rs"]
mod license_lease;

use std::{
    ffi::{c_char, c_int, c_void, CStr, CString},
    fs,
    io::{self, ErrorKind, Read, Seek, SeekFrom, Write},
    os::fd::AsRawFd,
    os::unix::fs::MetadataExt,
    os::unix::process::CommandExt,
    path::{Path, PathBuf},
    process::{Command, ExitCode, Stdio},
    ptr,
    sync::{mpsc, Mutex, OnceLock},
    thread,
    time::{Duration, SystemTime, UNIX_EPOCH},
};

mod recovery;

#[repr(C)]
struct GError {
    domain: u32,
    code: c_int,
    message: *mut c_char,
}
#[repr(C)]
struct O(c_void);
type Manager = O;
type DeviceList = O;
type Device = O;
type Session = O;
type Script = O;
type ScriptOptions = O;
#[link(name = "frida-core", kind = "static")]
unsafe extern "C" {
    fn frida_init();
    fn frida_deinit();
    fn frida_version_string() -> *const c_char;
    fn frida_device_manager_new() -> *mut Manager;
    fn frida_device_manager_enumerate_devices_sync(
        _: *mut Manager,
        _: *mut c_void,
        _: *mut *mut GError,
    ) -> *mut DeviceList;
    fn frida_device_list_size(_: *mut DeviceList) -> c_int;
    fn frida_device_list_get(_: *mut DeviceList, _: c_int) -> *mut Device;
    fn frida_device_get_dtype(_: *mut Device) -> c_int;
    fn frida_device_attach_sync(
        _: *mut Device,
        _: u32,
        _: *mut c_void,
        _: *mut c_void,
        _: *mut *mut GError,
    ) -> *mut Session;
    fn frida_session_create_script_sync(
        _: *mut Session,
        _: *const c_char,
        _: *mut ScriptOptions,
        _: *mut c_void,
        _: *mut *mut GError,
    ) -> *mut Script;
    fn frida_script_options_new() -> *mut ScriptOptions;
    fn frida_script_options_set_name(_: *mut ScriptOptions, _: *const c_char);
    fn frida_script_options_set_runtime(_: *mut ScriptOptions, _: c_int);
    fn frida_script_load_sync(_: *mut Script, _: *mut c_void, _: *mut *mut GError);
    fn frida_script_post(_: *mut Script, _: *const c_char, _: *mut c_void);
    fn frida_script_unload_sync(_: *mut Script, _: *mut c_void, _: *mut *mut GError);
    fn frida_session_detach_sync(_: *mut Session, _: *mut c_void, _: *mut *mut GError);
    fn frida_device_manager_close_sync(_: *mut Manager, _: *mut c_void, _: *mut *mut GError);
    fn frida_unref(_: *mut c_void);
    fn _frida_g_signal_connect_data(
        _: *mut c_void,
        _: *const c_char,
        _: Option<unsafe extern "C" fn()>,
        _: *mut c_void,
        _: Option<unsafe extern "C" fn(*mut c_void, *mut c_void)>,
        _: c_int,
    ) -> usize;
    fn _frida_g_main_context_iteration(_: *mut c_void, _: c_int) -> c_int;
    fn _frida_g_error_free(_: *mut GError);
}
unsafe extern "C" {
    fn flock(fd: c_int, op: c_int) -> c_int;
    fn fchown(fd: c_int, owner: u32, group: u32) -> c_int;
    fn fchmod(fd: c_int, mode: u32) -> c_int;
    fn setsid() -> c_int;
}
static TX: OnceLock<Mutex<Option<mpsc::Sender<String>>>> = OnceLock::new();
unsafe extern "C" fn on_message(
    _: *mut Script,
    msg: *const c_char,
    _: *mut c_void,
    _: *mut c_void,
) {
    let _ = std::panic::catch_unwind(|| {
        if msg.is_null() {
            return;
        }
        let s = CStr::from_ptr(msg).to_string_lossy().into_owned();
        if let Some(tx) = TX.get_or_init(|| Mutex::new(None)).lock().unwrap().as_ref() {
            let _ = tx.send(s);
        }
    });
}
fn cstr(s: &str) -> Result<CString, String> {
    CString::new(s).map_err(|e| e.to_string())
}
unsafe fn err(e: *mut GError, where_: &str) -> Result<(), String> {
    if e.is_null() {
        return Ok(());
    }
    let s = if (*e).message.is_null() {
        "unknown Frida error".into()
    } else {
        CStr::from_ptr((*e).message).to_string_lossy().into_owned()
    };
    _frida_g_error_free(e);
    Err(format!("{where_}: {s}"))
}
fn json_escape(s: &str) -> String {
    s.replace('\\', "\\\\")
        .replace('"', "\\\"")
        .replace('\n', "\\n")
        .replace('\r', "\\r")
}
fn lua_quote(s: &str) -> String {
    format!(
        "'{}'",
        s.replace('\\', "\\\\")
            .replace('\'', "\\'")
            .replace('\n', "\\n")
            .replace('\r', "\\r")
    )
}
fn replace_atomic(path: &Path, data: &[u8], mode: u32, owner: Option<u32>) -> io::Result<()> {
    let tmp = path.with_extension(format!("tmp-{}", std::process::id()));
    {
        let mut f = fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&tmp)?;
        f.write_all(data)?;
        if let Some(uid) = owner {
            if unsafe { fchown(f.as_raw_fd(), uid, u32::MAX) } != 0 {
                return Err(io::Error::last_os_error());
            }
        }
        if unsafe { fchmod(f.as_raw_fd(), mode) } != 0 {
            return Err(io::Error::last_os_error());
        }
        f.sync_all()?;
    }
    fs::rename(&tmp, path)?;
    Ok(())
}
fn replace_atomic_durable(path: &Path, data: &[u8], mode: u32, owner: Option<u32>) -> io::Result<()> {
    let parent = path
        .parent()
        .ok_or_else(|| io::Error::new(ErrorKind::InvalidInput, "target has no parent directory"))?;
    let nonce = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos();
    let tmp = path.with_extension(format!("tmp-{}-{nonce}", std::process::id()));
    let result = (|| {
        let mut file = fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&tmp)?;
        file.write_all(data)?;
        if let Some(uid) = owner {
            if unsafe { fchown(file.as_raw_fd(), uid, u32::MAX) } != 0 {
                return Err(io::Error::last_os_error());
            }
        }
        if unsafe { fchmod(file.as_raw_fd(), mode) } != 0 {
            return Err(io::Error::last_os_error());
        }
        file.sync_all()?;
        drop(file);
        fs::rename(&tmp, path)?;
        fs::File::open(parent)?.sync_all()?;
        Ok(())
    })();
    if result.is_err() {
        let _ = fs::remove_file(&tmp);
    }
    result
}
fn create_owned(path: &Path, uid: u32, mode: u32) -> io::Result<()> {
    let f = fs::OpenOptions::new().write(true).create_new(true).open(path)?;
    if unsafe { fchown(f.as_raw_fd(), uid, u32::MAX) } != 0 {
        return Err(io::Error::last_os_error());
    }
    if unsafe { fchmod(f.as_raw_fd(), mode) } != 0 {
        return Err(io::Error::last_os_error());
    }
    f.sync_all()
}
fn status(dir: &Path, s: &str) {
    let p = dir.join("status");
    let owner = fs::metadata(dir).ok().map(|m| m.uid());
    let _ = replace_atomic(&p, s.as_bytes(), 0o644, owner);
}
fn write_recovery_status(dir: &Path, contents: &str) -> io::Result<()> {
    let owner = fs::metadata(dir).map(|m| m.uid())?;
    replace_atomic(&dir.join("recovery-status"), contents.as_bytes(), 0o644, Some(owner))
}
fn write_terminal_status(dir: &Path, contents: &str) -> io::Result<()> {
    let owner = fs::metadata(dir).map(|m| m.uid())?;
    replace_atomic(&dir.join("status"), contents.as_bytes(), 0o644, Some(owner))
}

/// 把游戏侧逐回合日志的尾部同步到应用私有目录，便于无需 root 直接查看。
fn sync_rounds_log(source: &Path, dir: &Path) -> io::Result<()> {
    const MAX: usize = 64 * 1024;
    let bytes = match fs::read(source) {
        Ok(bytes) => bytes,
        Err(error) if error.kind() == ErrorKind::NotFound => return Ok(()),
        Err(error) => return Err(error),
    };
    if bytes.is_empty() {
        return Ok(());
    }
    let start = bytes.len().saturating_sub(MAX);
    let owner = fs::metadata(dir).map(|m| m.uid())?;
    replace_atomic(&dir.join("rounds.log"), &bytes[start..], 0o644, Some(owner))
}
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
struct BattleOptions {
    skip_battle: bool,
    hide_battle: bool,
}

impl BattleOptions {
    fn any_enabled(self) -> bool {
        self.skip_battle || self.hide_battle
    }
}

/// Parse the one-file v1 startup snapshot. Unknown, repeated, absent, or invalid fields fail closed.
fn parse_battle_options(raw: &str) -> Result<BattleOptions, String> {
    let mut version = None;
    let mut skip_battle = None;
    let mut hide_battle = None;
    for line in raw.lines() {
        let (key, value) = line
            .split_once('=')
            .ok_or_else(|| "malformed field".to_owned())?;
        match key {
            "version" => {
                if version.is_some() {
                    return Err("duplicate version".into());
                }
                if value != "1" {
                    return Err("unsupported version".into());
                }
                version = Some(());
            }
            "skipBattle" => {
                if skip_battle.is_some() {
                    return Err("duplicate skipBattle".into());
                }
                skip_battle = Some(parse_mode_value(value, "skipBattle")?);
            }
            "hideBattle" => {
                if hide_battle.is_some() {
                    return Err("duplicate hideBattle".into());
                }
                hide_battle = Some(parse_mode_value(value, "hideBattle")?);
            }
            _ => return Err("unknown field".into()),
        }
    }
    if version.is_none() {
        return Err("missing version".into());
    }
    Ok(BattleOptions {
        skip_battle: skip_battle.ok_or_else(|| "missing skipBattle".to_owned())?,
        hide_battle: hide_battle.ok_or_else(|| "missing hideBattle".to_owned())?,
    })
}

fn parse_mode_value(value: &str, field: &str) -> Result<bool, String> {
    match value {
        "1" => Ok(true),
        "0" => Ok(false),
        _ => Err(format!("invalid {field} value")),
    }
}

fn read_battle_options(dir: &Path) -> Result<BattleOptions, String> {
    match fs::read_to_string(dir.join("battle-options")) {
        Ok(contents) => parse_battle_options(&contents)
            .map_err(|error| format!("battle_options_invalid: {error}")),
        Err(error) if error.kind() == ErrorKind::NotFound => {
            // Older app versions only persisted the visual setting. Keep their skip behavior.
            let hide_battle = fs::read_to_string(dir.join("hide-battle"))
                .map(|contents| contents.trim() == "1")
                .unwrap_or(false);
            Ok(BattleOptions {
                skip_battle: true,
                hide_battle,
            })
        }
        Err(error) => Err(format!("battle_options_read_failed: {error}")),
    }
}

/// A no-hook terminal result is safe only for a fresh invocation with no prior state to replace.
fn may_publish_no_hook_terminal(
    existing_status: Option<&str>,
    recovery_exists: bool,
) -> bool {
    existing_status.is_none() && !recovery_exists
}

fn status_with_modes(dir: &Path, contents: &str, options: BattleOptions) {
    let mut snapshot = contents.to_owned();
    if parse_result_field(contents, "skipBattleRequested=").is_none() {
        snapshot.push_str(if options.skip_battle {
            "skipBattleRequested=true\n"
        } else {
            "skipBattleRequested=false\n"
        });
    }
    if parse_result_field(contents, "hideBattleRequested=").is_none() {
        snapshot.push_str(if options.hide_battle {
            "hideBattleRequested=true\n"
        } else {
            "hideBattleRequested=false\n"
        });
    }
    status(dir, &snapshot);
}
/// Copy only the bounded tail of the visual log into the module's app-private directory.
fn sync_visual_log(source: &Path, dir: &Path) -> io::Result<()> {
    const MAX: u64 = 64 * 1024;
    let mut input = match fs::File::open(source) {
        Ok(input) => input,
        Err(error) if error.kind() == ErrorKind::NotFound => return Ok(()),
        Err(error) => return Err(error),
    };
    let length = input.metadata()?.len();
    if length == 0 {
        return Ok(());
    }
    let start = length.saturating_sub(MAX);
    input.seek(SeekFrom::Start(start))?;
    let mut bytes = Vec::with_capacity((length - start) as usize);
    input.take(MAX).read_to_end(&mut bytes)?;
    if bytes.is_empty() {
        return Ok(());
    }
    let owner = fs::metadata(dir).map(|m| m.uid())?;
    replace_atomic(
        &dir.join("visual.log"),
        &bytes,
        0o644,
        Some(owner),
    )
}
fn write_recovery_identity(dir: &Path, identity: &recovery::RecoveryIdentity) -> io::Result<()> {
    let owner = fs::metadata(dir).map(|m| m.uid())?;
    replace_atomic_durable(
        &dir.join("recovery-identity"),
        identity.serialize().as_bytes(),
        0o600,
        Some(owner),
    )
}
fn write_game_file(path: &Path, value: &str, uid: u32) {
    let _ = replace_atomic(path, value.as_bytes(), 0o644, Some(uid));
}
fn valid_dir(p: &Path) -> bool {
    let s = p.to_string_lossy();
    p.is_absolute()
        && (s == "/data/user/0/com.xnas.wpe.suite/files/modules/battleloop"
            || s == "/data/data/com.xnas.wpe.suite/files/modules/battleloop")
}
fn parse_start_ticks(stat: &str) -> Option<u64> {
    stat.rsplit_once(") ")?
        .1
        .split_whitespace()
        .nth(19)?
        .parse()
        .ok()
}
fn first_cmdline_arg(cmdline: &[u8]) -> &[u8] {
    cmdline.split(|b| *b == 0).next().unwrap_or_default()
}
fn proc_pid_gone(pid: u32) -> Result<bool, String> {
    match fs::metadata(format!("/proc/{pid}")) {
        Ok(_) => Ok(false),
        Err(error) if error.kind() == ErrorKind::NotFound => Ok(true),
        Err(error) => Err(format!("proc pid directory unreadable for pid {pid}: {error}")),
    }
}
fn original_process_missing_stat(pid: u32) -> recovery::OriginalProcess {
    let observation = match fs::metadata(format!("/proc/{pid}")) {
        Ok(_) => recovery::ProcDirectoryObservation::Present,
        Err(error) if error.kind() == ErrorKind::NotFound => {
            recovery::ProcDirectoryObservation::Missing
        }
        Err(_) => recovery::ProcDirectoryObservation::Unreadable,
    };
    recovery::missing_stat_result(observation)
}
fn lease_fresh(path: &Path) -> bool {
    fs::read_to_string(path).ok().and_then(|s| s.trim().parse::<u64>().ok())
        .is_some_and(|t| SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_secs().saturating_sub(t) <= 5)
}
fn game_pids() -> Result<Vec<recovery::ProcessIdentity>, String> {
    let mut out = Vec::new();
    let entries = fs::read_dir("/proc").map_err(|e| format!("proc root unreadable: {e}"))?;
    for entry in entries {
        let entry = entry.map_err(|e| format!("proc directory entry unreadable: {e}"))?;
        let Some(pid) = entry.file_name().to_str().and_then(|name| name.parse::<u32>().ok()) else {
            continue;
        };
        let proc_path = format!("/proc/{pid}");
        let cmdline = match fs::read(format!("{proc_path}/cmdline")) {
            Ok(contents) => contents,
            Err(error) if error.kind() == ErrorKind::NotFound => {
                if proc_pid_gone(pid)? {
                    continue;
                }
                return Err(format!("proc cmdline missing while pid {pid} remains"));
            }
            Err(error) => return Err(format!("proc cmdline unreadable for pid {pid}: {error}")),
        };
        if first_cmdline_arg(&cmdline) != recovery::TARGET_PACKAGE.as_bytes() {
            continue;
        }

        let stat = match fs::read_to_string(format!("{proc_path}/stat")) {
            Ok(contents) => contents,
            Err(error) if error.kind() == ErrorKind::NotFound => {
                if proc_pid_gone(pid)? {
                    continue;
                }
                return Err(format!("proc stat missing while target pid {pid} remains"));
            }
            Err(error) => return Err(format!("target proc stat unreadable for pid {pid}: {error}")),
        };
        let start_ticks = parse_start_ticks(&stat)
            .filter(|ticks| *ticks > 0)
            .ok_or_else(|| format!("target proc start ticks invalid for pid {pid}"))?;
        let exe = match fs::read_link(format!("{proc_path}/exe")) {
            Ok(path) => path
                .to_str()
                .ok_or_else(|| format!("target proc exe is not UTF-8 for pid {pid}"))?
                .to_owned(),
            Err(error) if error.kind() == ErrorKind::NotFound => {
                if proc_pid_gone(pid)? {
                    continue;
                }
                return Err(format!("proc exe missing while target pid {pid} remains"));
            }
            Err(error) => return Err(format!("target proc exe unreadable for pid {pid}: {error}")),
        };
        if !recovery::is_app_process_exe(&exe) {
            return Err(format!("target proc exe mismatch for pid {pid}"));
        }
        out.push(recovery::ProcessIdentity {
            pid,
            start_ticks,
            exe,
        });
    }
    Ok(out)
}

fn read_boot_id() -> io::Result<String> {
    let value = fs::read_to_string("/proc/sys/kernel/random/boot_id")?;
    let value = value.trim().to_owned();
    if !recovery::is_valid_boot_id(&value) {
        return Err(io::Error::new(ErrorKind::InvalidData, "invalid boot id"));
    }
    Ok(value)
}

fn read_stored_identity(path: &Path) -> recovery::StoredIdentity {
    match fs::read_to_string(path) {
        Ok(contents) => recovery::parse_identity(&contents)
            .map(recovery::StoredIdentity::Valid)
            .unwrap_or(recovery::StoredIdentity::Corrupt),
        Err(error) if error.kind() == ErrorKind::NotFound => recovery::StoredIdentity::Missing,
        Err(_) => recovery::StoredIdentity::Unreadable,
    }
}

fn inspect_original_process(pid: u32, expected_ticks: u64) -> recovery::OriginalProcess {
    let stat = match fs::read_to_string(format!("/proc/{pid}/stat")) {
        Ok(contents) => contents,
        Err(error) if error.kind() == ErrorKind::NotFound => {
            return original_process_missing_stat(pid)
        }
        Err(_) => return recovery::OriginalProcess::ReadError,
    };
    let Some(start_ticks) = parse_start_ticks(&stat).filter(|ticks| *ticks > 0) else {
        return recovery::OriginalProcess::ReadError;
    };
    if start_ticks != expected_ticks {
        return recovery::OriginalProcess::StartTicksChanged(start_ticks);
    }

    let exe = match fs::read_link(format!("/proc/{pid}/exe")) {
        Ok(path) => match path.to_str() {
            Some(value) => value.to_owned(),
            None => return recovery::OriginalProcess::ReadError,
        },
        Err(error) if error.kind() == ErrorKind::NotFound => {
            return inspect_after_proc_entry_disappeared(pid, expected_ticks)
        }
        Err(_) => return recovery::OriginalProcess::ReadError,
    };
    let cmdline = match fs::read(format!("/proc/{pid}/cmdline")) {
        Ok(value) => value,
        Err(error) if error.kind() == ErrorKind::NotFound => {
            return inspect_after_proc_entry_disappeared(pid, expected_ticks)
        }
        Err(_) => return recovery::OriginalProcess::ReadError,
    };
    recovery::OriginalProcess::SameIdentity {
        exe,
        cmdline_first_arg: first_cmdline_arg(&cmdline).to_vec(),
    }
}
fn inspect_after_proc_entry_disappeared(
    pid: u32,
    expected_ticks: u64,
) -> recovery::OriginalProcess {
    match fs::read_to_string(format!("/proc/{pid}/stat")) {
        Err(error) if error.kind() == ErrorKind::NotFound => original_process_missing_stat(pid),
        Err(_) => recovery::OriginalProcess::ReadError,
        Ok(stat) => match parse_start_ticks(&stat).filter(|ticks| *ticks > 0) {
            Some(ticks) if ticks != expected_ticks => {
                recovery::OriginalProcess::StartTicksChanged(ticks)
            }
            _ => recovery::OriginalProcess::ReadError,
        },
    }
}
fn original_process_end_decision(identity: &recovery::RecoveryIdentity) -> recovery::Decision {
    let boot = boot_observation();
    let original_process = match &boot {
        recovery::BootObservation::Value(current) if current == &identity.boot_id => {
            inspect_original_process(identity.pid, identity.start_ticks)
        }
        _ => recovery::OriginalProcess::ReadError,
    };
    recovery::decide(
        recovery::LockObservation::Acquired,
        recovery::StoredIdentity::Valid(identity.clone()),
        boot,
        original_process,
        recovery::ProcessScan::Error,
    )
}
fn persist_run_identity(
    dir: &Path,
    identity: &recovery::RecoveryIdentity,
) -> Result<(), String> {
    match read_stored_identity(&dir.join("recovery-identity")) {
        recovery::StoredIdentity::Missing => {}
        recovery::StoredIdentity::Unreadable => {
            return Err("existing recovery identity is unreadable; refusing attach".into())
        }
        recovery::StoredIdentity::Corrupt => {
            return Err("existing recovery identity is corrupt; refusing attach".into())
        }
        recovery::StoredIdentity::Valid(previous) if previous == *identity => return Ok(()),
        recovery::StoredIdentity::Valid(previous) => {
            if read_boot_id().map_err(|e| format!("cannot verify previous boot identity: {e}"))?
                == previous.boot_id
            {
                match inspect_original_process(previous.pid, previous.start_ticks) {
                    recovery::OriginalProcess::Gone
                    | recovery::OriginalProcess::StartTicksChanged(_) => {}
                    recovery::OriginalProcess::ReadError
                    | recovery::OriginalProcess::SameIdentity { .. } => {
                        return Err(
                            "previous recovery identity is still live or uncertain; refusing replacement"
                                .into(),
                        )
                    }
                }
            }
        }
    }
    write_recovery_identity(dir, identity)
        .map_err(|e| format!("persist process identity before attach: {e}"))
}
fn write_lua(
    path: &Path,
    template: &str,
    result: &Path,
    binding: &Path,
    run: &str,
    mode: &str,
    uid: u32,
    player: &str,
    manager: &str,
    lease: &Path,
    worker: &Path,
    log_path: &Path,
    visual_log_path: &Path,
    options: BattleOptions,
) -> Result<(), String> {
    let cfg=format!("{{resultPath={},bindingPath={},leasePath={},workerPath={},logPath={},visualLogPath={},skipBattle={},hideBattle={},stateKey={},runId={},expectedPlayer={},expectedManagerTostring={},mode={}}}",lua_quote(&result.to_string_lossy()),lua_quote(&binding.to_string_lossy()),lua_quote(&lease.to_string_lossy()),lua_quote(&worker.to_string_lossy()),lua_quote(&log_path.to_string_lossy()),lua_quote(&visual_log_path.to_string_lossy()),if options.skip_battle { "true" } else { "false" },if options.hide_battle { "true" } else { "false" },lua_quote("__codex_battle_loop_v1"),lua_quote(run),lua_quote(player),lua_quote(manager),lua_quote(mode));
    let source = template.replace("__CONFIG__", &cfg);
    let mut f = fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(path)
        .map_err(|e| format!("create {}: {e}", path.display()))?;
    f.write_all(source.as_bytes()).map_err(|e| e.to_string())?;
    if unsafe { fchown(f.as_raw_fd(), uid, u32::MAX) } != 0 {
        return Err(io::Error::last_os_error().to_string());
    }
    if unsafe { fchmod(f.as_raw_fd(), 0o600) } != 0 {
        return Err(io::Error::last_os_error().to_string());
    }
    f.sync_all().map_err(|e| e.to_string())?;
    Ok(())
}
fn read_kv(path: &Path) -> Option<(String, String, String)> {
    let s = fs::read_to_string(path).ok()?;
    let mut run = None;
    let mut player = None;
    let mut manager = None;
    for l in s.lines() {
        if let Some(v) = l.strip_prefix("runId=") {
            run = Some(v.to_owned())
        }
        if let Some(v) = l.strip_prefix("player=") {
            player = Some(v.to_owned())
        }
        if let Some(v) = l.strip_prefix("manager=") {
            manager = Some(v.to_owned())
        }
    }
    Some((run?, player?, manager?))
}
fn parse_calls(contents: &str) -> Option<u64> {
    contents
        .lines()
        .find_map(|line| line.strip_prefix("calls=")?.parse().ok())
}
fn parse_result_field<'a>(contents: &'a str, key: &str) -> Option<&'a str> {
    contents.lines().find_map(|line| Some(line.strip_prefix(key)?))
}
fn safe_result_field(snapshot: &str, key: &str, fallback: &str) -> String {
    parse_result_field(snapshot, key)
        .unwrap_or(fallback)
        .replace(['\r', '\n', '='], " ")
}
fn cleanup_confirmed(snapshot: &str, options: BattleOptions) -> bool {
    let base = parse_result_field(snapshot, "status=") == Some("stopped")
        && parse_result_field(snapshot, "hooksRemoved=") == Some("true")
        && parse_result_field(snapshot, "bindingCleared=") == Some("true");
    if !base {
        return false;
    }
    let auto_restore_confirmed = !options.skip_battle
        || parse_result_field(snapshot, "restoreConfirmed=") == Some("true");
    let visual_restore_confirmed = match parse_result_field(snapshot, "hideBattleRequested=") {
        Some("true") => {
            parse_result_field(snapshot, "visibilityRestoreConfirmed=") == Some("true")
        }
        Some("false") | None => !options.hide_battle,
        _ => false,
    };
    auto_restore_confirmed && visual_restore_confirmed
}
fn terminal_loop_status(snapshot: &str, calls: u64, options: BattleOptions) -> String {
    let requested = if options.hide_battle { "true" } else { "false" };
    let skip_requested = parse_result_field(snapshot, "skipBattleRequested=")
        .unwrap_or(if options.skip_battle { "true" } else { "false" });
    let auto_restore = "true";
    format!(
        "status=stopped\nstate=stopped\ncalls={calls}\nskipBattleRequested={}\nhideBattleRequested={}\nvisualState={}\nhiddenBattles={}\nvisualRounds={}\nvisibilityRestoreConfirmed={}\ncharacterSkillId={}\npetId={}\npetSkillId={}\nround={}\nrestoreConfirmed={}\nautoRestoreState={}\nhooksRemoved=true\nbindingCleared=true\n",
        safe_result_field(snapshot, "skipBattleRequested=", skip_requested),
        safe_result_field(snapshot, "hideBattleRequested=", requested),
        safe_result_field(snapshot, "visualState=", "unknown"),
        safe_result_field(snapshot, "hiddenBattles=", "unknown"),
        safe_result_field(snapshot, "visualRounds=", "unknown"),
        safe_result_field(snapshot, "visibilityRestoreConfirmed=", "unknown"),
        safe_result_field(snapshot, "characterSkillId=", "unknown"),
        safe_result_field(snapshot, "petId=", "unknown"),
        safe_result_field(snapshot, "petSkillId=", "unknown"),
        safe_result_field(snapshot, "round=", "unknown"),
        auto_restore,
        if options.skip_battle { "confirmed" } else { "not_needed" }
    )
}
fn publish_loop_status(dir: &Path, snapshot: &str, calls: u64, options: BattleOptions) {
    let field = |key: &str| {
        parse_result_field(snapshot, key)
            .unwrap_or("unknown")
            .replace(['\r', '\n', '='], " ")
    };
    let requested = if options.hide_battle { "true" } else { "false" };
    let requested = parse_result_field(snapshot, "hideBattleRequested=").unwrap_or(requested);
    let skip_requested = parse_result_field(snapshot, "skipBattleRequested=")
        .unwrap_or(if options.skip_battle { "true" } else { "false" });
    let state = parse_result_field(snapshot, "status=")
        .or_else(|| parse_result_field(snapshot, "state="))
        .unwrap_or("waiting_role")
        .replace(['\r', '\n', '='], " ");
    status(
        dir,
        &format!(
            "status={state}\nstate={state}\ncalls={calls}\nskipBattleRequested={}\nhideBattleRequested={}\nvisualState={}\nhiddenBattles={}\nvisualRounds={}\nvisibilityRestoreConfirmed={}\ncharacterSkillId={}\npetId={}\npetSkillId={}\nround={}\nrestoreConfirmed={}\nautoRestoreState={}\nhooksRemoved={}\nbindingCleared={}\n",
            skip_requested.replace(['\r', '\n', '='], " "),
            requested.replace(['\r', '\n', '='], " "),
            field("visualState="),
            field("hiddenBattles="),
            field("visualRounds="),
            field("visibilityRestoreConfirmed="),
            field("characterSkillId="),
            field("petId="),
            field("petSkillId="),
            field("round="),
            field("restoreConfirmed="),
            if options.skip_battle { "confirmed" } else { "not_needed" },
            field("hooksRemoved="),
            field("bindingCleared=")
        ),
    );
}
fn no_hook_terminal_status(options: BattleOptions) -> String {
    format!(
        "status=stopped\nstate=stopped\nskipBattleRequested={}\nhideBattleRequested={}\nvisualState=not_started\nhiddenBattles=0\nvisualRounds=0\nvisibilityRestoreConfirmed=true\nvisibilityRestoreState=not_needed\nhooksRemoved=true\nbindingCleared=true\nrestoreConfirmed=true\nautoRestoreState=not_needed\n",
        if options.skip_battle { "true" } else { "false" },
        if options.hide_battle { "true" } else { "false" }
    )
}
fn incomplete_cleanup_status(snapshot: &str, calls: u64, options: BattleOptions, reason: &str) -> String {
    let requested = if options.hide_battle { "true" } else { "false" };
    let requested = parse_result_field(snapshot, "hideBattleRequested=").unwrap_or(requested);
    let skip_requested = parse_result_field(snapshot, "skipBattleRequested=")
        .unwrap_or(if options.skip_battle { "true" } else { "false" });
    format!(
        "status=stopping\nstate=stopping\nreason={reason}\ncalls={calls}\nskipBattleRequested={}\nhideBattleRequested={}\nvisualState={}\nhiddenBattles={}\nvisualRounds={}\nvisibilityRestoreConfirmed={}\ncharacterSkillId={}\npetId={}\npetSkillId={}\nround={}\nrestoreConfirmed={}\nautoRestoreState={}\nhooksRemoved={}\nbindingCleared={}\n",
        skip_requested.replace(['\r', '\n', '='], " "),
        requested.replace(['\r', '\n', '='], " "),
        safe_result_field(snapshot, "visualState=", "unknown"),
        safe_result_field(snapshot, "hiddenBattles=", "unknown"),
        safe_result_field(snapshot, "visualRounds=", "unknown"),
        safe_result_field(snapshot, "visibilityRestoreConfirmed=", "unknown"),
        safe_result_field(snapshot, "characterSkillId=", "unknown"),
        safe_result_field(snapshot, "petId=", "unknown"),
        safe_result_field(snapshot, "petSkillId=", "unknown"),
        safe_result_field(snapshot, "round=", "unknown"),
        safe_result_field(snapshot, "restoreConfirmed=", "unknown"),
        if options.skip_battle { "pending" } else { "not_needed" },
        safe_result_field(snapshot, "hooksRemoved=", "unknown"),
        safe_result_field(snapshot, "bindingCleared=", "unknown")
    )
}
fn self_test() -> Result<(), String> {
    assert!(valid_dir(Path::new(
        "/data/user/0/com.xnas.wpe.suite/files/modules/battleloop"
    )));
    assert!(!valid_dir(Path::new(
        "/data/data/com.xnas.wpe.suite/files/modules/battleloop/run"
    )));
    assert!(!valid_dir(Path::new("/data/user/0/other/files")));
    assert!(!valid_dir(Path::new(
        "/data/user/0/com.xnas.wpe.suite/files/modules/battleloop/../x"
    )));
    let clean = "status=stopped\nhooksRemoved=true\nbindingCleared=true\nrestoreConfirmed=true\n";
    assert!(cleanup_confirmed(
        &format!("{clean}hideBattleRequested=false\n"),
        BattleOptions { skip_battle: true, hide_battle: false }
    ));
    assert!(cleanup_confirmed(
        clean,
        BattleOptions { skip_battle: true, hide_battle: false }
    ));
    assert!(!cleanup_confirmed(
        &format!("{clean}hideBattleRequested=true\n"),
        BattleOptions { skip_battle: true, hide_battle: true }
    ));
    assert!(!cleanup_confirmed(
        &format!("{clean}hideBattleRequested=true\nvisibilityRestoreConfirmed=false\n"),
        BattleOptions { skip_battle: true, hide_battle: true }
    ));
    assert!(cleanup_confirmed(
        &format!("{clean}hideBattleRequested=true\nvisibilityRestoreConfirmed=true\n"),
        BattleOptions { skip_battle: true, hide_battle: true }
    ));
    assert!(!cleanup_confirmed(
        &format!("{clean}hideBattleRequested=false\n"),
        BattleOptions { skip_battle: true, hide_battle: true }
    ));
    let hide_only = "status=stopped\nhooksRemoved=true\nbindingCleared=true\nhideBattleRequested=true\nvisibilityRestoreConfirmed=true\n";
    assert!(cleanup_confirmed(
        hide_only,
        BattleOptions { skip_battle: false, hide_battle: true }
    ));
    assert!(!cleanup_confirmed(
        &hide_only.replace("visibilityRestoreConfirmed=true", "visibilityRestoreConfirmed=false"),
        BattleOptions { skip_battle: false, hide_battle: true }
    ));
    assert!(!cleanup_confirmed(
        &hide_only.replace("bindingCleared=true", "bindingCleared=false"),
        BattleOptions { skip_battle: false, hide_battle: true }
    ));
    assert!(may_publish_no_hook_terminal(None, false));
    assert!(!may_publish_no_hook_terminal(Some("state=unknown\n"), false));
    assert!(!may_publish_no_hook_terminal(None, true));
    let stat = format!(
        "1833 (name with ) spaces) S {}",
        (4..=22)
            .map(|n| n.to_string())
            .collect::<Vec<_>>()
            .join(" ")
    );
    assert_eq!(parse_start_ticks(&stat), Some(22));
    assert_eq!(first_cmdline_arg(b"com.gdoo.dhxy\0--x\0"), b"com.gdoo.dhxy");
    assert_ne!(
        first_cmdline_arg(b"com.gdoo.dhxy.extra\0"),
        b"com.gdoo.dhxy"
    );
    println!("self-test ok");
    Ok(())
}
fn daemonize(dir: &Path) -> Result<(), String> {
    let exe = std::env::current_exe().map_err(|e| e.to_string())?;
    let log = fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(dir.join("worker.log"))
        .map_err(|e| e.to_string())?;
    let err_log = log.try_clone().map_err(|e| e.to_string())?;
    let mut cmd = Command::new(exe);
    cmd.arg("--run")
        .arg("--dir")
        .arg(dir)
        .stdin(Stdio::null())
        .stdout(Stdio::from(log))
        .stderr(Stdio::from(err_log));
    unsafe {
        cmd.pre_exec(|| {
            if setsid() == -1 {
                Err(io::Error::last_os_error())
            } else {
                Ok(())
            }
        });
    }
    cmd.spawn().map_err(|e| format!("spawn daemon: {e}"))?;
    Ok(())
}
fn open_worker_lock(dir: &Path) -> io::Result<fs::File> {
    fs::OpenOptions::new()
        .read(true)
        .write(true)
        .create(true)
        .open(dir.join("worker.lock"))
}
fn lock_worker(lock: &fs::File) -> io::Result<()> {
    if unsafe { flock(lock.as_raw_fd(), 2 | 4) } == 0 {
        Ok(())
    } else {
        Err(io::Error::last_os_error())
    }
}
fn boot_observation() -> recovery::BootObservation {
    match read_boot_id() {
        Ok(value) => recovery::BootObservation::Value(value),
        Err(error) if error.kind() == ErrorKind::InvalidData => recovery::BootObservation::Invalid,
        Err(_) => recovery::BootObservation::Unreadable,
    }
}
fn scan_observation() -> (recovery::ProcessScan, Option<String>) {
    match game_pids() {
        Ok(processes) => (recovery::ProcessScan::Complete(processes), None),
        Err(error) => (recovery::ProcessScan::Error, Some(error)),
    }
}
fn recovery_message(reason: &str) -> &'static str {
    match reason {
        "worker_running" => "工作进程仍持有运行锁，旧任务状态未确认",
        "worker_lock_unavailable" => "无法安全取得工作进程锁，旧任务状态未确认",
        "prior_identity_unreadable" => "没有可用的上一轮进程记录，按当前游戏进程启动",
        "prior_identity_corrupt" => "上一轮进程记录损坏，按当前游戏进程启动",
        "prior_process_unreadable" => "读不到上一轮游戏进程信息，按当前游戏进程启动",
        "prior_process_replaced" => "上一轮的进程身份已变化，按当前游戏进程启动",
        "prior_process_alive" => "上一轮的游戏进程仍在，但没有工作进程在跑；启动时游戏内会先还原上一轮",
        "no_prior_record" => "未找到上一轮记录，以当前游戏进程作为新会话启动",
        "start_interrupted" => "上次启动在派发前中断，按当前游戏进程重新启动",
        "identity_unreadable" => "旧进程身份记录无法读取，按当前游戏进程启动",
        "identity_corrupt" => "旧进程身份记录损坏，按当前游戏进程启动",
        "boot_id_unreadable" => "系统启动身份无法读取，旧任务状态未确认",
        "boot_id_invalid" => "系统启动身份格式无效，旧任务状态未确认",
        "original_process_unreadable" => "原游戏进程信息无法完整读取，按当前游戏进程启动",
        "original_process_exe_changed" => "原进程号仍在运行但程序身份变化，按当前游戏进程启动",
        "original_process_cmdline_changed" => "原进程号仍在运行但命令行身份变化，按当前游戏进程启动",
        "original_process_identity_invalid" => "原进程身份读取结果无效，旧任务状态未确认",
        "process_scan_unreadable" => "无法完整扫描游戏进程，旧任务状态未确认",
        "multiple_target_processes" => "发现多个DHXY主进程，无法确认旧任务状态",
        "process_identity_invalid" => "游戏进程身份信息无效，旧任务状态未确认",
        _ => "旧任务状态无法确认",
    }
}
fn unix_seconds() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|elapsed| elapsed.as_secs())
        .unwrap_or(0)
}
fn apply_recovery_decision(dir: &Path, decision: recovery::Decision) -> Result<(), String> {
    match decision {
        recovery::Decision::Unknown(reason) => write_recovery_status(
            dir,
            &format!(
                "state=unconfirmed\nreason={reason}\ncheckedAt={}\nmessage={}\n",
                unix_seconds(),
                recovery_message(reason)
            ),
        )
        .map_err(|e| format!("write recovery-status: {e}")),
        // 锁已空闲：没有 worker 在跑。上一轮是否清理干净不再作为启动前置条件，
        // 只记录当前身份作为下一次核对的基线，并明确标记可启动。
        recovery::Decision::Startable(identity, reason) => {
            let mut body = format!(
                "state=startable\nreason={reason}\nlockProbe=free\ncheckedAt={}\n",
                unix_seconds()
            );
            if let Some(identity) = &identity {
                // 写失败只影响下次核对的基线，不阻止启动。
                let _ = write_recovery_identity(dir, identity);
                body.push_str(&format!(
                    "package=com.gdoo.dhxy\npid={}\nstartTicks={}\n",
                    identity.pid, identity.start_ticks
                ));
            }
            body.push_str(&format!("message={}\n", recovery_message(reason)));
            write_recovery_status(dir, &body).map_err(|e| format!("write recovery-status: {e}"))
        }
        recovery::Decision::VerifiedNoTarget => {
            write_terminal_status(
                dir,
                "state=stopped\nprocessEnded=true\nrecoveryVerified=true\nrecoveryProof=no_target_process\npackage=com.gdoo.dhxy\n",
            )
            .map_err(|e| format!("write terminal recovery status: {e}"))?;
            write_recovery_status(
                dir,
                "state=verified\nrecoveryProof=no_target_process\npackage=com.gdoo.dhxy\nmessage=未发现DHXY主进程，旧任务保护核验通过\n",
            )
            .map_err(|e| format!("write recovery-status: {e}"))
        }
        recovery::Decision::VerifiedOldProcess(identity) => {
            write_terminal_status(
                dir,
                &format!(
                    "state=stopped\nprocessEnded=true\nrecoveryVerified=true\nrecoveryProof=original_process_ended\npackage=com.gdoo.dhxy\npid={}\nstartTicks={}\n",
                    identity.pid, identity.start_ticks
                ),
            )
            .map_err(|e| format!("write terminal recovery status: {e}"))?;
            write_recovery_status(
                dir,
                &format!(
                    "state=verified\nrecoveryProof=original_process_ended\npackage=com.gdoo.dhxy\npid={}\nstartTicks={}\nmessage=旧任务对应的DHXY进程已结束，保护核验通过\n",
                    identity.pid, identity.start_ticks
                ),
            )
            .map_err(|e| format!("write recovery-status: {e}"))
        }
    }
}
fn reconcile(dir: PathBuf) -> Result<(), String> {
    if !valid_dir(&dir) {
        return Err("--dir must be the app private files directory".into());
    }
    fs::create_dir_all(&dir).map_err(|e| format!("module directory: {e}"))?;
    write_recovery_status(
        &dir,
        "state=checking\nmessage=正在只读检查旧任务进程身份\n",
    )
    .map_err(|e| format!("write recovery-status: {e}"))?;

    let lock = match open_worker_lock(&dir) {
        Ok(lock) => lock,
        Err(error) => {
            let decision = recovery::decide(
                recovery::LockObservation::Unavailable,
                recovery::StoredIdentity::Missing,
                recovery::BootObservation::Unreadable,
                recovery::OriginalProcess::ReadError,
                recovery::ProcessScan::Error,
            );
            let _ = apply_recovery_decision(&dir, decision);
            return Err(format!("open worker lock: {error}"));
        }
    };
    if let Err(error) = lock_worker(&lock) {
        if error.kind() == ErrorKind::WouldBlock {
            let decision = recovery::decide(
                recovery::LockObservation::Busy,
                recovery::StoredIdentity::Missing,
                recovery::BootObservation::Unreadable,
                recovery::OriginalProcess::ReadError,
                recovery::ProcessScan::Error,
            );
            return apply_recovery_decision(&dir, decision);
        }
        let decision = recovery::decide(
            recovery::LockObservation::Unavailable,
            recovery::StoredIdentity::Missing,
            recovery::BootObservation::Unreadable,
            recovery::OriginalProcess::ReadError,
            recovery::ProcessScan::Error,
        );
        let _ = apply_recovery_decision(&dir, decision);
        return Err(format!("lock worker: {error}"));
    }
    let _lock = lock;

    let stored = read_stored_identity(&dir.join("recovery-identity"));
    let boot = boot_observation();
    let mut original_process = recovery::OriginalProcess::ReadError;
    let mut scan = recovery::ProcessScan::Error;
    let mut scan_error = None;
    if let recovery::StoredIdentity::Valid(identity) = &stored {
        if boot == recovery::BootObservation::Value(identity.boot_id.clone()) {
            original_process = inspect_original_process(identity.pid, identity.start_ticks);
        }
    } else {
        // 没有可用记录时扫描当前 DHXY 进程，用它的身份作为新会话基线。
        let (observed, error) = scan_observation();
        scan = observed;
        scan_error = error;
    }

    let decision = recovery::decide(
        recovery::LockObservation::Acquired,
        stored,
        boot,
        original_process,
        scan,
    );
    if matches!(&decision, recovery::Decision::Unknown("process_scan_unreadable")) {
        if let Some(detail) = scan_error {
            eprintln!("reconcile: process scan failed: {detail}");
        }
    }
    apply_recovery_decision(&dir, decision)
}
fn run(dir: PathBuf) -> Result<(), String> {
    if !valid_dir(&dir) {
        return Err("--dir must be the app private files directory".into());
    }
    license_lease::require_module_lease(&dir)?;
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let options = read_battle_options(&dir)?;
    let lock = match open_worker_lock(&dir) {
        Ok(lock) => lock,
        Err(error) if !options.any_enabled() => {
            eprintln!("worker: no battle mode enabled; preserving prior state: {error}");
            return Ok(());
        }
        Err(error) => return Err(error.to_string()),
    };
    if let Err(error) = lock_worker(&lock) {
        if !options.any_enabled() {
            eprintln!("worker: no battle mode enabled; preserving prior state: {error}");
            return Ok(());
        }
        return Err("worker already running".into());
    }
    if !options.any_enabled() {
        let existing_status = match fs::read_to_string(dir.join("status")) {
            Ok(contents) => Some(contents),
            Err(error) if error.kind() == ErrorKind::NotFound => None,
            Err(error) => {
                eprintln!("worker: no battle mode enabled; preserving unreadable status: {error}");
                return Ok(());
            }
        };
        let recovery_exists = match fs::symlink_metadata(dir.join("recovery-status")) {
            Ok(_) => true,
            Err(error) if error.kind() == ErrorKind::NotFound => false,
            Err(error) => {
                eprintln!("worker: no battle mode enabled; preserving unreadable recovery evidence: {error}");
                return Ok(());
            }
        };
        if may_publish_no_hook_terminal(existing_status.as_deref(), recovery_exists) {
            status(&dir, &no_hook_terminal_status(options));
        } else {
            eprintln!("worker: no battle mode enabled; preserving prior state and recovery evidence");
        }
        return Ok(());
    }
    match fs::remove_file(dir.join("recovery-status")) {
        Ok(()) => {}
        Err(error) if error.kind() == ErrorKind::NotFound => {}
        Err(error) => return Err(format!("remove stale recovery-status: {error}")),
    }
    let stop = dir.join("stop");
    let _ = fs::remove_file(&stop);
    let template = include_str!("../../../../app/src/main/assets/battle_loop.lua.in");
    let agent = include_str!("../../../../app/src/main/assets/battle_loop_agent.js");
    let uid = fs::metadata("/data/data/com.gdoo.dhxy/cache")
        .or_else(|_| fs::metadata("/data/user/0/com.gdoo.dhxy/cache"))
        .map_err(|e| format!("game cache metadata: {e}"))?
        .uid();
    let mut run_id = String::new();
    let cache = if Path::new("/data/user/0/com.gdoo.dhxy/cache").is_dir() {
        PathBuf::from("/data/user/0/com.gdoo.dhxy/cache")
    } else {
        PathBuf::from("/data/data/com.gdoo.dhxy/cache")
    };
    let mut active_paths: Option<(PathBuf, PathBuf, PathBuf, PathBuf)> = None;
    let mut attach_attempted = false;
    // Houdini 在游戏启动最初几秒最不稳定，过早 attach 会引发 SIGSEGV。
    let mut attach_ready_at: Option<std::time::Instant> = None;
    let mut calls = 0u64;
    loop {
        if !lease_fresh(&dir.join("lease")) || license_lease::require_module_lease(&dir).is_err() {
            if attach_attempted {
                status_with_modes(&dir, "state=stopping\nstatus=stopping\nreason=app_lease_expired\n", options);
            } else {
                let mut terminal = no_hook_terminal_status(options);
                terminal.push_str("reason=app_lease_expired\n");
                status_with_modes(&dir, &terminal, options);
            }
            return Ok(());
        }
        if stop.exists() {
            if active_paths.is_some() {
                status_with_modes(&dir, "state=stopping\nstatus=stopping\n", options);
            } else {
                status(&dir, &no_hook_terminal_status(options));
                return Ok(());
            }
        }
        let pids = match game_pids() {
            Ok(pids) => pids,
            Err(_) => {
                status_with_modes(&dir, "state=waiting_game\nstatus=waiting_game\ngameCount=unknown\nreason=process_scan_unknown\n", options);
                thread::sleep(Duration::from_secs(1));
                continue;
            }
        };
        if pids.len() != 1 {
            status_with_modes(&dir, &format!("state=waiting_game\nstatus=waiting_game\ngameCount={}\n", pids.len()), options);
            thread::sleep(Duration::from_secs(1));
            continue;
        }
        let target = pids[0].clone();
        let pid = target.pid;
        let ticks = target.start_ticks;
        let base = format!("codex-battle-loop-{pid}-{ticks}");
        let same_identity = active_paths.as_ref().is_some_and(|(old, _, _, _)| {
            old.file_name()
                .is_some_and(|n| n.to_string_lossy().starts_with(&format!("{base}-")))
        });
        if !same_identity {
            attach_ready_at = None;
            run_id = format!(
                "{}",
                SystemTime::now()
                    .duration_since(UNIX_EPOCH)
                    .unwrap()
                    .as_nanos()
            );
        }
        match game_pids() {
            Ok(processes) if processes.len() == 1 && processes[0] == target => {}
            Ok(_) => {
                status_with_modes(&dir, "state=waiting_game\nstatus=waiting_game\nreason=identity_changed_before_attach\n", options);
                continue;
            }
            Err(_) => {
                status_with_modes(&dir, "state=waiting_game\nstatus=waiting_game\ngameCount=unknown\nreason=process_scan_unknown\n", options);
                thread::sleep(Duration::from_secs(1));
                continue;
            }
        }
        let boot_id = read_boot_id().map_err(|e| format!("boot id unavailable before attach: {e}"))?;
        let run_identity = recovery::RecoveryIdentity::from_process(&target, boot_id);
        persist_run_identity(&dir, &run_identity)?;
        let prefix = format!("{base}-{run_id}");
        let work = cache.join(format!("{prefix}-tick"));
        if active_paths
            .as_ref()
            .is_none_or(|(old, _, _, _)| old != &work)
        {
            let tick = cache.join(format!("{prefix}-tick.lua"));
            let cleanup = cache.join(format!("{prefix}-cleanup.lua"));
            let result = cache.join(format!("{prefix}-result"));
            let binding = cache.join(format!("{prefix}-binding"));
            let lease_mirror = cache.join(format!("{prefix}-lease"));
            let worker_beat = cache.join(format!("{prefix}-worker-heartbeat"));
            let rounds_log = cache.join(format!("{prefix}-rounds.log"));
            let visual_log = cache.join(format!("{prefix}-visual.log"));
            for p in [&tick, &cleanup, &result, &binding, &lease_mirror, &worker_beat, &visual_log] {
                if p.exists() {
                    return Err(format!("refusing collision at {}", p.display()));
                }
            }
            write_lua(
                &tick, template, &result, &binding, &run_id, "tick", uid, "", "",
                &lease_mirror, &worker_beat, &rounds_log, &visual_log, options,
            )?;
            write_lua(
                &cleanup, template, &result, &binding, &run_id, "stop", uid, "", "",
                &lease_mirror, &worker_beat, &rounds_log, &visual_log, options,
            )?;
            for p in [&result, &binding, &lease_mirror, &worker_beat] {
                create_owned(p, uid, 0o600)
                    .map_err(|e| format!("create/own {}: {e}", p.display()))?;
            }
            let initial_lease = fs::read_to_string(dir.join("lease")).unwrap_or_else(|_| "0".into());
            write_game_file(&lease_mirror, initial_lease.trim(), uid);
            write_game_file(&worker_beat, &SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_secs().to_string(), uid);
            active_paths = Some((work, tick, result, binding));
        }
        let (_, tick, result, binding) = active_paths.as_ref().unwrap();
        let lease_mirror = cache.join(format!("{prefix}-lease"));
        let worker_beat = cache.join(format!("{prefix}-worker-heartbeat"));
        let after = game_pids();
        if !matches!(after, Ok(ref processes) if processes.len() == 1 && processes[0] == target) {
            status_with_modes(&dir, "state=waiting_game\nstatus=waiting_game\nreason=identity_changed\n", options);
            continue;
        }
        // 第一次看到目标进程后先等 6 秒再注入：直接 attach 会让刚启动的游戏在
        // houdini 转译层里 SIGSEGV（tombstone 里进程 uptime 只有 2-3 秒）。
        // 等待期间仍每秒写心跳与状态，避免 Lua/服务端把 worker 判成失联。
        let now = std::time::Instant::now();
        let ready = *attach_ready_at.get_or_insert(now + Duration::from_secs(6));
        if now < ready {
            status_with_modes(&dir, "state=waiting_game\nstatus=waiting_game\nreason=settling_before_attach\n", options);
            thread::sleep(Duration::from_millis(200));
            continue;
        }        license_lease::require_module_lease(&dir)?;
        unsafe { frida_init() }
        let mut error = ptr::null_mut();
        let manager = unsafe { frida_device_manager_new() };
        if manager.is_null() {
            return Err("Frida manager null".into());
        }
        let list = unsafe {
            frida_device_manager_enumerate_devices_sync(manager, ptr::null_mut(), &mut error)
        };
        unsafe { err(error, "enumerate")? }
        let mut dev = ptr::null_mut();
        for i in 0..unsafe { frida_device_list_size(list) } {
            let d = unsafe { frida_device_list_get(list, i) };
            if !d.is_null() && unsafe { frida_device_get_dtype(d) } == 0 {
                dev = d;
                break;
            }
            if !d.is_null() {
                unsafe { frida_unref(d.cast()) }
            }
        }
        unsafe { frida_unref(list.cast()) }
        if dev.is_null() {
            return Err("local Frida device missing".into());
        }
        attach_attempted = true;
        let session = unsafe {
            frida_device_attach_sync(dev, pid, ptr::null_mut(), ptr::null_mut(), &mut error)
        };
        unsafe { err(error, "attach")? }
        if session.is_null() {
            return Err("attach returned null".into());
        }
        let attached_identity = game_pids();
        if !matches!(attached_identity, Ok(ref processes) if processes.len() == 1 && processes[0] == target) {
            unsafe {
                let _ = frida_session_detach_sync(session, ptr::null_mut(), ptr::null_mut());
                frida_unref(session.cast());
                frida_unref(dev.cast());
                frida_device_manager_close_sync(manager, ptr::null_mut(), ptr::null_mut());
                frida_unref(manager.cast());
            }
            for p in [
                &*tick,
                &*result,
                &*binding,
                &cache.join(format!("{prefix}-cleanup.lua")),
            ] {
                let _ = fs::remove_file(p);
            }
            active_paths = None;
            status_with_modes(&dir, "state=waiting_game\nstatus=waiting_game\nreason=identity_changed_after_attach\n", options);
            continue;
        }
        let opts = unsafe { frida_script_options_new() };
        let name = cstr("battle-loop-worker")?;
        unsafe {
            frida_script_options_set_name(opts, name.as_ptr());
            frida_script_options_set_runtime(opts, 1)
        }
        let src = cstr(agent)?;
        let script = unsafe {
            frida_session_create_script_sync(
                session,
                src.as_ptr(),
                opts,
                ptr::null_mut(),
                &mut error,
            )
        };
        unsafe {
            frida_unref(opts.cast());
            err(error, "create script")?
        }
        if script.is_null() {
            return Err("script null".into());
        }
        let (tx, rx) = mpsc::channel();
        *TX.get_or_init(|| Mutex::new(None)).lock().unwrap() = Some(tx);
        let signal = cstr("message")?;
        unsafe {
            _frida_g_signal_connect_data(
                script.cast(),
                signal.as_ptr(),
                Some(std::mem::transmute(
                    on_message as unsafe extern "C" fn(_, _, _, _),
                )),
                ptr::null_mut(),
                None,
                0,
            );
            frida_script_load_sync(script, ptr::null_mut(), &mut error);
            err(error, "load script")?
        }
        let command=format!("{{\"type\":\"control\",\"payload\":{{\"command\":\"start\",\"id\":\"{run_id}\",\"tickPath\":\"{}\"}}}}",json_escape(&tick.to_string_lossy()));
        let command = cstr(&command)?;
        unsafe { frida_script_post(script, command.as_ptr(), ptr::null_mut()) }
        let mut stopping = false;
        let mut cleanup_sent = false;
        let mut cleanup_last_post = None;
        let mut detached = false;
        let mut retry_timeout = false;
        let mut fatal_exit: Option<String> = None;
        let mut cleanup_started = None;
        let mut cleanup_prompted = false;
        let mut status_at = std::time::Instant::now() - Duration::from_secs(2);
        let mut identity_checked = std::time::Instant::now();
        let mut result_checked = std::time::Instant::now() - Duration::from_millis(100);
        let mut beat_at = std::time::Instant::now() - Duration::from_secs(1);
        let mut rounds_log_at = std::time::Instant::now() - Duration::from_secs(2);
        let mut result_snapshot = String::new();
        let mut binding_snapshot = None;
        while !detached {
            unsafe { _frida_g_main_context_iteration(ptr::null_mut(), 0) };
            if rounds_log_at.elapsed() >= Duration::from_secs(2) {
                rounds_log_at = std::time::Instant::now();
                let _ = sync_rounds_log(&cache.join(format!("{prefix}-rounds.log")), &dir);
                let _ = sync_visual_log(&cache.join(format!("{prefix}-visual.log")), &dir);
            }
            if beat_at.elapsed() >= Duration::from_secs(1) {
                let app_lease = fs::read_to_string(dir.join("lease")).unwrap_or_else(|_| "0".into());
                let beat = SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_secs().to_string();
                write_game_file(&lease_mirror, app_lease.trim(), uid);
                write_game_file(&worker_beat, &beat, uid);
                beat_at = std::time::Instant::now();
                if !lease_fresh(&dir.join("lease")) || license_lease::require_module_lease(&dir).is_err() {
                    status_with_modes(&dir, "state=stopping\nstatus=stopping\nreason=app_lease_expired\n", options);
                    stopping = true;
                }
            }
            while let Ok(msg) = rx.try_recv() {
                if msg.contains("\"event\":\"fatal\"") {
                    let timeout = msg.contains("gl_thread_queue_timeout");
                    retry_timeout |= timeout;
                    if !timeout {
                        fatal_exit = Some(msg.chars().take(220).collect())
                    }
                    status_with_modes(
                        &dir,
                        if timeout {
                            "state=stopping\nstatus=stopping\nreason=gl_thread_queue_timeout\n"
                        } else {
                            "state=stopping\nstatus=stopping\nreason=fatal_business_error\n"
                        },
                        options,
                    );
                    stopping = true;
                }
            }
            if identity_checked.elapsed() >= Duration::from_secs(1) {
                let ident = game_pids();
                identity_checked = std::time::Instant::now();
                match ident {
                    Ok(processes) if processes.len() == 1 && processes[0] == target => {}
                    Err(error) => {
                        if matches!(
                            original_process_end_decision(&run_identity),
                            recovery::Decision::VerifiedOldProcess(_)
                        ) {
                            status_with_modes(
                                &dir,
                                "state=waiting_game\nstatus=waiting_game\nreason=game_gone\n",
                                options,
                            );
                            break;
                        }
                        eprintln!("worker: target identity scan failed: {error}");
                        status_with_modes(
                            &dir,
                            "state=stopping\nstatus=stopping\nreason=identity_read_unconfirmed\n",
                            options,
                        );
                        fatal_exit = Some("identity_read_unconfirmed".into());
                        stopping = true;
                    }
                    Ok(_) => {
                        if matches!(
                            original_process_end_decision(&run_identity),
                            recovery::Decision::VerifiedOldProcess(_)
                        ) {
                            status_with_modes(
                                &dir,
                                "state=waiting_game\nstatus=waiting_game\nreason=original_process_ended\n",
                                options,
                            );
                            detached = true;
                        } else {
                            status_with_modes(
                                &dir,
                                "state=stopping\nstatus=stopping\nreason=identity_changed_unconfirmed\n",
                                options,
                            );
                            fatal_exit = Some("identity_changed_unconfirmed".into());
                            stopping = true;
                        }
                    }
                }
            }
            if result_checked.elapsed() >= Duration::from_millis(100) {
                if let Some((_, _, result, binding)) = &active_paths {
                    result_snapshot = fs::read_to_string(result).unwrap_or_default();
                    if let Some(latest) = parse_calls(&result_snapshot) {
                        calls = latest;
                    }
                    binding_snapshot = read_kv(binding);
                    if cleanup_confirmed(&result_snapshot, options) {
                        detached = true;
                    }
                }
                result_checked = std::time::Instant::now();
            }
            if stop.exists() || stopping {
                if status_at.elapsed() >= Duration::from_millis(100) || !stopping {
                    let reason = if cleanup_prompted {
                        "cleanup_unconfirmed"
                    } else {
                        "cleanup_pending"
                    };
                    status_with_modes(
                        &dir,
                        &incomplete_cleanup_status(&result_snapshot, calls, options, reason),
                        options,
                    );
                    status_at = std::time::Instant::now();
                }
                if !cleanup_sent
                    || cleanup_last_post.is_some_and(|at: std::time::Instant| {
                        at.elapsed() >= Duration::from_secs(1)
                    })
                {
                    let cleanup = cache.join(format!("{prefix}-cleanup.lua"));
                    let cmd=format!("{{\"type\":\"control\",\"payload\":{{\"command\":\"stop\",\"id\":\"stop-{run_id}\",\"cleanupPath\":\"{}\"}}}}",json_escape(&cleanup.to_string_lossy()));
                    let c = cstr(&cmd)?;
                    unsafe { frida_script_post(script, c.as_ptr(), ptr::null_mut()) }
                    cleanup_sent = true;
                    cleanup_last_post = Some(std::time::Instant::now());
                    stopping = true;
                }
                if cleanup_started.is_none() {
                    cleanup_started = Some(std::time::Instant::now())
                }
                if active_paths.is_some() {
                    if cleanup_confirmed(&result_snapshot, options) {
                        detached = true;
                    } else if cleanup_started.unwrap().elapsed() > Duration::from_secs(5)
                        && !cleanup_prompted
                    {
                        cleanup_prompted = true;
                    }
                }
            } else if active_paths.is_some() {
                if result_snapshot.contains("status=fatal") {
                    fatal_exit = Some(
                        result_snapshot
                            .lines()
                            .take(8)
                            .collect::<Vec<_>>()
                            .join(" "),
                    );
                    status_with_modes(&dir, "state=stopping\nstatus=stopping\nreason=lua_fatal\n", options);
                    stopping = true;
                }
                if let Some((bound_run, player, manager_text)) = &binding_snapshot {
                    // Lua 的清理路径会用同一个 runId 写入空 player/manager，那是清理标记而不是身份失败；
                    // 只有非空身份快照才参与校验，避免正常停止被误判成 fatal。
                    if bound_run == &run_id && !player.is_empty() && !manager_text.is_empty() {
                        let uid_ok = active_paths.as_ref().is_some_and(|(_, _, _, binding)| {
                            fs::metadata(binding).is_ok_and(|m| m.uid() == uid)
                        });
                        if !uid_ok {
                            fatal_exit = Some("binding owner or identity validation failed".into());
                            stopping = true;
                        } else if status_at.elapsed() >= Duration::from_millis(100) {
                            publish_loop_status(&dir, &result_snapshot, calls, options);
                            status_at = std::time::Instant::now();
                        }
                    } else if status_at.elapsed() >= Duration::from_millis(100) {
                        publish_loop_status(&dir, &result_snapshot, calls, options);
                        status_at = std::time::Instant::now();
                    }
                } else if status_at.elapsed() >= Duration::from_millis(100) {
                    publish_loop_status(&dir, &result_snapshot, calls, options);
                    status_at = std::time::Instant::now();
                }
            }
            thread::sleep(Duration::from_millis(40));
        }
        if detached {
            if cleanup_confirmed(&result_snapshot, options) {
                attach_attempted = false;
            }
            let _ = sync_visual_log(&cache.join(format!("{prefix}-visual.log")), &dir);
            unsafe {
                frida_script_unload_sync(script, ptr::null_mut(), &mut error);
                err(error, "unload")?
            }
        }
        unsafe {
            frida_session_detach_sync(session, ptr::null_mut(), &mut error);
            let _ = err(error, "detach");
            frida_unref(script.cast());
            frida_unref(session.cast());
            frida_unref(dev.cast());
            frida_device_manager_close_sync(manager, ptr::null_mut(), ptr::null_mut());
            frida_unref(manager.cast());
        }
        for p in [tick, result, binding, &lease_mirror, &worker_beat] {
            let _ = fs::remove_file(p);
        }
        let _ = fs::remove_file(cache.join(format!("{prefix}-cleanup.lua")));
        if stop.exists() {
            let _ = fs::remove_file(stop);
            let cleanup_is_confirmed = if active_paths.is_none() {
                true
            } else {
                cleanup_confirmed(&result_snapshot, options)
            };
            if cleanup_is_confirmed {
                let terminal = if active_paths.is_none() {
                    no_hook_terminal_status(options)
                } else {
                    terminal_loop_status(&result_snapshot, calls, options)
                };
                status_with_modes(&dir, &terminal, options);
            } else {
                status_with_modes(
                    &dir,
                    &incomplete_cleanup_status(
                        &result_snapshot,
                        calls,
                        options,
                        "cleanup_unconfirmed",
                    ),
                    options,
                );
            }
            return Ok(());
        }
        if let Some(e) = fatal_exit {
            return Err(format!("stopped after fatal: {e}"));
        }
        if retry_timeout {
            status_with_modes(&dir, "state=waiting_game\nstatus=waiting_game\nreason=cleaned_waiting_rebind\n", options);
            thread::sleep(Duration::from_millis(500));
        }
        active_paths = None;
    }
}
fn main() -> ExitCode {
    let native_diag_args: Vec<String> = std::env::args().skip(1).collect();
    if let Some(code) = frida_diagnostics::dispatch(
        &native_diag_args,
        std::process::id(),
        frida_diagnostics::Api {
            init: frida_init,
            deinit: frida_deinit,
            version_string: frida_version_string,
        },
    ) {
        std::process::exit(code);
    }

    let raw_args: Vec<String> = std::env::args().skip(1).collect();
    let reconcile_mode = raw_args.first().map(String::as_str) == Some("--reconcile");
    let status_dir = raw_args
        .windows(2)
        .find(|w| w[0] == "--dir")
        .map(|w| PathBuf::from(&w[1]));
    let r = std::panic::catch_unwind(|| {
        let args: Vec<String> = std::env::args().skip(1).collect();
        if args.as_slice() == ["--self-test"] {
            return self_test();
        }
        if args.first().map(String::as_str) == Some("--daemon") {
            let Some(i) = args.iter().position(|x| x == "--dir") else {
                return Err("--daemon requires --dir".into());
            };
            let d = PathBuf::from(args.get(i + 1).ok_or("--dir missing")?);
            if !valid_dir(&d) {
                return Err("invalid --dir".into());
            }
            fs::create_dir_all(&d).map_err(|e| e.to_string())?;
            return daemonize(&d);
        }
        if args.first().map(String::as_str) == Some("--stop") {
            let Some(i) = args.iter().position(|x| x == "--dir") else {
                return Err("--stop requires --dir".into());
            };
            let d = PathBuf::from(args.get(i + 1).ok_or("--dir missing")?);
            if !valid_dir(&d) {
                return Err("invalid --dir".into());
            }
            fs::write(d.join("stop"), b"stop\n").map_err(|e| e.to_string())?;
            return Ok(());
        }
        if args.first().map(String::as_str) == Some("--reconcile") {
            let Some(i) = args.iter().position(|x| x == "--dir") else {
                return Err("--reconcile requires --dir".into());
            };
            let d = PathBuf::from(args.get(i + 1).ok_or("--dir missing")?);
            return reconcile(d);
        }
        if args.first().map(String::as_str) == Some("--dir") {
            let d = PathBuf::from(args.get(1).ok_or("--dir missing")?);
            return run(d);
        }
        if args.first().map(String::as_str) == Some("--run") {
            let Some(i) = args.iter().position(|x| x == "--dir") else {
                return Err("--run requires --dir".into());
            };
            let d = PathBuf::from(args.get(i + 1).ok_or("--dir missing")?);
            return run(d);
        }
        Err("usage: battle_loop_worker --daemon --dir <private-files> | --stop --dir <private-files> | --reconcile --dir <private-files> | --self-test".into())
    });
    match r {
        Ok(Ok(())) => ExitCode::SUCCESS,
        Ok(Err(e)) => {
            if let Some(dir) = status_dir.as_deref().filter(|p| valid_dir(p)) {
                if reconcile_mode {
                    let existing = fs::read_to_string(dir.join("recovery-status")).unwrap_or_default();
                    if !existing.starts_with("state=unconfirmed\n") {
                        let _ = write_recovery_status(
                            dir,
                            &format!(
                                "state=unconfirmed\nreason=reconcile_error\nmessage=旧任务检查失败，保护仍保留\ndetail={}\n",
                                e.chars().take(220).collect::<String>().replace(['\r', '\n', '='], " ")
                            ),
                        );
                    }
                } else {
                    let mode_error_fields = if e.starts_with("battle_options_") {
                        "skipBattleRequested=unknown\nhideBattleRequested=unknown\nvisualState=not_started\nhiddenBattles=unknown\nvisualRounds=unknown\nvisibilityRestoreConfirmed=unknown\n"
                    } else {
                        ""
                    };
                    status(
                        dir,
                        &format!("state=error\nstatus=error\nreason={}\n{mode_error_fields}", e.chars().take(220).collect::<String>().replace(['\r', '\n', '='], " ")),
                    );
                }
            }
            eprintln!("worker: {e}");
            ExitCode::FAILURE
        }
        Err(_) => {
            if reconcile_mode {
                if let Some(dir) = status_dir.as_deref().filter(|p| valid_dir(p)) {
                    let existing = fs::read_to_string(dir.join("recovery-status")).unwrap_or_default();
                    if !existing.starts_with("state=unconfirmed\n") {
                        let _ = write_recovery_status(
                            dir,
                            "state=unconfirmed\nreason=reconcile_panic\nmessage=旧任务检查异常中断，保护仍保留\n",
                        );
                    }
                }
            }
            eprintln!("worker panic caught");
            ExitCode::FAILURE
        }
    }
}
