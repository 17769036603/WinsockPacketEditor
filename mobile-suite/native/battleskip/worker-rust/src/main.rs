#[path = "../../../frida_diagnostics.rs"]
mod frida_diagnostics;

#[path = "../../../license_lease.rs"]
mod license_lease;

use std::{
    ffi::{c_char, c_int, c_void, CStr, CString},
    fs,
    io::{self, ErrorKind, Write},
    os::unix::fs::{MetadataExt, PermissionsExt},
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
    fn chown(p: *const c_char, owner: u32, group: u32) -> c_int;
    fn chmod(p: *const c_char, mode: u32) -> c_int;
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
        f.sync_all()?;
        if let Some(uid) = owner {
            if unsafe { fchown(f.as_raw_fd(), uid, u32::MAX) } != 0 {
                return Err(io::Error::last_os_error());
            }
        }
        f.set_permissions(fs::Permissions::from_mode(mode))?;
    }
    fs::rename(&tmp, path)?;
    Ok(())
}
fn replace_atomic_durable(
    path: &Path,
    data: &[u8],
    mode: u32,
    owner: Option<u32>,
) -> io::Result<()> {
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
        file.set_permissions(fs::Permissions::from_mode(mode))?;
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
fn status(dir: &Path, s: &str) {
    let p = dir.join("status");
    let owner = fs::metadata(dir).ok().map(|metadata| metadata.uid());
    let _ = replace_atomic(&p, s.as_bytes(), 0o644, owner);
}
fn write_worker_status(dir: &Path, contents: &str) -> io::Result<()> {
    let owner = fs::metadata(dir)?.uid();
    replace_atomic_durable(&dir.join("status"), contents.as_bytes(), 0o644, Some(owner))
}
fn valid_dir(p: &Path) -> bool {
    let s = p.to_string_lossy();
    p.is_absolute()
        && (s == "/data/user/0/com.xnas.wpe.suite/files/modules/battleskip"
            || s == "/data/data/com.xnas.wpe.suite/files/modules/battleskip")
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
        Err(error) => Err(format!(
            "proc pid directory unreadable for pid {pid}: {error}"
        )),
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
fn game_pids() -> Result<Vec<recovery::ProcessIdentity>, String> {
    let mut out = Vec::new();
    let entries = fs::read_dir("/proc").map_err(|e| format!("proc root unreadable: {e}"))?;
    for entry in entries {
        let entry = entry.map_err(|e| format!("proc directory entry unreadable: {e}"))?;
        let Some(pid) = entry
            .file_name()
            .to_str()
            .and_then(|name| name.parse::<u32>().ok())
        else {
            continue;
        };
        let proc_path = format!("/proc/{pid}");
        let cmdline = match fs::read(format!("{proc_path}/cmdline")) {
            Ok(contents) => contents,
            Err(error) => {
                if proc_pid_gone(pid)? {
                    continue;
                }
                if error.kind() == ErrorKind::NotFound {
                    return Err(format!("proc cmdline missing while pid {pid} remains"));
                }
                return Err(format!("proc cmdline unreadable for pid {pid}: {error}"));
            }
        };
        if first_cmdline_arg(&cmdline) != recovery::TARGET_PACKAGE.as_bytes() {
            continue;
        }

        let stat = match fs::read_to_string(format!("{proc_path}/stat")) {
            Ok(contents) => contents,
            Err(error) => {
                if proc_pid_gone(pid)? {
                    continue;
                }
                if error.kind() == ErrorKind::NotFound {
                    return Err(format!("proc stat missing while target pid {pid} remains"));
                }
                return Err(format!(
                    "target proc stat unreadable for pid {pid}: {error}"
                ));
            }
        };
        let start_ticks = parse_start_ticks(&stat)
            .filter(|ticks| *ticks > 0)
            .ok_or_else(|| format!("target proc start ticks invalid for pid {pid}"))?;
        let exe = match fs::read_link(format!("{proc_path}/exe")) {
            Ok(path) => path
                .to_str()
                .ok_or_else(|| format!("target proc exe is not UTF-8 for pid {pid}"))?
                .to_owned(),
            Err(error) => {
                if proc_pid_gone(pid)? {
                    continue;
                }
                if error.kind() == ErrorKind::NotFound {
                    return Err(format!("proc exe missing while target pid {pid} remains"));
                }
                return Err(format!("target proc exe unreadable for pid {pid}: {error}"));
            }
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
fn boot_observation() -> recovery::BootObservation {
    match read_boot_id() {
        Ok(value) => recovery::BootObservation::Value(value),
        Err(error) if error.kind() == ErrorKind::InvalidData => recovery::BootObservation::Invalid,
        Err(_) => recovery::BootObservation::Unreadable,
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
fn persist_recovery_identity(
    dir: &Path,
    identity: &recovery::RecoveryIdentity,
) -> Result<(), String> {
    let owner = fs::metadata(dir)
        .map_err(|e| format!("module directory metadata: {e}"))?
        .uid();
    replace_atomic_durable(
        &dir.join("recovery-identity"),
        identity.serialize().as_bytes(),
        0o600,
        Some(owner),
    )
    .map_err(|e| format!("persist recovery identity: {e}"))
}
fn persist_run_identity(dir: &Path, identity: &recovery::RecoveryIdentity) -> Result<(), String> {
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
            if !matches!(
                original_process_end_decision(&previous),
                recovery::Decision::VerifiedOldProcess(_)
            ) {
                return Err(
                    "previous recovery identity is live or uncertain; refusing attach".into(),
                );
            }
        }
    }
    persist_recovery_identity(dir, identity)
}
fn write_recovery_status(dir: &Path, contents: &str) -> io::Result<()> {
    let owner = fs::metadata(dir)?.uid();
    replace_atomic_durable(
        &dir.join("recovery-status"),
        contents.as_bytes(),
        0o644,
        Some(owner),
    )
}
fn recovery_message(reason: &str) -> &'static str {
    match reason {
        "worker_running" => "工作进程仍持有运行锁，旧任务状态未确认",
        "worker_lock_unavailable" => "无法安全取得工作进程锁，旧任务状态未确认",
        "recovery_running" => "恢复检查正在运行，旧任务状态未确认",
        "new_run_identity_not_recorded" => "新任务已预留但进程身份尚未记录，旧任务状态未确认",
        "module_status_unreadable" => "模块状态无法读取，旧任务状态未确认",
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
/// 把游戏侧逐回合日志的尾部同步到应用私有目录，便于无需 root 直接查看。
fn sync_rounds_log(source: &Path, dir: &Path) -> std::io::Result<()> {
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
    let owner = fs::metadata(dir)?.uid();
    replace_atomic_durable(&dir.join("rounds.log"), &bytes[start..], 0o644, Some(owner))
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
                "state=unconfirmed\nreason={reason}\nrecoveryModule=battleskip\ncheckedAt={}\nmessage={}\n",
                unix_seconds(),
                recovery_message(reason)
            ),
        )
        .map_err(|e| format!("write recovery-status: {e}")),
        // 锁已空闲：没有 worker 在跑。上一轮是否清理干净不再作为启动前置条件，
        // 只记录当前身份作为下一次核对的基线，并明确标记可启动。
        recovery::Decision::Startable(identity, reason) => {
            let mut body = format!(
                "state=startable\nreason={reason}\nrecoveryModule=battleskip\nlockProbe=free\ncheckedAt={}\n",
                unix_seconds()
            );
            if let Some(identity) = &identity {
                // 写失败只影响下次核对的基线，不阻止启动。
                let _ = persist_recovery_identity(dir, identity);
                body.push_str(&format!(
                    "package={}\npid={}\nstartTicks={}\n",
                    recovery::TARGET_PACKAGE, identity.pid, identity.start_ticks
                ));
            }
            body.push_str(&format!("message={}\n", recovery_message(reason)));
            write_recovery_status(dir, &body).map_err(|e| format!("write recovery-status: {e}"))
        }
        recovery::Decision::VerifiedNoTarget => {
            let terminal = "state=stopped\nprocessEnded=true\nrecoveryVerified=true\nrecoveryModule=battleskip\npackage=com.gdoo.dhxy\nrecoveryProof=no_target_process\n";
            let owner = fs::metadata(dir).map_err(|e| format!("module directory metadata: {e}"))?.uid();
            replace_atomic_durable(&dir.join("status"), terminal.as_bytes(), 0o644, Some(owner))
                .map_err(|e| format!("write terminal status: {e}"))?;
            write_recovery_status(
                dir,
                "state=verified\nrecoveryModule=battleskip\npackage=com.gdoo.dhxy\nrecoveryProof=no_target_process\nmessage=完整进程扫描未发现DHXY主进程，旧任务保护核验通过\n",
            )
            .map_err(|e| format!("write recovery-status: {e}"))
        }
        recovery::Decision::VerifiedOldProcess(identity) => {
            let terminal = format!(
                "state=stopped\nprocessEnded=true\nrecoveryVerified=true\nrecoveryModule=battleskip\npackage={}\nrecoveryProof=original_process_ended\npid={}\nstartTicks={}\n",
                recovery::TARGET_PACKAGE, identity.pid, identity.start_ticks
            );
            let owner = fs::metadata(dir).map_err(|e| format!("module directory metadata: {e}"))?.uid();
            replace_atomic_durable(&dir.join("status"), terminal.as_bytes(), 0o644, Some(owner))
                .map_err(|e| format!("write terminal status: {e}"))?;
            write_recovery_status(
                dir,
                &format!(
                    "state=verified\nrecoveryModule=battleskip\npackage={}\nrecoveryProof=original_process_ended\npid={}\nstartTicks={}\nmessage=旧任务对应的DHXY进程已结束，保护核验通过\n",
                    recovery::TARGET_PACKAGE, identity.pid, identity.start_ticks
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

    let recovery_lock = fs::OpenOptions::new()
        .read(true)
        .write(true)
        .create(true)
        .open(dir.join("recovery.lock"))
        .map_err(|e| format!("open recovery lock: {e}"))?;
    if unsafe { flock(recovery_lock.as_raw_fd(), 2 | 4) } != 0 {
        let error = io::Error::last_os_error();
        if error.kind() == ErrorKind::WouldBlock {
            write_recovery_status(
                &dir,
                "state=unconfirmed\nreason=recovery_running\nrecoveryModule=battleskip\nmessage=恢复检查正在运行，旧任务状态未确认\n",
            )
            .map_err(|e| format!("write recovery-status: {e}"))?;
            return Ok(());
        }
        return Err(format!("lock recovery: {error}"));
    }

    write_recovery_status(
        &dir,
        "state=checking\nrecoveryModule=battleskip\nmessage=正在只读检查旧任务进程身份\n",
    )
    .map_err(|e| format!("write recovery-status: {e}"))?;

    let worker_lock = fs::OpenOptions::new()
        .read(true)
        .write(true)
        .create(true)
        .open(dir.join("worker.lock"));
    let worker_lock = match worker_lock {
        Ok(lock) => lock,
        Err(_) => {
            return apply_recovery_decision(
                &dir,
                recovery::decide(
                    recovery::LockObservation::Unavailable,
                    recovery::StoredIdentity::Missing,
                    recovery::BootObservation::Unreadable,
                    recovery::OriginalProcess::ReadError,
                    recovery::ProcessScan::Error,
                ),
            )
        }
    };
    if unsafe { flock(worker_lock.as_raw_fd(), 2 | 4) } != 0 {
        let error = io::Error::last_os_error();
        let lock = if error.kind() == ErrorKind::WouldBlock {
            recovery::LockObservation::Busy
        } else {
            recovery::LockObservation::Unavailable
        };
        return apply_recovery_decision(
            &dir,
            recovery::decide(
                lock,
                recovery::StoredIdentity::Missing,
                recovery::BootObservation::Unreadable,
                recovery::OriginalProcess::ReadError,
                recovery::ProcessScan::Error,
            ),
        );
    }
    let _worker_lock = worker_lock;

    let module_status = match fs::read_to_string(dir.join("status")) {
        Ok(contents) => contents,
        Err(error) if error.kind() == ErrorKind::NotFound => String::new(),
        Err(_) => {
            return apply_recovery_decision(
                &dir,
                recovery::Decision::Unknown("module_status_unreadable"),
            )
        }
    };
    if recovery::status_has_starting_state(&module_status) {
        // 上次启动在派发前中断：锁现在空闲说明没有 worker 在跑，按当前进程重新启动。
        return apply_recovery_decision(
            &dir,
            recovery::Decision::Startable(None, "start_interrupted"),
        );
    }

    let stored = read_stored_identity(&dir.join("recovery-identity"));
    let boot = boot_observation();
    let mut original_process = recovery::OriginalProcess::ReadError;
    let mut scan = recovery::ProcessScan::Error;
    if let recovery::StoredIdentity::Valid(identity) = &stored {
        if boot == recovery::BootObservation::Value(identity.boot_id.clone()) {
            original_process = inspect_original_process(identity.pid, identity.start_ticks);
        }
    } else {
        // 没有可用记录时扫描当前 DHXY 进程，用它的身份作为新会话基线。
        scan = match game_pids() {
            Ok(processes) => recovery::ProcessScan::Complete(processes),
            Err(error) => {
                eprintln!("reconcile: process scan failed: {error}");
                recovery::ProcessScan::Error
            }
        };
    }

    let decision = recovery::decide(
        recovery::LockObservation::Acquired,
        stored,
        boot,
        original_process,
        scan,
    );
    apply_recovery_decision(&dir, decision)
}
fn write_lua(
    path: &Path,
    template: &str,
    result: &Path,
    binding: &Path,
    run: &str,
    mode: &str,
    player: &str,
    manager: &str,
    lease_path: &Path,
    log_path: &Path,
) -> Result<(), String> {
    let cfg=format!("{{resultPath={},bindingPath={},leasePath={},logPath={},stateKey={},runId={},expectedPlayer={},expectedManagerTostring={},mode={}}}",lua_quote(&result.to_string_lossy()),lua_quote(&binding.to_string_lossy()),lua_quote(&lease_path.to_string_lossy()),lua_quote(&log_path.to_string_lossy()),lua_quote("__codex_apk_battle_skip_v1"),lua_quote(run),lua_quote(player),lua_quote(manager),lua_quote(mode));
    let source = template.replace("__CONFIG__", &cfg);
    let mut f = fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(path)
        .map_err(|e| format!("create {}: {e}", path.display()))?;
    f.write_all(source.as_bytes()).map_err(|e| e.to_string())?;
    f.sync_all().map_err(|e| e.to_string())?;
    f.set_permissions(fs::Permissions::from_mode(0o600))
        .map_err(|e| e.to_string())?;
    Ok(())
}

fn write_lease_mirror(path: &Path, contents: &str, uid: u32) -> Result<(), String> {
    let name = path
        .file_name()
        .ok_or("lease mirror path has no filename")?
        .to_string_lossy();
    let temporary = path.with_file_name(format!("{name}.tmp-{}", std::process::id()));
    let mut file = fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&temporary)
        .map_err(|error| format!("create lease mirror: {error}"))?;
    let result = (|| {
        let temp_c = cstr(&temporary.to_string_lossy())?;
        if unsafe { chown(temp_c.as_ptr(), uid, u32::MAX) } != 0
            || unsafe { chmod(temp_c.as_ptr(), 0o600) } != 0
        {
            return Err("set lease mirror ownership or mode failed".to_owned());
        }
        file.write_all(contents.as_bytes())
            .map_err(|error| error.to_string())?;
        file.sync_all().map_err(|error| error.to_string())?;
        fs::rename(&temporary, path).map_err(|error| format!("publish lease mirror: {error}"))?;
        Ok(())
    })();
    if result.is_err() {
        let _ = fs::remove_file(&temporary);
    }
    result
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
    contents.lines().find_map(|line| line.strip_prefix(key))
}
fn self_test() -> Result<(), String> {
    assert!(valid_dir(Path::new(
        "/data/user/0/com.xnas.wpe.suite/files/modules/battleskip"
    )));
    assert!(!valid_dir(Path::new(
        "/data/data/com.xnas.wpe.suite/files/modules/battleskip/run"
    )));
    assert!(!valid_dir(Path::new("/data/user/0/other/files")));
    assert!(!valid_dir(Path::new(
        "/data/user/0/com.xnas.wpe.suite/files/modules/battleskip/../x"
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
use std::os::fd::AsRawFd;
fn run(dir: PathBuf) -> Result<(), String> {
    if !valid_dir(&dir) {
        return Err("--dir must be the app private files directory".into());
    }
    license_lease::require_module_lease(&dir)?;
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let lock = fs::OpenOptions::new()
        .read(true)
        .write(true)
        .create(true)
        .open(dir.join("worker.lock"))
        .map_err(|e| e.to_string())?;
    if unsafe { flock(lock.as_raw_fd(), 2 | 4) } != 0 {
        return Err("worker already running".into());
    }
    match fs::remove_file(dir.join("recovery-status")) {
        Ok(()) => {}
        Err(error) if error.kind() == ErrorKind::NotFound => {}
        Err(error) => return Err(format!("remove stale recovery-status: {error}")),
    }
    match fs::remove_file(dir.join("recovery-identity")) {
        Ok(()) => {}
        Err(error) if error.kind() == ErrorKind::NotFound => {}
        Err(error) => return Err(format!("remove prior recovery identity: {error}")),
    }
    write_worker_status(
        &dir,
        "state=waiting_game\nstatus=waiting_game\nreason=worker_identity_pending\n",
    )
    .map_err(|e| format!("publish worker identity-pending state: {e}"))?;
    let stop = dir.join("stop");
    let _ = fs::remove_file(&stop);
    let template = include_str!("../../../../app/src/main/assets/battle_skip.lua.in");
    let agent = include_str!("../../../../app/src/main/assets/battle_skip_agent.js");
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
    let mut calls = 0u64;
    loop {
        if license_lease::require_module_lease(&dir).is_err() {
            if !attach_attempted {
                status(&dir, "state=stopped\nhooksRemoved=true\nbindingCleared=true\nreason=activation_lease_expired");
                return Ok(());
            }
            // An attached session handles the transition below and always runs Lua cleanup.
        }
        if stop.exists() {
            if active_paths.is_some() {
                status(&dir, "stopping");
            } else {
                status(
                    &dir,
                    "state=stopped\nhooksRemoved=true\nbindingCleared=true",
                );
                return Ok(());
            }
        }
        let pids = game_pids()?;
        if pids.len() != 1 {
            status(&dir, &format!("waiting_game count={}", pids.len()));
            thread::sleep(Duration::from_secs(1));
            continue;
        }
        let target = pids[0].clone();
        let pid = target.pid;
        let ticks = target.start_ticks;
        let base = format!("codex-apk-skip-{pid}-{ticks}");
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
            let rounds_log = cache.join(format!("{prefix}-rounds.log"));
            for p in [&tick, &cleanup, &result, &binding, &lease_mirror] {
                if p.exists() {
                    return Err(format!("refusing collision at {}", p.display()));
                }
            }
            let lease = license_lease::read_valid_module_lease(&dir)
                .ok_or("activation lease expired before Lua setup")?;
            write_lease_mirror(&lease_mirror, &lease.game_mirror(), uid)?;
            write_lua(
                &tick,
                template,
                &result,
                &binding,
                &run_id,
                "tick",
                "",
                "",
                &lease_mirror,
                &rounds_log,
            )?;
            write_lua(
                &cleanup,
                template,
                &result,
                &binding,
                &run_id,
                "stop",
                "",
                "",
                &lease_mirror,
                &rounds_log,
            )?;
            for p in [&result, &binding] {
                fs::OpenOptions::new()
                    .write(true)
                    .create_new(true)
                    .open(p)
                    .map_err(|e| format!("create {}: {e}", p.display()))?;
            }
            for p in [&tick, &cleanup, &result, &binding, &lease_mirror] {
                let cp = cstr(&p.to_string_lossy())?;
                if unsafe { chown(cp.as_ptr(), uid, u32::MAX) } != 0 {
                    return Err(format!("chown failed for {}", p.display()));
                }
                if unsafe { chmod(cp.as_ptr(), 0o600) } != 0 {
                    return Err(format!("chmod failed for {}", p.display()));
                }
            }
            active_paths = Some((work, tick, result, binding));
        }
        let after = game_pids()?;
        if after.len() != 1 || after[0] != target {
            if let Some((work, tick, result, binding)) = active_paths.take() {
                let cleanup = cache.join(format!("{prefix}-cleanup.lua"));
                for path in [
                    work,
                    tick,
                    result,
                    binding,
                    cleanup,
                    cache.join(format!("{prefix}-lease")),
                ] {
                    let _ = fs::remove_file(path);
                }
            }
            status(&dir, "identity_changed");
            thread::sleep(Duration::from_millis(100));
            continue;
        }
        let (_, tick, result, binding) = active_paths.as_ref().unwrap();
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
        let before_attach = match game_pids() {
            Ok(processes) => processes,
            Err(error) => {
                unsafe {
                    frida_unref(dev.cast());
                    frida_device_manager_close_sync(manager, ptr::null_mut(), ptr::null_mut());
                    frida_unref(manager.cast());
                }
                for path in [
                    &*tick,
                    &*result,
                    &*binding,
                    &cache.join(format!("{prefix}-cleanup.lua")),
                    &cache.join(format!("{prefix}-lease")),
                ] {
                    let _ = fs::remove_file(path);
                }
                status(&dir, "state=error\nreason=identity_read_unconfirmed");
                return Err(format!(
                    "target identity scan failed before attach: {error}"
                ));
            }
        };
        if let Err(lease_error) = license_lease::require_module_lease(&dir) {
            let work_path = active_paths.as_ref().map(|(work, _, _, _)| work.clone());
            unsafe {
                frida_unref(dev.cast());
                frida_device_manager_close_sync(manager, ptr::null_mut(), ptr::null_mut());
                frida_unref(manager.cast());
            }
            for path in [
                &*tick,
                &*result,
                &*binding,
                &cache.join(format!("{prefix}-cleanup.lua")),
                &cache.join(format!("{prefix}-lease")),
            ] {
                let _ = fs::remove_file(path);
            }
            if let Some(work) = work_path {
                let _ = fs::remove_file(work);
            }
            if attach_attempted {
                status(
                    &dir,
                    "state=stopping\nreason=activation_lease_expired_cleanup_unconfirmed",
                );
            } else {
                status(&dir, "state=stopped\nhooksRemoved=true\nbindingCleared=true\nreason=activation_lease_expired");
            }
            if attach_attempted {
                return Err(format!("activation lease expired before attach; previous cleanup unconfirmed: {lease_error}"));
            }
            return Ok(());
        }
        if before_attach.len() != 1 || before_attach[0] != target {
            unsafe {
                frida_unref(dev.cast());
                frida_device_manager_close_sync(manager, ptr::null_mut(), ptr::null_mut());
                frida_unref(manager.cast());
            }
            for path in [
                &*tick,
                &*result,
                &*binding,
                &cache.join(format!("{prefix}-cleanup.lua")),
                &cache.join(format!("{prefix}-lease")),
            ] {
                let _ = fs::remove_file(path);
            }
            active_paths = None;
            status(&dir, "identity_changed_before_attach");
            continue;
        }
        let boot_id = match read_boot_id() {
            Ok(value) => value,
            Err(error) => {
                unsafe {
                    frida_unref(dev.cast());
                    frida_device_manager_close_sync(manager, ptr::null_mut(), ptr::null_mut());
                    frida_unref(manager.cast());
                }
                return Err(format!("boot id unavailable before attach: {error}"));
            }
        };
        let run_identity = recovery::RecoveryIdentity::from_process(&target, boot_id);
        if let Err(error) = persist_run_identity(&dir, &run_identity) {
            unsafe {
                frida_unref(dev.cast());
                frida_device_manager_close_sync(manager, ptr::null_mut(), ptr::null_mut());
                frida_unref(manager.cast());
            }
            return Err(error);
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
        if !matches!(&attached_identity, Ok(processes) if processes.len() == 1 && processes[0] == target)
        {
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
                &cache.join(format!("{prefix}-lease")),
            ] {
                let _ = fs::remove_file(p);
            }
            active_paths = None;
            status(
                &dir,
                if attached_identity.is_err() {
                    "state=error\nreason=identity_read_unconfirmed"
                } else {
                    "identity_changed_after_attach"
                },
            );
            if let Err(error) = attached_identity {
                return Err(format!("target identity scan failed after attach: {error}"));
            }
            continue;
        }
        let opts = unsafe { frida_script_options_new() };
        let name = cstr("battle-skip-worker")?;
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
        let mut cleanup_confirmed = false;
        let mut lease_mirror_at = std::time::Instant::now() - Duration::from_secs(1);
        let mut rounds_log_at = std::time::Instant::now() - Duration::from_secs(2);
        let mut status_at = std::time::Instant::now() - Duration::from_secs(2);
        let mut identity_checked = std::time::Instant::now();
        let mut result_checked = std::time::Instant::now() - Duration::from_millis(250);
        let mut result_snapshot = String::new();
        let mut binding_snapshot = None;
        while !detached {
            if lease_mirror_at.elapsed() >= Duration::from_secs(1) {
                lease_mirror_at = std::time::Instant::now();
                let lease_mirror = cache.join(format!("{prefix}-lease"));
                match license_lease::read_valid_module_lease(&dir) {
                    Some(lease) => {
                        if let Err(error) =
                            write_lease_mirror(&lease_mirror, &lease.game_mirror(), uid)
                        {
                            status(
                                &dir,
                                &format!("state=stopping\nreason=lease_mirror_failed:{error}"),
                            );
                            stopping = true;
                        }
                    }
                    None => {
                        let _ = fs::remove_file(&lease_mirror);
                        status(
                            &dir,
                            "state=stopping\nstatus=stopping\nreason=activation_lease_expired",
                        );
                        stopping = true;
                    }
                }
            }
            if rounds_log_at.elapsed() >= Duration::from_secs(2) {
                rounds_log_at = std::time::Instant::now();
                let _ = sync_rounds_log(&cache.join(format!("{prefix}-rounds.log")), &dir);
            }
            unsafe { _frida_g_main_context_iteration(ptr::null_mut(), 0) };
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
                            "请返回游戏以完成停止"
                        } else {
                            "fatal business error; cleaning up"
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
                    Ok(_) => {
                        status(&dir, "game_gone detached");
                        break;
                    }
                    Err(error) => {
                        status(&dir, "state=stopping\nreason=identity_read_unconfirmed");
                        fatal_exit = Some(format!("identity_read_unconfirmed: {error}"));
                        stopping = true;
                    }
                }
            }
            if result_checked.elapsed() >= Duration::from_millis(250) {
                if let Some((_, _, result, binding)) = &active_paths {
                    result_snapshot = fs::read_to_string(result).unwrap_or_default();
                    if let Some(latest) = parse_calls(&result_snapshot) {
                        calls = latest;
                    }
                    binding_snapshot = read_kv(binding);
                }
                result_checked = std::time::Instant::now();
            }
            if stop.exists() || stopping {
                if !stopping {
                    status(&dir, &format!("running calls={calls}"));
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
                    if parse_result_field(&result_snapshot, "status=") == Some("stopped")
                        && parse_result_field(&result_snapshot, "bindingCleared=") == Some("true")
                        && parse_result_field(&result_snapshot, "hooksRemoved=") == Some("true")
                    {
                        detached = true;
                        cleanup_confirmed = true;
                    } else if cleanup_started.unwrap().elapsed() > Duration::from_secs(5)
                        && !cleanup_prompted
                    {
                        status(
                            &dir,
                            "请返回游戏以完成停止（worker仍保留，cleanup尚未确认）",
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
                    status(&dir, "Lua fatal; cleaning up");
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
                        } else if status_at.elapsed() >= Duration::from_secs(1) {
                            status(&dir, &format!("running calls={calls}"));
                            status_at = std::time::Instant::now();
                        }
                    }
                } else if status_at.elapsed() >= Duration::from_secs(1) {
                    status(&dir, "waiting_manager_or_local_role");
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
        for p in [
            tick,
            result,
            binding,
            &cache.join(format!("{prefix}-lease")),
        ] {
            let _ = fs::remove_file(p);
        }
        if cleanup_confirmed {
            attach_attempted = false;
        }
        let _ = fs::remove_file(cache.join(format!("{prefix}-cleanup.lua")));
        if stop.exists() {
            let _ = fs::remove_file(stop);
            if active_paths.is_none() || cleanup_confirmed {
                status(
                    &dir,
                    "state=stopped\nhooksRemoved=true\nbindingCleared=true",
                );
            } else {
                status(&dir, "state=stopping\nreason=cleanup_unconfirmed");
            }
            return Ok(());
        }
        if let Some(e) = fatal_exit {
            return Err(format!("stopped after fatal: {e}"));
        }
        if retry_timeout {
            status(&dir, "GL thread恢复后已清理，等待重新绑定");
            thread::sleep(Duration::from_secs(2));
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
        Err("usage: battle_skip_worker --daemon --dir <private-files> | --stop --dir <private-files> | --reconcile --dir <private-files> | --self-test".into())
    });
    match r {
        Ok(Ok(())) => ExitCode::SUCCESS,
        Ok(Err(e)) => {
            if let Some(dir) = status_dir.as_deref().filter(|p| valid_dir(p)) {
                if reconcile_mode {
                    let existing =
                        fs::read_to_string(dir.join("recovery-status")).unwrap_or_default();
                    if !existing.starts_with("state=unconfirmed\n") {
                        let _ = write_recovery_status(
                            dir,
                            &format!(
                                "state=unconfirmed\nreason=reconcile_error\nrecoveryModule=battleskip\ndetail={}\n",
                                e.chars()
                                    .take(220)
                                    .collect::<String>()
                                    .replace(['\r', '\n', '='], " ")
                            ),
                        );
                    }
                } else {
                    status(
                        dir,
                        &format!("error {}", e.chars().take(220).collect::<String>()),
                    );
                }
            }
            eprintln!("worker: {e}");
            ExitCode::FAILURE
        }
        Err(_) => {
            if reconcile_mode {
                if let Some(dir) = status_dir.as_deref().filter(|p| valid_dir(p)) {
                    let existing =
                        fs::read_to_string(dir.join("recovery-status")).unwrap_or_default();
                    if !existing.starts_with("state=unconfirmed\n") {
                        let _ = write_recovery_status(
                            dir,
                            "state=unconfirmed\nreason=reconcile_panic\nrecoveryModule=battleskip\nmessage=旧任务检查异常中断，保护仍保留\n",
                        );
                    }
                }
            }
            eprintln!("worker panic caught");
            ExitCode::FAILURE
        }
    }
}
