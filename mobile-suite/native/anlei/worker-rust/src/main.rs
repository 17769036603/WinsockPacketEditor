#[path = "../../../frida_diagnostics.rs"]
mod frida_diagnostics;

use std::{
    ffi::{c_char, c_int, c_void, CStr, CString},
    fs,
    io::{self, Write},
    os::fd::AsRawFd,
    os::unix::fs::{MetadataExt, PermissionsExt},
    os::unix::process::CommandExt,
    path::{Path, PathBuf},
    process::{Command, ExitCode, Stdio},
    ptr,
    sync::{mpsc, Mutex, OnceLock},
    thread,
    time::{Duration, SystemTime, UNIX_EPOCH},
};

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
    fn chown(p: *const c_char, owner: u32, group: u32) -> c_int;
    fn fchown(fd: c_int, owner: u32, group: u32) -> c_int;
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
fn prepare_atomic_temp(
    path: &Path,
    data: &[u8],
    mode: u32,
    owner: Option<u32>,
) -> io::Result<PathBuf> {
    let tmp = path.with_extension(format!("tmp-{}", std::process::id()));
    let mut f = fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&tmp)?;
    let setup = (|| {
        f.write_all(data)?;
        f.sync_all()?;
        if let Some(uid) = owner {
            if unsafe { fchown(f.as_raw_fd(), uid, u32::MAX) } != 0 {
                return Err(io::Error::last_os_error());
            }
        }
        f.set_permissions(fs::Permissions::from_mode(mode))?;
        f.sync_all()
    })();
    if let Err(error) = setup {
        drop(f);
        let _ = fs::remove_file(&tmp);
        return Err(error);
    }
    Ok(tmp)
}
fn replace_atomic_with_owner(
    path: &Path,
    data: &[u8],
    mode: u32,
    owner: Option<u32>,
) -> io::Result<()> {
    let tmp = prepare_atomic_temp(path, data, mode, owner)?;
    if let Err(error) = fs::rename(&tmp, path) {
        let _ = fs::remove_file(&tmp);
        return Err(error);
    }
    Ok(())
}
fn replace_atomic(path: &Path, data: &[u8], mode: u32) -> io::Result<()> {
    replace_atomic_with_owner(path, data, mode, None)
}
fn create_atomic_owned(path: &Path, data: &[u8], mode: u32, uid: u32) -> io::Result<()> {
    let tmp = prepare_atomic_temp(path, data, mode, Some(uid))?;
    // Linking publishes the fully configured inode atomically and fails if a
    // colliding destination appeared after the caller's collision check.
    if let Err(error) = fs::hard_link(&tmp, path) {
        let _ = fs::remove_file(&tmp);
        return Err(error);
    }
    fs::remove_file(tmp)?;
    Ok(())
}
fn status(dir: &Path, s: &str) {
    let p = dir.join("status");
    if replace_atomic(&p, s.as_bytes(), 0o644).is_ok() {
        if let (Ok(owner), Ok(path)) = (fs::metadata(dir), CString::new(p.to_string_lossy().as_bytes())) {
            unsafe { let _ = chown(path.as_ptr(), owner.uid(), u32::MAX); }
        }
    }
}
fn clear_stop_file(path: &Path) -> Result<(), String> {
    match fs::remove_file(path) {
        Ok(()) => Ok(()),
        Err(e) if e.kind() == io::ErrorKind::NotFound => Ok(()),
        Err(e) => Err(format!("clear stop file: {e}")),
    }
}
fn write_game_file(path: &Path, value: &str, uid: u32) -> Result<(), String> {
    replace_atomic_with_owner(path, value.as_bytes(), 0o644, Some(uid))
        .map_err(|e| format!("write {}: {e}", path.display()))
}
fn valid_dir(p: &Path) -> bool {
    let s = p.to_string_lossy();
    p.is_absolute()
        && (s == "/data/user/0/com.xnas.wpe.suite/files/modules/anlei"
            || s == "/data/data/com.xnas.wpe.suite/files/modules/anlei")
}
fn proc_start(pid: u32) -> Option<u64> {
    let s = fs::read_to_string(format!("/proc/{pid}/stat")).ok()?;
    parse_start_ticks(&s)
}
fn parse_proc_stat(stat: &str) -> Option<(char, u64)> {
    let fields = stat
        .rsplit_once(") ")?
        .1
        .split_whitespace()
        .collect::<Vec<_>>();
    let state = fields.first()?.chars().next()?;
    let start_ticks = fields.get(19)?.parse().ok()?;
    Some((state, start_ticks))
}
fn parse_start_ticks(stat: &str) -> Option<u64> {
    parse_proc_stat(stat).map(|(_, start_ticks)| start_ticks)
}
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum BoundProcessState {
    Alive,
    Exited,
    Replaced,
    Unconfirmed,
}
fn classify_bound_process(
    expected_ticks: u64,
    stat: Option<(char, u64)>,
    proc_entry_missing: bool,
    exe: Option<&Path>,
    expected_exe: &Path,
) -> BoundProcessState {
    let Some((state, ticks)) = stat else {
        return if proc_entry_missing {
            BoundProcessState::Exited
        } else {
            BoundProcessState::Unconfirmed
        };
    };
    if ticks != expected_ticks {
        return BoundProcessState::Replaced;
    }
    if matches!(state, 'Z' | 'X' | 'x') {
        return BoundProcessState::Exited;
    }
    if exe == Some(expected_exe) {
        BoundProcessState::Alive
    } else {
        BoundProcessState::Unconfirmed
    }
}
fn bound_process_state(pid: u32, ticks: u64, expected_exe: &Path) -> BoundProcessState {
    let proc_dir = PathBuf::from(format!("/proc/{pid}"));
    let stat = fs::read_to_string(proc_dir.join("stat"))
        .ok()
        .and_then(|stat| parse_proc_stat(&stat));
    let proc_entry_missing = matches!(
        fs::metadata(&proc_dir),
        Err(ref error) if error.kind() == io::ErrorKind::NotFound
    );
    let exe = fs::read_link(proc_dir.join("exe")).ok();
    classify_bound_process(
        ticks,
        stat,
        proc_entry_missing,
        exe.as_deref(),
        expected_exe,
    )
}
fn first_cmdline_arg(cmdline: &[u8]) -> &[u8] {
    cmdline.split(|b| *b == 0).next().unwrap_or_default()
}
fn lease_timestamp_fresh(timestamp: u64, now: u64) -> bool {
    timestamp <= now && now - timestamp <= 5
}
fn lease_fresh(path: &Path) -> bool {
    let now = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs();
    fs::read_to_string(path)
        .ok()
        .and_then(|s| s.trim().parse::<u64>().ok())
        .is_some_and(|timestamp| lease_timestamp_fresh(timestamp, now))
}
fn game_pids() -> Vec<(u32, u64)> {
    let mut out = Vec::new();
    let Ok(entries) = fs::read_dir("/proc") else {
        return out;
    };
    for e in entries.flatten() {
        let Ok(pid) = e.file_name().to_string_lossy().parse::<u32>() else {
            continue;
        };
        let cmd = fs::read(format!("/proc/{pid}/cmdline")).unwrap_or_default();
        if first_cmdline_arg(&cmd) == b"com.gdoo.dhxy" {
            if let (Some(t), Ok(exe)) = (proc_start(pid), fs::read_link(format!("/proc/{pid}/exe")))
            {
                if exe.to_string_lossy().contains("app_process") {
                    out.push((pid, t));
                }
            }
        }
    }
    out
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
    lease: &Path,
    worker: &Path,
    uid: u32,
) -> Result<(), String> {
    let cfg=format!("{{resultPath={},bindingPath={},leasePath={},workerPath={},stateKey={},runId={},expectedPlayer={},expectedManagerTostring={},mode={}}}",lua_quote(&result.to_string_lossy()),lua_quote(&binding.to_string_lossy()),lua_quote(&lease.to_string_lossy()),lua_quote(&worker.to_string_lossy()),lua_quote("__codex_anlei_v1"),lua_quote(run),lua_quote(player),lua_quote(manager),lua_quote(mode));
    let source = template.replace("__CONFIG__", &cfg);
    create_atomic_owned(path, source.as_bytes(), 0o600, uid)
        .map_err(|e| format!("create {}: {e}", path.display()))
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
fn self_test() -> Result<(), String> {
    assert!(valid_dir(Path::new(
        "/data/user/0/com.xnas.wpe.suite/files/modules/anlei"
    )));
    assert!(!valid_dir(Path::new(
        "/data/data/com.xnas.wpe.suite/files/modules/anlei/run"
    )));
    assert!(!valid_dir(Path::new("/data/user/0/other/files")));
    assert!(!valid_dir(Path::new(
        "/data/user/0/com.xnas.wpe.suite/files/modules/anlei/../x"
    )));
    let stat = format!(
        "1833 (name with ) spaces) S {}",
        (4..=22)
            .map(|n| n.to_string())
            .collect::<Vec<_>>()
            .join(" ")
    );
    assert_eq!(parse_start_ticks(&stat), Some(22));
    assert_eq!(parse_proc_stat(&stat), Some(('S', 22)));
    assert!(lease_timestamp_fresh(100, 105));
    assert!(lease_timestamp_fresh(100, 100));
    assert!(!lease_timestamp_fresh(100, 106));
    assert!(!lease_timestamp_fresh(101, 100));
    let exe = Path::new("/system/bin/app_process64");
    assert_eq!(
        classify_bound_process(22, Some(('S', 22)), false, Some(exe), exe),
        BoundProcessState::Alive
    );
    assert_eq!(
        classify_bound_process(22, None, true, None, exe),
        BoundProcessState::Exited
    );
    assert_eq!(
        classify_bound_process(22, None, false, None, exe),
        BoundProcessState::Unconfirmed
    );
    assert_eq!(
        classify_bound_process(22, Some(('S', 23)), false, Some(exe), exe),
        BoundProcessState::Replaced
    );
    assert_eq!(
        classify_bound_process(22, Some(('S', 22)), false, None, exe),
        BoundProcessState::Unconfirmed
    );
    assert_eq!(
        classify_bound_process(22, Some(('Z', 22)), false, None, exe),
        BoundProcessState::Exited
    );
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
    let stop = dir.join("stop");
    let template = include_str!("../../../../app/src/main/assets/anlei.lua.in");
    let agent = include_str!("../../../../app/src/main/assets/anlei_agent.js");
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
        if active_paths.is_none() && stop.exists() {
            clear_stop_file(&stop)?;
            status(&dir, "stopped");
            return Ok(());
        }
        if !lease_fresh(&dir.join("lease")) {
            status(&dir, "app lease expired; worker stopped");
            return Ok(());
        }
        if stop.exists() {
            if active_paths.is_some() {
                status(&dir, "stopping");
            } else {
                clear_stop_file(&stop)?;
                status(&dir, "stopped");
                return Ok(());
            }
        }
        let pids = game_pids();
        if pids.len() != 1 {
            status(&dir, &format!("waiting_game count={}", pids.len()));
            thread::sleep(Duration::from_secs(1));
            continue;
        }
        let (pid, ticks) = pids[0];
        let exe = match fs::read_link(format!("/proc/{pid}/exe")) {
            Ok(exe) => exe,
            Err(_) => {
                status(&dir, "waiting_game");
                thread::sleep(Duration::from_secs(1));
                continue;
            }
        };
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
            let worker_beat = cache.join(format!("{prefix}-worker-heartbeat"));
            for p in [&tick, &cleanup, &result, &binding, &lease_mirror, &worker_beat] {
                if p.exists() {
                    return Err(format!("refusing collision at {}", p.display()));
                }
            }
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
                &worker_beat,
                uid,
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
                &worker_beat,
                uid,
            )?;
            for p in [&result, &binding, &lease_mirror, &worker_beat] {
                create_atomic_owned(p, b"", 0o600, uid)
                    .map_err(|e| format!("create {}: {e}", p.display()))?;
            }
            let initial_lease =
                fs::read_to_string(dir.join("lease")).unwrap_or_else(|_| "0".into());
            write_game_file(&lease_mirror, initial_lease.trim(), uid)?;
            write_game_file(
                &worker_beat,
                &SystemTime::now()
                    .duration_since(UNIX_EPOCH)
                    .unwrap_or_default()
                    .as_secs()
                    .to_string(),
                uid,
            )?;
            active_paths = Some((work, tick, result, binding));
        }
        let (_, tick, result, binding) = active_paths.as_ref().unwrap();
        let lease_mirror = cache.join(format!("{prefix}-lease"));
        let worker_beat = cache.join(format!("{prefix}-worker-heartbeat"));
        let after = game_pids();
        if after.len() != 1
            || after[0] != (pid, ticks)
            || fs::read_link(format!("/proc/{pid}/exe")).ok() != Some(exe.clone())
        {
            // These files were staged by this exact run_id and no Frida session has
            // attached yet. Remove only this run's inputs before selecting a new process.
            for path in [
                &*tick,
                &cache.join(format!("{prefix}-cleanup.lua")),
                &*result,
                &*binding,
                &lease_mirror,
                &worker_beat,
            ] {
                let _ = fs::remove_file(path);
            }
            active_paths = None;
            status(&dir, "identity_changed");
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
        license_lease::require_module_lease(&dir)?;
        let session = unsafe {
            frida_device_attach_sync(dev, pid, ptr::null_mut(), ptr::null_mut(), &mut error)
        };
        unsafe { err(error, "attach")? }
        if session.is_null() {
            return Err("attach returned null".into());
        }
        let attached_identity = game_pids();
        if attached_identity.len() != 1
            || attached_identity[0] != (pid, ticks)
            || fs::read_link(format!("/proc/{pid}/exe")).ok() != Some(exe.clone())
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
                &lease_mirror,
                &worker_beat,
                &cache.join(format!("{prefix}-cleanup.lua")),
            ] {
                let _ = fs::remove_file(p);
            }
            active_paths = None;
            status(&dir, "identity_changed_after_attach");
            continue;
        }
        let opts = unsafe { frida_script_options_new() };
        let name = cstr("anlei-worker")?;
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
        let mut terminal_detach = false;
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
            if beat_at.elapsed() >= Duration::from_secs(1) && !stopping {
                let app_lease = fs::read_to_string(dir.join("lease")).unwrap_or_else(|_| "0".into());
                let beat = SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_secs().to_string();
                let lease_error = write_game_file(&lease_mirror, app_lease.trim(), uid).err();
                let beat_error = write_game_file(&worker_beat, &beat, uid).err();
                beat_at = std::time::Instant::now();
                if let Some(error) = lease_error.or(beat_error) {
                    let file_error = format!("game file update failed: {error}");
                    if let Some(existing) = fatal_exit.as_mut() {
                        existing.push_str("; ");
                        existing.push_str(&file_error);
                    } else {
                        fatal_exit = Some(file_error);
                    }
                    status(&dir, "game file update failed; cleaning Lua state");
                    stopping = true;
                }
                if !lease_fresh(&dir.join("lease")) || license_lease::require_module_lease(&dir).is_err() {
                    status(&dir, "app lease expired; cleaning Lua state");
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
                            "请返回游戏以完成停止"
                        } else {
                            "fatal business error; cleaning up"
                        },
                    );
                    stopping = true;
                }
            }
            if identity_checked.elapsed() >= Duration::from_secs(1) {
                identity_checked = std::time::Instant::now();
                match bound_process_state(pid, ticks, &exe) {
                    BoundProcessState::Exited | BoundProcessState::Replaced => {
                        terminal_detach = true;
                        detached = true;
                        status(
                            &dir,
                            "game_gone; Lua cleanup and hook removal unconfirmed; detaching old Frida session",
                        );
                        break;
                    }
                    BoundProcessState::Alive => {
                        let ident = game_pids();
                        if ident.len() != 1 || ident[0] != (pid, ticks) {
                            status(
                                &dir,
                                "identity_unconfirmed; cleaning old session before rebind",
                            );
                            stopping = true;
                        }
                    }
                    BoundProcessState::Unconfirmed => {
                        status(
                            &dir,
                            "identity_unconfirmed; cleaning old session before rebind",
                        );
                        stopping = true;
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
                    {
                        detached = true;
                    }
                }
                result_checked = std::time::Instant::now();
            }
            if stop.exists() || stopping {
                if !stopping {
                    let state = parse_result_field(&result_snapshot, "status=").unwrap_or("waiting_role");
                    let map = parse_result_field(&result_snapshot, "map=").unwrap_or("0");
                    status(&dir, &format!("{state} calls={calls} map={map}"));
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
                        } else if status_at.elapsed() >= Duration::from_millis(100) {
                            let state = parse_result_field(&result_snapshot, "status=").unwrap_or("waiting_role");
                            let map = parse_result_field(&result_snapshot, "map=").unwrap_or("0");
                            status(&dir, &format!("{state} calls={calls} map={map}"));
                            status_at = std::time::Instant::now();
                        }
                    }
                } else if status_at.elapsed() >= Duration::from_millis(100) {
                    status(&dir, "waiting_manager_or_local_role");
                    status_at = std::time::Instant::now();
                }
            }
            thread::sleep(Duration::from_millis(40));
        }
        let mut unload_error = None;
        if detached && !terminal_detach {
            unsafe {
                frida_script_unload_sync(script, ptr::null_mut(), &mut error);
                let unload_result = err(error, "unload");
                error = ptr::null_mut();
                if let Err(unload_error_message) = unload_result {
                    unload_error = Some(unload_error_message);
                }
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
            if let Err(clear_error) = clear_stop_file(&stop) {
                return Err(match unload_error {
                    Some(unload_error_message) => {
                        format!("{unload_error_message}; {clear_error}")
                    }
                    None => clear_error,
                });
            }
            if terminal_detach {
                status(
                    &dir,
                    "state=stopped\nreason=original_process_ended\nprocessEnded=true\nluaCleanupConfirmed=false\nsessionDetached=true\n",
                );
            } else {
                status(&dir, "stopped");
            }
            return match unload_error {
                Some(error) => Err(format!("unload after cleanup: {error}")),
                None => Ok(()),
            };
        }
        if let Some(e) = fatal_exit {
            return Err(match unload_error {
                Some(unload_error_message) => {
                    format!("stopped after fatal: {e}; {unload_error_message}")
                }
                None => format!("stopped after fatal: {e}"),
            });
        }
        if let Some(error) = unload_error {
            return Err(format!("unload after cleanup: {error}"));
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
        Err("usage: anlei_worker --daemon --dir <private-files> | --stop --dir <private-files> | --self-test".into())
    });
    match r {
        Ok(Ok(())) => ExitCode::SUCCESS,
        Ok(Err(e)) => {
            if let Some(dir) = status_dir.as_deref().filter(|p| valid_dir(p)) {
                status(
                    dir,
                    &format!("error {}", e.chars().take(220).collect::<String>()),
                );
            }
            eprintln!("worker: {e}");
            ExitCode::FAILURE
        }
        Err(_) => {
            eprintln!("worker panic caught");
            ExitCode::FAILURE
        }
    }
}
#[path = "../../../license_lease.rs"]
mod license_lease;
