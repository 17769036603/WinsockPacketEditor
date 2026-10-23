use std::{
    ffi::{c_char, c_int, c_void, CStr, CString},
    fs,
    io::{Read, Write},
    os::fd::AsRawFd,
    os::unix::fs::{MetadataExt, PermissionsExt},
    path::{Path, PathBuf},
    ptr,
    sync::{
        atomic::{AtomicBool, Ordering},
        Mutex,
    },
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
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
        pid: u32,
        _: *mut c_void,
        _: *mut c_void,
        _: *mut *mut GError,
    ) -> *mut Session;
    fn frida_session_create_script_sync(
        _: *mut Session,
        source: *const c_char,
        options: *mut ScriptOptions,
        _: *mut c_void,
        error: *mut *mut GError,
    ) -> *mut Script;
    fn frida_script_options_new() -> *mut ScriptOptions;
    fn frida_script_options_set_name(_: *mut ScriptOptions, name: *const c_char);
    fn frida_script_options_set_runtime(_: *mut ScriptOptions, runtime: c_int);
    fn frida_script_load_sync(_: *mut Script, _: *mut c_void, error: *mut *mut GError);
    fn frida_script_unload_sync(_: *mut Script, _: *mut c_void, error: *mut *mut GError);
    fn frida_session_detach_sync(_: *mut Session, _: *mut c_void, error: *mut *mut GError);
    fn frida_session_is_detached(_: *mut Session) -> c_int;
    fn frida_device_manager_close_sync(_: *mut Manager, _: *mut c_void, error: *mut *mut GError);
    fn frida_unref(object: *mut c_void);
    fn _frida_g_signal_connect_data(
        instance: *mut c_void,
        signal: *const c_char,
        callback: Option<unsafe extern "C" fn()>,
        data: *mut c_void,
        destroy: Option<unsafe extern "C" fn(*mut c_void, *mut c_void)>,
        flags: c_int,
    ) -> usize;
    fn _frida_g_main_context_iteration(context: *mut c_void, may_block: c_int) -> c_int;
    fn _frida_g_error_free(error: *mut GError);
}
unsafe extern "C" {
    fn flock(fd: c_int, op: c_int) -> c_int;
    fn chown(path: *const c_char, owner: u32, group: u32) -> c_int;
}

static COMPLETE: AtomicBool = AtomicBool::new(false);
static RESTORED: AtomicBool = AtomicBool::new(false);
static BUSINESS_OK: AtomicBool = AtomicBool::new(false);
static SCRIPT_LOADED: AtomicBool = AtomicBool::new(false);
static CLEANUP_CONFIRMED: AtomicBool = AtomicBool::new(false);
static BRIDGE_ERROR: Mutex<Option<String>> = Mutex::new(None);
static ACTIVE_NONCE: Mutex<String> = Mutex::new(String::new());
const GAME_PACKAGES: [&str; 2] = ["com.gdoo.dhxy", "com.gdoo.xxxy2026"];
fn json_flag(text: &str, key: &str) -> bool {
    let needle = format!("\"{key}\":");
    text.find(&needle)
        .map(|i| text[i + needle.len()..].trim_start().starts_with("true"))
        .unwrap_or(false)
}
fn message_matches_nonce(text: &str, nonce: &str) -> bool {
    text.find("\"nonce\":")
        .and_then(|i| text[i + 8..].trim_start().strip_prefix('"'))
        .and_then(|s| s.split('"').next())
        .map(|s| !nonce.is_empty() && s == nonce)
        .unwrap_or(false)
}
unsafe extern "C" fn on_message(
    _: *mut Script,
    message: *const c_char,
    _: *mut c_void,
    _: *mut c_void,
) {
    let _ = std::panic::catch_unwind(|| {
        if message.is_null() {
            return;
        }
        let s = CStr::from_ptr(message).to_string_lossy();
        if s.contains("jinchan_bridge_progress") {
            eprintln!("jinchan bridge: {}", s.chars().take(1000).collect::<String>());
        }
        if s.contains("resistance_query_complete") || s.contains("resistance_query_error") {
            if !ACTIVE_NONCE
                .lock()
                .map(|nonce| message_matches_nonce(&s, &nonce))
                .unwrap_or(false)
            {
                return;
            }
            eprintln!(
                "jinchan bridge: {}",
                s.chars().take(1000).collect::<String>()
            );
            if s.contains("resistance_query_error") {
                if let Ok(mut error) = BRIDGE_ERROR.lock() {
                    *error = Some(s.chars().take(600).collect());
                }
            }
            RESTORED.store(json_flag(&s, "restored"), Ordering::Release);
            BUSINESS_OK.store(json_flag(&s, "ok"), Ordering::Release);
            COMPLETE.store(true, Ordering::Release);
        }
    });
}
fn cstr(s: &str) -> Result<CString, String> {
    CString::new(s).map_err(|_| "value contains NUL".into())
}
unsafe fn check_error(error: &mut *mut GError, context: &str) -> Result<(), String> {
    let e = *error;
    *error = ptr::null_mut();
    if e.is_null() {
        return Ok(());
    }
    let msg = if (*e).message.is_null() {
        "unknown Frida error".into()
    } else {
        CStr::from_ptr((*e).message).to_string_lossy().into_owned()
    };
    _frida_g_error_free(e);
    Err(format!("{context}: {msg}"))
}
struct FridaResources {
    manager: *mut Manager,
    device: *mut Device,
    session: *mut Session,
    script: *mut Script,
}
impl FridaResources {
    unsafe fn unload_script(&mut self) -> Result<(), String> {
        if self.script.is_null() {
            return Ok(());
        }
        let mut error = ptr::null_mut();
        frida_script_unload_sync(self.script, ptr::null_mut(), &mut error);
        let result = check_error(&mut error, "unload script");
        if result.is_ok() {
            frida_unref(self.script.cast());
            self.script = ptr::null_mut();
        }
        result
    }
    unsafe fn detach_session(&mut self) -> Result<(), String> {
        if self.session.is_null() {
            return Ok(());
        }
        let mut error = ptr::null_mut();
        frida_session_detach_sync(self.session, ptr::null_mut(), &mut error);
        let result = check_error(&mut error, "detach");
        if result.is_ok() {
            frida_unref(self.session.cast());
            self.session = ptr::null_mut();
        }
        result
    }
    unsafe fn close_manager(&mut self) -> Result<(), String> {
        if self.manager.is_null() {
            return Ok(());
        }
        let mut error = ptr::null_mut();
        frida_device_manager_close_sync(self.manager, ptr::null_mut(), &mut error);
        let result = check_error(&mut error, "close manager");
        if result.is_ok() {
            frida_unref(self.manager.cast());
            self.manager = ptr::null_mut();
        }
        result
    }
}
impl Drop for FridaResources {
    fn drop(&mut self) {
        unsafe {
            let _ = self.unload_script();
            let _ = self.detach_session();
            if !self.device.is_null() {
                frida_unref(self.device.cast());
                self.device = ptr::null_mut();
            }
            let _ = self.close_manager();
        }
    }
}
fn valid_files_dir(p: &Path) -> bool {
    p.is_absolute()
        && (p == Path::new("/data/user/0/com.xnas.wpe.jinchan/files")
            || p == Path::new("/data/data/com.xnas.wpe.jinchan/files"))
}
fn hex(s: &str, min: usize, max: usize) -> bool {
    (min..=max).contains(&s.len()) && s.bytes().all(|b| b.is_ascii_hexdigit())
}
fn valid_args(a: &[String]) -> Result<Args, String> {
    if a.len() != 10 {
        return Err("usage: jinchan_worker FILES_DIR GAME_PACKAGE ACTION POS BASE_INDEX REFINE_INDEX EXPECTED_ROLE EXPECTED_UID PREVIEW_TOKEN NONCE".into());
    }
    let dir = PathBuf::from(&a[0]);
    if !valid_files_dir(&dir) {
        return Err("FILES_DIR must be the Jinchan app private files directory".into());
    }
    if !GAME_PACKAGES.contains(&a[1].as_str()) {
        return Err("unsupported GAME_PACKAGE".into());
    }
    if !matches!(a[2].as_str(), "query" | "refresh" | "create" | "return") {
        return Err("invalid ACTION".into());
    }
    let pos = number(&a[3], 4, "POS")?;
    let base = number(&a[4], 100, "BASE_INDEX")?;
    let refine = number(&a[5], 100, "REFINE_INDEX")?;
    for (name, value) in [("EXPECTED_ROLE", &a[6]), ("EXPECTED_UID", &a[7])] {
        if value.is_empty() || value.len() > 20 || !value.bytes().all(|b| b.is_ascii_digit()) {
            return Err(format!("{name} must be a decimal ASCII string"));
        }
    }
    if !hex(&a[8], 1, 64) {
        return Err("PREVIEW_TOKEN must be 1-64 ASCII hexadecimal characters".into());
    }
    if !hex(&a[9], 1, 64) {
        return Err("NONCE must be 1-64 ASCII hexadecimal characters".into());
    }
    Ok(Args {
        dir,
        package: a[1].clone(),
        action: a[2].clone(),
        pos,
        base,
        refine,
        role: a[6].clone(),
        uid: a[7].clone(),
        token: a[8].clone(),
        nonce: a[9].clone(),
    })
}
fn number(s: &str, max: u32, name: &str) -> Result<u32, String> {
    let n = s.parse::<u32>().map_err(|_| format!("invalid {name}"))?;
    if n > max {
        return Err(format!("{name} out of range"));
    }
    Ok(n)
}
struct Args {
    dir: PathBuf,
    package: String,
    action: String,
    pos: u32,
    base: u32,
    refine: u32,
    role: String,
    uid: String,
    token: String,
    nonce: String,
}
fn proc_start(stat: &str) -> Option<u64> {
    stat.rsplit_once(") ")?
        .1
        .split_whitespace()
        .nth(19)?
        .parse()
        .ok()
}
fn game_processes(package: &str) -> Vec<(u32, u64, String)> {
    let mut out = Vec::new();
    let Ok(entries) = fs::read_dir("/proc") else {
        return out;
    };
    for e in entries.flatten() {
        let Ok(pid) = e.file_name().to_string_lossy().parse::<u32>() else {
            continue;
        };
        let cmd = fs::read(format!("/proc/{pid}/cmdline")).unwrap_or_default();
        if cmd.split(|b| *b == 0).next().unwrap_or_default() != package.as_bytes() {
            continue;
        }
        let (Ok(stat), Ok(exe)) = (
            fs::read_to_string(format!("/proc/{pid}/stat")),
            fs::read_link(format!("/proc/{pid}/exe")),
        ) else {
            continue;
        };
        let Some(ticks) = proc_start(&stat) else {
            continue;
        };
        let exe = exe.to_string_lossy().into_owned();
        if exe.contains("app_process64") {
            out.push((pid, ticks, exe));
        }
    }
    out
}
fn process_owner(pid: u32) -> Result<(u32, u32), String> {
    let status = fs::read_to_string(format!("/proc/{pid}/status"))
        .map_err(|e| format!("read game uid: {e}"))?;
    let get = |key: &str| {
        status
            .lines()
            .find_map(|l| l.strip_prefix(key)?.split_whitespace().nth(3)?.parse().ok())
            .ok_or("could not read game owner".to_string())
    };
    Ok((get("Uid:")?, get("Gid:")?))
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
fn json_escape(s: &str) -> String {
    s.replace('\\', "\\\\")
        .replace('"', "\\\"")
        .replace('\n', "\\n")
        .replace('\r', "\\r")
}
fn write_owned(path: &Path, bytes: &[u8], uid: u32, gid: u32) -> Result<(), String> {
    let tmp = path.with_extension(format!("tmp-{}", std::process::id()));
    let result = (|| {
        let mut f = fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&tmp)
            .map_err(|e| e.to_string())?;
        f.write_all(bytes).map_err(|e| e.to_string())?;
        f.sync_all().map_err(|e| e.to_string())?;
        f.set_permissions(fs::Permissions::from_mode(0o600))
            .map_err(|e| e.to_string())?;
        let p = cstr(&tmp.to_string_lossy())?;
        if unsafe { chown(p.as_ptr(), uid, gid) } != 0 {
            return Err("chown staging file failed".into());
        }
        fs::rename(&tmp, path).map_err(|e| e.to_string())
    })();
    if result.is_err() {
        let _ = fs::remove_file(tmp);
    }
    result
}
struct Stage {
    lua: PathBuf,
    result: PathBuf,
}
impl Drop for Stage {
    fn drop(&mut self) {
        let _ = fs::remove_file(&self.lua);
        let _ = fs::remove_file(&self.result);
        let _ = fs::remove_file(
            self.result
                .with_extension(format!("tmp-{}", std::process::id())),
        );
        let _ = fs::remove_file(
            self.lua
                .with_extension(format!("tmp-{}", std::process::id())),
        );
    }
}
unsafe fn connect_resources(pid: u32) -> Result<FridaResources, String> {
    frida_init();
    let manager = frida_device_manager_new();
    if manager.is_null() {
        return Err("Frida manager creation failed".into());
    }
    let mut resources = FridaResources {
        manager,
        device: ptr::null_mut(),
        session: ptr::null_mut(),
        script: ptr::null_mut(),
    };
    let mut err = ptr::null_mut();
    let list = frida_device_manager_enumerate_devices_sync(manager, ptr::null_mut(), &mut err);
    let enumerated = check_error(&mut err, "enumerate devices");
    if let Err(error) = enumerated {
        if !list.is_null() {
            frida_unref(list.cast());
        }
        return Err(error);
    }
    if list.is_null() {
        return Err("device enumeration returned null".into());
    }
    for i in 0..frida_device_list_size(list) {
        let device = frida_device_list_get(list, i);
        if !device.is_null() && frida_device_get_dtype(device) == 0 {
            resources.device = device;
            break;
        }
        if !device.is_null() {
            frida_unref(device.cast());
        }
    }
    frida_unref(list.cast());
    if resources.device.is_null() {
        return Err("local Frida device missing".into());
    }
    eprintln!("jinchan connection: attaching pid={pid}");
    resources.session = frida_device_attach_sync(
        resources.device,
        pid,
        ptr::null_mut(),
        ptr::null_mut(),
        &mut err,
    );
    check_error(&mut err, "attach")?;
    if resources.session.is_null() {
        return Err("Frida attach returned null".into());
    }
    eprintln!("jinchan connection: attached pid={pid}");
    Ok(resources)
}
unsafe fn attach_and_run(
    pid: u32,
    ticks: u64,
    expected_package: &str,
    js_path: &Path,
    lua_path: &Path,
    result_path: &Path,
    nonce: &str,
    expected_exe: &str,
) -> Result<(), String> {
    let mut resources = connect_resources(pid)?;
    let result = run_on_connection(
        &mut resources,
        pid,
        ticks,
        expected_package,
        js_path,
        lua_path,
        result_path,
        nonce,
        expected_exe,
    );
    let detached = resources.detach_session();
    let closed = resources.close_manager();
    if detached.is_err() || closed.is_err() {
        CLEANUP_CONFIRMED.store(false, Ordering::Release);
    }
    result?;
    detached?;
    closed
}
unsafe fn run_on_connection(
    resources: &mut FridaResources,
    pid: u32,
    ticks: u64,
    expected_package: &str,
    js_path: &Path,
    lua_path: &Path,
    result_path: &Path,
    nonce: &str,
    expected_exe: &str,
) -> Result<(), String> {
    COMPLETE.store(false, Ordering::Release);
    RESTORED.store(false, Ordering::Release);
    BUSINESS_OK.store(false, Ordering::Release);
    SCRIPT_LOADED.store(false, Ordering::Release);
    CLEANUP_CONFIRMED.store(false, Ordering::Release);
    if let Ok(mut error) = BRIDGE_ERROR.lock() {
        *error = None;
    }
    *ACTIVE_NONCE
        .lock()
        .map_err(|_| "operation nonce lock is poisoned")? = nonce.to_owned();
    let mut source = fs::read_to_string(js_path).map_err(|e| format!("read JS: {e}"))?;
    source = source
        .replace("__RESISTANCE_LUA_PATH__", &lua_path.to_string_lossy())
        .replace("__RESISTANCE_RESULT_PATH__", &result_path.to_string_lossy())
        .replace("__RESISTANCE_NONCE__", nonce)
        .replace("__RESISTANCE_PID__", &pid.to_string())
        .replace("__RESISTANCE_START_TICKS__", &ticks.to_string());
    source.push_str(&format!(
        "\nbegin({}, {}, {});\n",
        lua_quote(&lua_path.to_string_lossy()),
        lua_quote(&result_path.to_string_lossy()),
        lua_quote(nonce)
    ));
    let name = cstr("jinchan-worker")?;
    let src = cstr(&source)?;
    let signal = cstr("message")?;
    let mut err = ptr::null_mut();
    let opts = frida_script_options_new();
    if opts.is_null() {
        return Err("Frida script options are null".into());
    }
    frida_script_options_set_name(opts, name.as_ptr());
    frida_script_options_set_runtime(opts, 1);
    resources.script = frida_session_create_script_sync(
        resources.session,
        src.as_ptr(),
        opts,
        ptr::null_mut(),
        &mut err,
    );
    frida_unref(opts.cast());
    check_error(&mut err, "create script")?;
    if resources.script.is_null() {
        return Err("Frida script is null".into());
    }
    _frida_g_signal_connect_data(
        resources.script.cast(),
        signal.as_ptr(),
        Some(std::mem::transmute(
            on_message as unsafe extern "C" fn(_, _, _, _),
        )),
        ptr::null_mut(),
        None,
        0,
    );
    frida_script_load_sync(resources.script, ptr::null_mut(), &mut err);
    check_error(&mut err, "load script")?;
    SCRIPT_LOADED.store(true, Ordering::Release);
    let deadline = Instant::now() + Duration::from_secs(15);
    while !COMPLETE.load(Ordering::Acquire) && Instant::now() < deadline {
        _frida_g_main_context_iteration(ptr::null_mut(), 0);
        std::thread::sleep(Duration::from_millis(10));
    }
    let same = game_processes(expected_package);
    let identity_ok =
        same.len() == 1 && same[0].0 == pid && same[0].1 == ticks && same[0].2 == expected_exe;
    let unload = resources.unload_script();
    let cleanup_ok = unload.is_ok();
    CLEANUP_CONFIRMED.store(cleanup_ok, Ordering::Release);
    unload?;
    if !identity_ok {
        return Err("game process identity changed during operation".into());
    }
    if !COMPLETE.load(Ordering::Acquire) {
        return Err("operation timed out; cleanup was not confirmed".into());
    }
    if let Some(error) = BRIDGE_ERROR.lock().ok().and_then(|e| e.clone()) {
        return Err(format!("bridge execution failed: {error}"));
    }
    if !RESTORED.load(Ordering::Acquire) {
        return Err("Lua completion did not confirm restoration".into());
    }
    Ok(())
}
fn config(a: &Args, result: &Path) -> String {
    format!("{{action={},pos={},baseIndex={},refineIndex={},expectedRole={},expectedUid={},previewToken={},nonce={},resultPath={}}}",lua_quote(&a.action),a.pos,a.base,a.refine,lua_quote(&a.role),lua_quote(&a.uid),lua_quote(&a.token),lua_quote(&a.nonce),lua_quote(&result.to_string_lossy()))
}
fn failure_result(nonce: &str, restored: bool, msg: &str) -> String {
    format!(
        "{{\"nonce\":\"{}\",\"ok\":false,\"restored\":{},\"message\":\"{}\"}}",
        json_escape(nonce),
        restored,
        json_escape(&msg.chars().take(180).collect::<String>())
    )
}
fn write_ui_result(path: &Path, bytes: &[u8], uid: u32, gid: u32) -> Result<(), String> {
    write_owned(path, bytes, uid, gid)
}
fn parse_block_marker(text: &str) -> Option<(u32, u64, String)> {
    let mut pid = None;
    let mut ticks = None;
    let mut exe = None;
    for line in text.lines() {
        if let Some(v) = line.strip_prefix("pid=") {
            pid = v.parse().ok();
        }
        if let Some(v) = line.strip_prefix("startTicks=") {
            ticks = v.parse().ok();
        }
        if let Some(v) = line.strip_prefix("exe=") {
            exe = Some(v.to_owned());
        }
    }
    Some((pid?, ticks?, exe.filter(|s| !s.is_empty())?))
}
fn block_marker(pid: u32, ticks: u64, exe: &str) -> String {
    format!("pid={pid}\nstartTicks={ticks}\nexe={exe}\n")
}
fn should_block(script_loaded: bool, restored: bool, cleanup_confirmed: bool) -> bool {
    script_loaded && (!restored || !cleanup_confirmed)
}
fn execute(a: Args) -> Result<(), String> {
    let _lock = acquire_worker_lock(&a.dir)?;
    execute_connected(a, None)
}
fn acquire_worker_lock(dir: &Path) -> Result<fs::File, String> {
    let lock = fs::OpenOptions::new()
        .read(true)
        .write(true)
        .create(true)
        .open(dir.join("worker.lock"))
        .map_err(|e| format!("open worker lock: {e}"))?;
    if unsafe { flock(lock.as_raw_fd(), 2 | 4) } != 0 {
        return Err("another Jinchan operation is running".into());
    }
    Ok(lock)
}
fn execute_connected(a: Args, connection: Option<&mut FridaResources>) -> Result<(), String> {
    SCRIPT_LOADED.store(false, Ordering::Release);
    RESTORED.store(false, Ordering::Release);
    CLEANUP_CONFIRMED.store(false, Ordering::Release);
    let js = a.dir.join("jinchan_agent.js");
    let template = a.dir.join("jinchan.lua.in");
    let procs = game_processes(&a.package);
    if procs.len() != 1 {
        return Err(format!(
            "expected one {} process, found {}",
            a.package,
            procs.len(),
        ));
    }
    let (pid, ticks, exe) = &procs[0];
    let marker = a.dir.join("worker.blocked");
    check_previous_marker(&a.dir, &procs[0])?;
    let (uid, gid) = process_owner(*pid)?;
    let cache = PathBuf::from(format!("/data/user/{}/{}/cache", uid / 100000, a.package));
    let meta = fs::metadata(&cache).map_err(|e| format!("game cache unavailable: {e}"))?;
    if !meta.is_dir() || meta.uid() != uid {
        return Err("game cache ownership mismatch".into());
    }
    let prefix = format!("jinchan-{}-{ticks}-{}", pid, a.nonce);
    let lua = cache.join(format!("{prefix}.lua"));
    let game_result = cache.join(format!("{prefix}.json"));
    let ui = a.dir.join(format!("result-{}.json", a.nonce));
    if lua.exists() || game_result.exists() || ui.exists() {
        return Err("nonce path already exists".into());
    }
    let stage = Stage {
        lua: lua.clone(),
        result: game_result.clone(),
    };
    let raw = fs::read_to_string(&template).map_err(|e| format!("read Lua template: {e}"))?;
    let lua_text = raw.replace("__CONFIG__", &config(&a, &game_result));
    let mut lf = fs::OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&lua)
        .map_err(|e| format!("create game Lua: {e}"))?;
    lf.write_all(lua_text.as_bytes())
        .map_err(|e| e.to_string())?;
    lf.sync_all().map_err(|e| e.to_string())?;
    lf.set_permissions(fs::Permissions::from_mode(0o600))
        .map_err(|e| e.to_string())?;
    drop(lf);
    let owner = cstr(&lua.to_string_lossy())?;
    if unsafe { chown(owner.as_ptr(), uid, gid) } != 0 {
        return Err("set game Lua owner failed".into());
    }
    let outcome = unsafe {
        if let Some(resources) = connection {
            let result = run_on_connection(
                resources,
                *pid,
                *ticks,
                &a.package,
                &js,
                &lua,
                &game_result,
                &a.nonce,
                exe,
            );
            let cleanup = resources.unload_script();
            CLEANUP_CONFIRMED.store(cleanup.is_ok(), Ordering::Release);
            result.and(cleanup)
        } else {
            attach_and_run(
                *pid,
                *ticks,
                &a.package,
                &js,
                &lua,
                &game_result,
                &a.nonce,
                exe,
            )
        }
    };
    let restored = RESTORED.load(Ordering::Acquire);
    if should_block(
        SCRIPT_LOADED.load(Ordering::Acquire),
        restored,
        CLEANUP_CONFIRMED.load(Ordering::Acquire),
    ) {
        let _ = write_owned(
            &marker,
            block_marker(*pid, *ticks, exe).as_bytes(),
            fs::metadata(&a.dir).map(|m| m.uid()).unwrap_or(0),
            fs::metadata(&a.dir).map(|m| m.gid()).unwrap_or(0),
        );
    }
    let output = if let Err(ref error) = outcome {
        failure_result(
            &a.nonce,
            !SCRIPT_LOADED.load(Ordering::Acquire) || restored,
            error,
        )
        .into_bytes()
    } else if game_result.exists() {
        let b = fs::read(&game_result).map_err(|e| format!("read Lua result: {e}"))?;
        if b.len() > 256 * 1024 {
            return Err("Lua result exceeds 256 KiB".into());
        }
        let s = String::from_utf8(b.clone()).map_err(|_| "Lua result is not UTF-8")?;
        if !s.contains(&format!("\"nonce\":\"{}\"", a.nonce)) {
            return Err("Lua result nonce mismatch".into());
        }
        b
    } else {
        failure_result(
            &a.nonce,
            !SCRIPT_LOADED.load(Ordering::Acquire) || restored,
            outcome
                .as_ref()
                .err()
                .map(String::as_str)
                .unwrap_or("Lua result missing"),
        )
        .into_bytes()
    };
    write_ui_result(
        &ui,
        &output,
        fs::metadata(&a.dir).map_err(|e| e.to_string())?.uid(),
        fs::metadata(&a.dir).map_err(|e| e.to_string())?.gid(),
    )?;
    drop(stage);
    match outcome {
        Err(e) => Err(e),
        Ok(()) => {
            if !restored {
                Err("Lua restoration was not confirmed".into())
            } else if !BUSINESS_OK.load(Ordering::Acquire) {
                Err("operation returned ok=false; result is available to UI".into())
            } else {
                Ok(())
            }
        }
    }
}
fn check_previous_marker(dir: &Path, identity: &(u32, u64, String)) -> Result<(), String> {
    let marker = dir.join("worker.blocked");
    if marker.exists() {
        let text = fs::read_to_string(&marker).map_err(|e| format!("read cleanup marker: {e}"))?;
        let previous =
            parse_block_marker(&text).ok_or("cleanup marker is invalid; refusing to attach")?;
        if &previous == identity {
            return Err(
                "previous operation cleanup is unconfirmed; restart the game before reconnecting"
                    .into(),
            );
        }
        fs::remove_file(marker).map_err(|e| format!("remove stale cleanup marker: {e}"))?;
    }
    Ok(())
}
fn unix_seconds() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs()
}
fn connection_status(
    dir: &Path,
    package: &str,
    token: &str,
    state: &str,
    message: &str,
    identity: Option<&(u32, u64, String)>,
) -> Result<(), String> {
    let meta = fs::metadata(dir).map_err(|e| e.to_string())?;
    let (pid, ticks) = identity.map(|i| (i.0, i.1)).unwrap_or((0, 0));
    let output = format!("{{\"token\":\"{}\",\"package\":\"{}\",\"state\":\"{}\",\"message\":\"{}\",\"pid\":{},\"startTicks\":{},\"workerPid\":{},\"heartbeat\":{}}}", json_escape(token), json_escape(package), json_escape(state), json_escape(&message.chars().take(240).collect::<String>()), pid, ticks, std::process::id(), unix_seconds());
    write_owned(
        &dir.join(format!("connection-{token}.json")),
        output.as_bytes(),
        meta.uid(),
        meta.gid(),
    )
}
fn parse_request(text: &str, dir: &Path, package: &str) -> Result<Args, String> {
    if text.len() > 8192 {
        return Err("request is too large".into());
    }
    let lines: Vec<String> = text.lines().map(str::to_owned).collect();
    let args = valid_args(&lines)?;
    if args.dir != dir || args.package != package {
        return Err("request does not match this connection".into());
    }
    Ok(args)
}
fn serve(dir: PathBuf, package: String, token: String) -> Result<(), String> {
    if !valid_files_dir(&dir) || !GAME_PACKAGES.contains(&package.as_str()) || !hex(&token, 1, 64) {
        return Err("invalid persistent connection arguments".into());
    }
    let _lock = acquire_worker_lock(&dir)?;
    let app_owner = fs::metadata(&dir).map_err(|e| e.to_string())?;
    let request_path = dir.join(format!("request-{token}.txt"));
    let stop_path = dir.join(format!("stop-{token}"));
    let mut identity = None;
    let result = (|| -> Result<(), String> {
        let owners = game_processes("com.xnas.wpe.jinchan");
        if owners.len() != 1 {
            return Err("helper app process is missing or ambiguous".into());
        }
        let owner_identity = owners[0].clone();
        connection_status(
            &dir,
            &package,
            &token,
            "connecting",
            "Connecting to the selected game",
            None,
        )?;
        let procs = game_processes(&package);
        if procs.len() != 1 {
            return Err(format!(
                "expected one {package} process, found {}",
                procs.len()
            ));
        }
        identity = Some(procs[0].clone());
        let target = identity.as_ref().unwrap();
        check_previous_marker(&dir, target)?;
        let mut resources = unsafe { connect_resources(target.0)? };
        if game_processes(&package) != vec![target.clone()] {
            return Err("game process identity changed while connecting".into());
        }
        connection_status(
            &dir,
            &package,
            &token,
            "connected",
            "Connected; this session is reused for all operations",
            Some(target),
        )?;
        let mut heartbeat = Instant::now();
        loop {
            unsafe {
                _frida_g_main_context_iteration(ptr::null_mut(), 0);
            }
            if stop_path.exists() {
                break;
            }
            if game_processes("com.xnas.wpe.jinchan") != vec![owner_identity.clone()] {
                connection_status(
                    &dir,
                    &package,
                    &token,
                    "disconnected",
                    "Helper app process ended; releasing connection",
                    Some(target),
                )?;
                return Ok(());
            }
            if unsafe { frida_session_is_detached(resources.session) } != 0
                || game_processes(&package) != vec![target.clone()]
            {
                connection_status(
                    &dir,
                    &package,
                    &token,
                    "disconnected",
                    "Game process ended or connection was detached; reconnect manually",
                    Some(target),
                )?;
                return Ok(());
            }
            if request_path.exists() {
                let meta = fs::symlink_metadata(&request_path).map_err(|e| e.to_string())?;
                if !meta.is_file() || meta.uid() != app_owner.uid() || meta.len() > 8192 {
                    return Err("request file ownership or size is invalid".into());
                }
                let mut text = String::new();
                fs::File::open(&request_path)
                    .map_err(|e| e.to_string())?
                    .take(8193)
                    .read_to_string(&mut text)
                    .map_err(|e| e.to_string())?;
                // Remove before execution: a crashed or timed-out operation must never be replayed.
                fs::remove_file(&request_path).map_err(|e| e.to_string())?;
                let args = parse_request(&text, &dir, &package)?;
                let nonce = args.nonce.clone();
                let output = dir.join(format!("result-{nonce}.json"));
                if output.exists() {
                    return Err("operation nonce has already been used".into());
                }
                connection_status(
                    &dir,
                    &package,
                    &token,
                    "busy",
                    "Executing one operation; no automatic retries",
                    Some(target),
                )?;
                eprintln!(
                    "jinchan operation: begin action={} nonce={nonce}",
                    args.action
                );
                let operation = execute_connected(args, Some(&mut resources));
                if let Err(ref error) = operation {
                    eprintln!("jinchan operation: failed nonce={nonce}: {error}");
                    if !output.exists() {
                        let restored = !SCRIPT_LOADED.load(Ordering::Acquire)
                            || RESTORED.load(Ordering::Acquire);
                        write_ui_result(
                            &output,
                            failure_result(&nonce, restored, error).as_bytes(),
                            app_owner.uid(),
                            app_owner.gid(),
                        )?;
                    }
                } else {
                    eprintln!("jinchan operation: completed nonce={nonce}");
                }
                if dir.join("worker.blocked").exists() || !resources.script.is_null() {
                    connection_status(&dir, &package, &token, "blocked", "Lua or script cleanup is unconfirmed; restart the game before reconnecting", Some(target))?;
                    return Ok(());
                }
                if unsafe { frida_session_is_detached(resources.session) } != 0
                    || game_processes(&package) != vec![target.clone()]
                {
                    connection_status(
                        &dir,
                        &package,
                        &token,
                        "disconnected",
                        "Game identity changed during operation",
                        Some(target),
                    )?;
                    return Ok(());
                }
                connection_status(
                    &dir,
                    &package,
                    &token,
                    "connected",
                    "Connected; previous operation finished",
                    Some(target),
                )?;
                heartbeat = Instant::now();
            }
            if heartbeat.elapsed() >= Duration::from_secs(1) {
                connection_status(
                    &dir,
                    &package,
                    &token,
                    "connected",
                    "Connected",
                    Some(target),
                )?;
                heartbeat = Instant::now();
            }
            std::thread::sleep(Duration::from_millis(50));
        }
        unsafe {
            resources.unload_script()?;
            resources.detach_session()?;
            resources.close_manager()?;
        }
        connection_status(
            &dir,
            &package,
            &token,
            "disconnected",
            "Disconnected by user",
            Some(target),
        )?;
        Ok(())
    })();
    if let Err(ref error) = result {
        let _ = connection_status(&dir, &package, &token, "error", error, identity.as_ref());
    }
    let _ = fs::remove_file(request_path);
    let _ = fs::remove_file(stop_path);
    result
}
fn self_test() -> Result<(), String> {
    assert!(json_flag("{\"restored\": true}", "restored"));
    assert!(json_flag("{\"restored\":true}", "restored"));
    assert!(!json_flag("{\"restored\":false}", "restored"));
    assert!(!json_flag("{\"message\":\"restored\"}", "restored"));
    assert!(message_matches_nonce("{\"nonce\": \"a1\"}", "a1"));
    assert!(!message_matches_nonce("{\"nonce\":\"a2\"}", "a1"));
    assert!(!message_matches_nonce("{\"ok\":true}", "a1"));
    assert!(valid_files_dir(Path::new(
        "/data/user/0/com.xnas.wpe.jinchan/files"
    )));
    assert!(valid_files_dir(Path::new(
        "/data/data/com.xnas.wpe.jinchan/files"
    )));
    assert!(!valid_files_dir(Path::new(
        "/data/user/0/com.xnas.wpe.jinchan/files/.."
    )));
    assert!(hex("0aF1", 1, 64));
    assert!(!hex("../x", 1, 64));
    assert!(number("4", 4, "POS").is_ok());
    assert!(number("5", 4, "POS").is_err());
    assert!(number("100", 100, "index").is_ok());
    assert!(number("101", 100, "index").is_err());
    let args = vec![
        "/data/user/0/com.xnas.wpe.jinchan/files".into(),
        "com.gdoo.xxxy2026".into(),
        "create".into(),
        "4".into(),
        "100".into(),
        "0".into(),
        "123".into(),
        "7654321".into(),
        "0f".into(),
        "deadbeef".into(),
    ];
    // The equipment UID is a business value and intentionally differs from an Android app UID.
    assert_ne!(args[7], "10123");
    assert!(valid_args(&args).is_ok());
    assert!(valid_args(&args[..9]).is_err());
    let mut bad_role = args.clone();
    bad_role[6] = "a1".into();
    assert!(valid_args(&bad_role).is_err());
    let mut bad_pos = args.clone();
    bad_pos[3] = "5".into();
    assert!(valid_args(&bad_pos).is_err());
    let mut bad_nonce = args.clone();
    bad_nonce[9] = "../bad".into();
    assert!(valid_args(&bad_nonce).is_err());
    let mut bad_package = args.clone();
    bad_package[1] = "com.android.settings".into();
    assert!(valid_args(&bad_package).is_err());
    let marker = block_marker(42, 123456, "/system/bin/app_process64");
    let identity = parse_block_marker(&marker).unwrap();
    assert_eq!(identity, (42, 123456, "/system/bin/app_process64".into()));
    assert_ne!(identity, (42, 123457, "/system/bin/app_process64".into()));
    assert_ne!(identity, (43, 123456, "/system/bin/app_process64".into()));
    assert!(parse_block_marker("invalid").is_none());
    assert!(!should_block(false, false, false)); // attach/load failed before hook activation
    assert!(should_block(true, false, true)); // loaded script without Lua restoration
    assert!(should_block(true, true, false)); // Lua restored but Frida cleanup failed
    assert!(!should_block(true, true, true)); // both cleanup steps confirmed
    assert!(matches!(
        "refresh",
        "query" | "refresh" | "create" | "return"
    ));
    assert!(!matches!(
        "delete",
        "query" | "refresh" | "create" | "return"
    ));
    let r = Path::new("/data/user/0/com.gdoo.xxxy2026/cache/result-a1.json");
    let a = Args {
        dir: PathBuf::from("/data/user/0/com.xnas.wpe.jinchan/files"),
        package: "com.gdoo.xxxy2026".into(),
        action: "create".into(),
        pos: 2,
        base: 3,
        refine: 4,
        role: "0".into(),
        uid: "0".into(),
        token: "0".into(),
        nonce: "a1".into(),
    };
    let c = config(&a, r);
    assert!(c.contains("action='create'"));
    assert!(c.contains("resultPath='/data/user/0/com.gdoo.xxxy2026/cache/result-a1.json'"));
    assert_eq!(
        Path::new(&format!("result-{}.json", a.nonce))
            .file_name()
            .unwrap(),
        "result-a1.json"
    );
    assert!(failure_result("a1", false, "timeout").contains("\"nonce\":\"a1\""));
    assert!(failure_result("a1", false, "timeout").contains("\"restored\":false"));
    let request =
        "/data/user/0/com.xnas.wpe.jinchan/files\ncom.gdoo.dhxy\nquery\n0\n0\n0\n0\n0\n0\na1\n";
    let dir = Path::new("/data/user/0/com.xnas.wpe.jinchan/files");
    assert!(parse_request(request, dir, "com.gdoo.dhxy").is_ok());
    assert!(parse_request(request, dir, "com.gdoo.xxxy2026").is_err());
    assert!(parse_request(&request.replace("query", "delete"), dir, "com.gdoo.dhxy").is_err());
    assert!(parse_request(&request.replace("a1", "a1/../../bad"), dir, "com.gdoo.dhxy").is_err());
    assert!(parse_request(&request.repeat(100), dir, "com.gdoo.dhxy").is_err());
    println!("self-test ok");
    Ok(())
}
fn main() {
    let raw: Vec<String> = std::env::args().skip(1).collect();
    let r = std::panic::catch_unwind(|| {
        if raw.as_slice() == ["--self-test"] {
            return self_test();
        }
        if raw.len() == 4 && raw[0] == "--serve" {
            return serve(PathBuf::from(&raw[1]), raw[2].clone(), raw[3].clone());
        }
        let a = valid_args(&raw)?;
        let nonce = a.nonce.clone();
        let result = execute(a);
        if let Err(ref e) = result {
            let dir = PathBuf::from(&raw[0]);
            let out = dir.join(format!("result-{nonce}.json"));
            if valid_files_dir(&dir) && !out.exists() {
                if let Ok(m) = fs::metadata(&dir) {
                    let _ = write_ui_result(
                        &out,
                        failure_result(
                            &nonce,
                            !SCRIPT_LOADED.load(Ordering::Acquire)
                                || RESTORED.load(Ordering::Acquire),
                            e,
                        )
                        .as_bytes(),
                        m.uid(),
                        m.gid(),
                    );
                }
            }
        }
        result
    });
    match r {
        Ok(Ok(())) => {}
        Ok(Err(e)) => {
            eprintln!("jinchan worker: {e}");
            std::process::exit(1)
        }
        Err(_) => {
            eprintln!("jinchan worker panic caught");
            std::process::exit(2)
        }
    }
}
