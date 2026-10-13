use std::{
    ffi::{c_char, c_int, c_void, CStr, CString},
    fs,
    io::{self, ErrorKind, Write},
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
) -> Result<(), String> {
    let cfg=format!("{{resultPath={},bindingPath={},leasePath={},workerPath={},stateKey={},runId={},expectedPlayer={},expectedManagerTostring={},mode={}}}",lua_quote(&result.to_string_lossy()),lua_quote(&binding.to_string_lossy()),lua_quote(&lease.to_string_lossy()),lua_quote(&worker.to_string_lossy()),lua_quote("__codex_battle_loop_v1"),lua_quote(run),lua_quote(player),lua_quote(manager),lua_quote(mode));
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
fn publish_loop_status(dir: &Path, snapshot: &str, calls: u64) {
    let field = |key: &str| {
        parse_result_field(snapshot, key)
            .unwrap_or("unknown")
            .replace(['\r', '\n', '='], " ")
    };
    let state = parse_result_field(snapshot, "status=")
        .or_else(|| parse_result_field(snapshot, "state="))
        .unwrap_or("waiting_role")
        .replace(['\r', '\n', '='], " ");
    status(
        dir,
        &format!(
            "status={state}\nstate={state}\ncalls={calls}\ncharacterSkillId={}\npetId={}\npetSkillId={}\nround={}\nrestoreConfirmed={}\nhooksRemoved={}\nbindingCleared={}\n",
            field("characterSkillId="),
            field("petId="),
            field("petSkillId="),
            field("round="),
            field("restoreConfirmed="),
            field("hooksRemoved="),
            field("bindingCleared=")
        ),
    );
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
        "identity_unreadable" => "旧进程身份记录无法读取，保护仍保留",
        "identity_corrupt" => "旧进程身份记录损坏，保护仍保留",
        "boot_id_unreadable" => "系统启动身份无法读取，保护仍保留",
        "boot_id_invalid" => "系统启动身份格式无效，保护仍保留",
        "original_process_unreadable" => "原游戏进程信息无法完整读取，保护仍保留",
        "original_process_exe_changed" => "原进程号仍在运行但程序身份变化，保护仍保留",
        "original_process_cmdline_changed" => "原进程号仍在运行但命令行身份变化，保护仍保留",
        "original_process_identity_invalid" => "原进程身份读取结果无效，保护仍保留",
        "process_scan_unreadable" => "无法完整扫描游戏进程，保护仍保留",
        "multiple_target_processes" => "发现多个DHXY主进程，无法确认旧任务状态",
        "process_identity_invalid" => "游戏进程身份信息无效，保护仍保留",
        _ => "旧任务状态无法确认，保护仍保留",
    }
}
fn apply_recovery_decision(dir: &Path, decision: recovery::Decision) -> Result<(), String> {
    match decision {
        recovery::Decision::Unknown(reason) => write_recovery_status(
            dir,
            &format!(
                "state=unconfirmed\nreason={reason}\nmessage={}\n",
                recovery_message(reason)
            ),
        )
        .map_err(|e| format!("write recovery-status: {e}")),
        recovery::Decision::Observe(identity) => {
            if let Err(error) = write_recovery_identity(dir, &identity) {
                let _ = write_recovery_status(
                    dir,
                    &format!(
                        "state=unconfirmed\nreason=identity_write_failed\nmessage=旧进程身份无法安全保存，保护仍保留\ndetail={}\n",
                        error
                    ),
                );
                return Ok(());
            }
            write_recovery_status(
                dir,
                &format!(
                    "state=waiting_restart\nreason=legacy_baseline_saved\npid={}\nstartTicks={}\nmessage=已记录当前DHXY进程身份；请重启游戏后再次检查\n",
                    identity.pid, identity.start_ticks
                ),
            )
            .map_err(|e| format!("write recovery-status: {e}"))
        }
        recovery::Decision::Waiting(identity) => write_recovery_status(
            dir,
            &format!(
                "state=waiting_restart\nreason=original_process_alive\npid={}\nstartTicks={}\nmessage=旧任务对应的DHXY进程仍在运行；请重启游戏后再次检查\n",
                identity.pid, identity.start_ticks
            ),
        )
        .map_err(|e| format!("write recovery-status: {e}")),
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
    } else if stored == recovery::StoredIdentity::Missing {
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
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let lock = open_worker_lock(&dir).map_err(|e| e.to_string())?;
    if lock_worker(&lock).is_err() {
        return Err("worker already running".into());
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
    let mut calls = 0u64;
    loop {
        if !lease_fresh(&dir.join("lease")) {
            status(&dir, "state=stopping\nstatus=stopping\nreason=app_lease_expired");
            return Ok(());
        }
        if stop.exists() {
            if active_paths.is_some() {
                status(&dir, "state=stopping\nstatus=stopping");
            } else {
                status(&dir, "state=stopped\nstatus=stopped\nhooksRemoved=true\nbindingCleared=true\nrestoreConfirmed=true");
                return Ok(());
            }
        }
        let pids = match game_pids() {
            Ok(pids) => pids,
            Err(_) => {
                status(&dir, "state=waiting_game\nstatus=waiting_game\ngameCount=unknown\nreason=process_scan_unknown");
                thread::sleep(Duration::from_secs(1));
                continue;
            }
        };
        if pids.len() != 1 {
            status(&dir, &format!("state=waiting_game\nstatus=waiting_game\ngameCount={}", pids.len()));
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
                status(&dir, "state=waiting_game\nstatus=waiting_game\nreason=identity_changed_before_attach");
                continue;
            }
            Err(_) => {
                status(&dir, "state=waiting_game\nstatus=waiting_game\ngameCount=unknown\nreason=process_scan_unknown");
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
            for p in [&tick, &cleanup, &result, &binding, &lease_mirror, &worker_beat] {
                if p.exists() {
                    return Err(format!("refusing collision at {}", p.display()));
                }
            }
            write_lua(&tick, template, &result, &binding, &run_id, "tick", uid, "", "", &lease_mirror, &worker_beat)?;
            write_lua(
                &cleanup, template, &result, &binding, &run_id, "stop", uid, "", "", &lease_mirror, &worker_beat,
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
            status(&dir, "state=waiting_game\nstatus=waiting_game\nreason=identity_changed");
            continue;
        }
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
            status(&dir, "state=waiting_game\nstatus=waiting_game\nreason=identity_changed_after_attach");
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
        let mut result_snapshot = String::new();
        let mut binding_snapshot = None;
        while !detached {
            unsafe { _frida_g_main_context_iteration(ptr::null_mut(), 0) };
            if beat_at.elapsed() >= Duration::from_secs(1) {
                let app_lease = fs::read_to_string(dir.join("lease")).unwrap_or_else(|_| "0".into());
                let beat = SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_secs().to_string();
                write_game_file(&lease_mirror, app_lease.trim(), uid);
                write_game_file(&worker_beat, &beat, uid);
                beat_at = std::time::Instant::now();
                if !lease_fresh(&dir.join("lease")) {
                    status(&dir, "state=stopping\nstatus=stopping\nreason=app_lease_expired");
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
                    status(
                        &dir,
                        if timeout {
                            "state=stopping\nstatus=stopping\nreason=gl_thread_queue_timeout"
                        } else {
                            "state=stopping\nstatus=stopping\nreason=fatal_business_error"
                        },
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
                        eprintln!("worker: target identity scan failed: {error}");
                        status(
                            &dir,
                            "state=stopping\nstatus=stopping\nreason=identity_read_unconfirmed",
                        );
                        fatal_exit = Some("identity_read_unconfirmed".into());
                        stopping = true;
                    }
                    Ok(_) => {
                        if matches!(
                            original_process_end_decision(&run_identity),
                            recovery::Decision::VerifiedOldProcess(_)
                        ) {
                            status(
                                &dir,
                                "state=waiting_game\nstatus=waiting_game\nreason=original_process_ended",
                            );
                            detached = true;
                        } else {
                            status(
                                &dir,
                                "state=stopping\nstatus=stopping\nreason=identity_changed_unconfirmed",
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
                    if result_snapshot.contains("status=stopped")
                        && result_snapshot.contains("hooksRemoved=true")
                        && result_snapshot.contains("bindingCleared=true")
                        && parse_result_field(&result_snapshot, "restoreConfirmed=") == Some("true")
                    {
                        detached = true;
                    }
                }
                result_checked = std::time::Instant::now();
            }
            if stop.exists() || stopping {
                if !stopping {
                    publish_loop_status(&dir, &result_snapshot, calls);
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
                    if result_snapshot.contains("status=stopped")
                        && result_snapshot.contains("bindingCleared=true")
                        && result_snapshot.contains("hooksRemoved=true")
                    {
                        detached = true;
                    } else if cleanup_started.unwrap().elapsed() > Duration::from_secs(5)
                        && !cleanup_prompted
                    {
                        status(
                            &dir,
                            "state=stopping\nstatus=stopping\nreason=cleanup_unconfirmed",
                        );
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
                    status(&dir, "state=stopping\nstatus=stopping\nreason=lua_fatal");
                    stopping = true;
                }
                if let Some((bound_run, player, manager_text)) = &binding_snapshot {
                    if bound_run == &run_id {
                        let uid_ok = active_paths.as_ref().is_some_and(|(_, _, _, binding)| {
                            fs::metadata(binding).is_ok_and(|m| m.uid() == uid)
                        });
                        if !uid_ok || player.is_empty() || manager_text.is_empty() {
                            fatal_exit = Some("binding owner or identity validation failed".into());
                            stopping = true;
                        } else if status_at.elapsed() >= Duration::from_millis(100) {
                            publish_loop_status(&dir, &result_snapshot, calls);
                            status_at = std::time::Instant::now();
                        }
                    }
                } else if status_at.elapsed() >= Duration::from_millis(100) {
                    publish_loop_status(&dir, &result_snapshot, calls);
                    status_at = std::time::Instant::now();
                }
            }
            thread::sleep(Duration::from_millis(40));
        }
        if detached {
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
            let cleanup_confirmed = active_paths.is_none()
                || (parse_result_field(&result_snapshot, "status=") == Some("stopped")
                    && parse_result_field(&result_snapshot, "hooksRemoved=") == Some("true")
                    && parse_result_field(&result_snapshot, "bindingCleared=") == Some("true")
                    && parse_result_field(&result_snapshot, "restoreConfirmed=") == Some("true"));
            if cleanup_confirmed {
                status(&dir, "state=stopped\nstatus=stopped\nhooksRemoved=true\nbindingCleared=true\nrestoreConfirmed=true");
            } else {
                status(&dir, "state=stopping\nstatus=stopping\nreason=cleanup_unconfirmed");
            }
            return Ok(());
        }
        if let Some(e) = fatal_exit {
            return Err(format!("stopped after fatal: {e}"));
        }
        if retry_timeout {
            status(&dir, "state=waiting_game\nstatus=waiting_game\nreason=cleaned_waiting_rebind");
            thread::sleep(Duration::from_secs(2));
        }
        active_paths = None;
    }
}
fn main() -> ExitCode {
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
                    status(
                        dir,
                        &format!("state=error\nstatus=error\nreason={}", e.chars().take(220).collect::<String>().replace(['\r', '\n', '='], " ")),
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
