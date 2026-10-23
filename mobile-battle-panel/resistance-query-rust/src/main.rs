use std::ffi::{c_char, c_int, c_void, CStr, CString};
use std::fs;
use std::os::unix::fs::{MetadataExt, PermissionsExt};
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::path::{Path, PathBuf};
use std::ptr;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::{Duration, Instant};

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
    fn frida_device_manager_close_sync(_: *mut Manager, _: *mut c_void, error: *mut *mut GError);
    fn frida_unref(object: *mut c_void);
}
#[link(name = "frida-core", kind = "static")]
unsafe extern "C" {
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
    fn chown(path: *const c_char, owner: u32, group: u32) -> c_int;
}

static COMPLETE: AtomicBool = AtomicBool::new(false);
static LUA_RESTORED: AtomicBool = AtomicBool::new(false);
static LUA_OK: AtomicBool = AtomicBool::new(false);
unsafe extern "C" fn on_message(
    _: *mut Script,
    message: *const c_char,
    _: *mut c_void,
    _: *mut c_void,
) {
    let _ = catch_unwind(AssertUnwindSafe(|| {
        if message.is_null() {
            return;
        }
        let s = CStr::from_ptr(message).to_string_lossy();
        if s.contains("resistance_query_complete") {
            LUA_OK.store(s.contains("\"ok\":true"), Ordering::Release);
            LUA_RESTORED.store(s.contains("\"restored\":true"), Ordering::Release);
            COMPLETE.store(true, Ordering::Release);
        } else if s.contains("resistance_query_error") {
            COMPLETE.store(true, Ordering::Release);
        }
    }));
}
unsafe fn check_error(error: *mut GError, context: &str) -> Result<(), String> {
    if error.is_null() {
        return Ok(());
    }
    let message = if (*error).message.is_null() {
        "unknown Frida error".into()
    } else {
        CStr::from_ptr((*error).message)
            .to_string_lossy()
            .into_owned()
    };
    _frida_g_error_free(error);
    Err(format!("{context}: {message}"))
}
fn is_target_executable(path: &str) -> bool {
    path.contains("app_process64")
}
fn proc_start_ticks(stat: &str) -> Option<u64> {
    stat.rsplit_once(") ")?
        .1
        .split_whitespace()
        .nth(19)?
        .parse()
        .ok()
}
fn identity(pid: u32, start_ticks: u64) -> Result<String, String> {
    let base = format!("/proc/{pid}");
    let cmd = fs::read(format!("{base}/cmdline")).map_err(|e| format!("read cmdline: {e}"))?;
    if cmd.split(|b| *b == 0).next().unwrap_or_default() != b"com.gdoo.dhxy" {
        return Err("package identity mismatch".into());
    }
    let stat = fs::read_to_string(format!("{base}/stat")).map_err(|e| format!("read stat: {e}"))?;
    let ticks = proc_start_ticks(&stat).ok_or("invalid process start ticks")?;
    if ticks != start_ticks {
        return Err("process start ticks changed".into());
    }
    let exe = fs::read_link(format!("{base}/exe"))
        .map_err(|e| format!("read executable: {e}"))?
        .to_string_lossy()
        .into_owned();
    if !is_target_executable(&exe) {
        return Err("target executable is not app_process64".into());
    }
    Ok(exe)
}
fn process_ids(pid: u32) -> Result<(u32, u32), String> {
    let status = fs::read_to_string(format!("/proc/{pid}/status"))
        .map_err(|e| format!("read process uid: {e}"))?;
    let uid = status
        .lines()
        .find_map(|line| {
            line.strip_prefix("Uid:")?
                .split_whitespace()
                .nth(3)?
                .parse()
                .ok()
        })
        .ok_or("could not read game filesystem uid")?;
    let gid = status
        .lines()
        .find_map(|line| {
            line.strip_prefix("Gid:")?
                .split_whitespace()
                .nth(3)?
                .parse()
                .ok()
        })
        .ok_or("could not read game filesystem gid")?;
    Ok((uid, gid))
}
fn lua_quote(value: &str) -> String {
    format!(
        "'{}'",
        value
            .replace('\\', "\\\\")
            .replace('\'', "\\'")
            .replace('\n', "\\n")
            .replace('\r', "\\r")
    )
}
fn write_owned(path: &Path, bytes: &[u8], uid: u32, gid: u32) -> Result<(), String> {
    let tmp = path.with_extension(format!("tmp-{}", std::process::id()));
    let result = (|| -> Result<(), String> {
        let mut file = fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&tmp)
            .map_err(|e| format!("create staging file: {e}"))?;
        use std::io::Write;
        file.write_all(bytes)
            .map_err(|e| format!("write staging file: {e}"))?;
        file.sync_all()
            .map_err(|e| format!("sync staging file: {e}"))?;
        file.set_permissions(fs::Permissions::from_mode(0o600))
            .map_err(|e| format!("set staging mode: {e}"))?;
        let raw = CString::new(tmp.to_string_lossy().as_bytes()).map_err(|e| e.to_string())?;
        if unsafe { chown(raw.as_ptr(), uid, gid) } != 0 {
            return Err("set staging owner failed".into());
        }
        fs::rename(&tmp, path).map_err(|e| format!("publish staging file: {e}"))
    })();
    if result.is_err() {
        let _ = fs::remove_file(&tmp);
    }
    result
}
struct Staging {
    lua: PathBuf,
    result: PathBuf,
}
impl Drop for Staging {
    fn drop(&mut self) {
        let _ = fs::remove_file(&self.lua);
        let _ = fs::remove_file(&self.result);
        let _ = fs::remove_file(format!("{}.tmp", self.result.display()));
        let _ = fs::remove_file(
            self.result
                .with_extension("tmp-".to_owned() + &std::process::id().to_string()),
        );
        let _ = fs::remove_file(
            self.lua
                .with_extension("tmp-".to_owned() + &std::process::id().to_string()),
        );
    }
}
#[cfg(test)]
fn self_check() {
    assert_eq!(
        b"com.gdoo.dhxy".split(|b| *b == 0).next(),
        Some(b"com.gdoo.dhxy".as_slice())
    );
    assert_ne!(
        b"com.gdoo.dhxy:remote".split(|b| *b == 0).next(),
        Some(b"com.gdoo.dhxy".as_slice())
    );
    assert!(is_target_executable("/system/bin/app_process64"));
    assert!(!is_target_executable("/system/bin/app_process"));
    let stat = format!("1 (test) S {} 456", vec!["0"; 18].join(" "));
    assert_eq!(proc_start_ticks(&stat), Some(456));
    assert_eq!(proc_start_ticks("malformed"), None);
}
#[cfg(test)]
mod tests {
    #[test]
    fn identity_helpers_validate_package_process_and_executable() {
        super::self_check();
    }
}
fn js_quote(s: &str) -> String {
    format!(
        "\"{}\"",
        s.replace('\\', "\\\\")
            .replace('"', "\\\"")
            .replace('\n', "\\n")
            .replace('\r', "\\r")
    )
}
unsafe fn run_attached(
    pid: u32,
    start_ticks: u64,
    battle: u64,
    nonce: &str,
    js_path: &str,
    lua_path: &str,
    result_path: &str,
) -> Result<(), String> {
    if pid == 0
        || start_ticks == 0
        || battle == 0
        || nonce.len() != 32
        || !nonce.bytes().all(|b| b.is_ascii_hexdigit())
    {
        return Err("invalid query identity".into());
    }
    let exe = identity(pid, start_ticks)?;
    let mut source = fs::read_to_string(js_path).map_err(|e| format!("read JS source: {e}"))?;
    source = source
        .replace("__RESISTANCE_LUA_PATH__", lua_path)
        .replace("__RESISTANCE_RESULT_PATH__", result_path)
        .replace("__RESISTANCE_NONCE__", nonce)
        .replace("__RESISTANCE_PID__", &pid.to_string())
        .replace("__RESISTANCE_START_TICKS__", &start_ticks.to_string())
        .replace("__RESISTANCE_BATTLE__", &battle.to_string());
    source.push_str(&format!(
        "\nbegin({}, {});\n",
        js_quote(lua_path),
        js_quote(result_path)
    ));
    frida_init();
    let manager = frida_device_manager_new();
    if manager.is_null() {
        return Err("Frida manager creation failed".into());
    }
    let mut error = ptr::null_mut();
    let devices = frida_device_manager_enumerate_devices_sync(manager, ptr::null_mut(), &mut error);
    check_error(error, "enumerate devices")?;
    if devices.is_null() {
        return Err("device enumeration returned null".into());
    }
    let mut local = ptr::null_mut();
    for i in 0..frida_device_list_size(devices) {
        let device = frida_device_list_get(devices, i);
        if !device.is_null() && frida_device_get_dtype(device) == 0 {
            local = device;
            break;
        }
        if !device.is_null() {
            frida_unref(device.cast());
        }
    }
    frida_unref(devices.cast());
    if local.is_null() {
        return Err("no local Frida device found".into());
    }
    let session =
        frida_device_attach_sync(local, pid, ptr::null_mut(), ptr::null_mut(), &mut error);
    check_error(error, "attach")?;
    if session.is_null() {
        return Err("attach returned null session".into());
    }
    let options = frida_script_options_new();
    let name = CString::new("role-resistance-query").unwrap();
    let script_source = CString::new(source).map_err(|_| "JS source contains NUL".to_owned())?;
    frida_script_options_set_name(options, name.as_ptr());
    frida_script_options_set_runtime(options, 1);
    let script = frida_session_create_script_sync(
        session,
        script_source.as_ptr(),
        options,
        ptr::null_mut(),
        &mut error,
    );
    frida_unref(options.cast());
    check_error(error, "create script")?;
    if script.is_null() {
        return Err("script creation returned null".into());
    }
    let signal = CString::new("message").unwrap();
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
    check_error(error, "load script")?;
    let deadline = Instant::now() + Duration::from_secs(7);
    while !COMPLETE.load(Ordering::Acquire) && Instant::now() < deadline {
        _frida_g_main_context_iteration(ptr::null_mut(), 0);
        std::thread::sleep(Duration::from_millis(10));
    }
    let same_identity = identity(pid, start_ticks)
        .map(|value| value == exe)
        .unwrap_or(false);
    let mut cleanup_error = ptr::null_mut();
    frida_script_unload_sync(script, ptr::null_mut(), &mut cleanup_error);
    let unload = check_error(cleanup_error, "unload script");
    frida_unref(script.cast());
    let mut detach_error = ptr::null_mut();
    frida_session_detach_sync(session, ptr::null_mut(), &mut detach_error);
    let detach = check_error(detach_error, "detach");
    frida_unref(session.cast());
    frida_unref(local.cast());
    let mut close_error = ptr::null_mut();
    frida_device_manager_close_sync(manager, ptr::null_mut(), &mut close_error);
    let close = check_error(close_error, "close manager");
    unload?;
    detach?;
    close?;
    if !same_identity {
        return Err("target process identity changed during query".into());
    }
    if !COMPLETE.load(Ordering::Acquire) {
        return Err("Lua query timed out before restoration confirmation".into());
    }
    if !LUA_RESTORED.load(Ordering::Acquire) {
        return Err("Lua query completion did not confirm handler restoration".into());
    }
    if !LUA_OK.load(Ordering::Acquire) {
        return Err("Lua query failed; inspect result file".into());
    }
    Ok(())
}
fn run(
    pid: u32,
    start_ticks: u64,
    battle: u64,
    nonce: &str,
    js_path: &str,
    lua_source_path: &str,
    panel_result_path: &str,
) -> Result<(), String> {
    identity(pid, start_ticks)?;
    let (game_uid, game_gid) = process_ids(pid)?;
    let panel_metadata = fs::metadata(js_path).map_err(|e| format!("read panel app owner: {e}"))?;
    let panel_uid = panel_metadata.uid();
    let panel_gid = panel_metadata.gid();
    let cache = PathBuf::from(format!(
        "/data/user/{}/com.gdoo.dhxy/cache",
        game_uid / 100_000
    ));
    let cache_meta = fs::metadata(&cache).map_err(|e| format!("open game cache: {e}"))?;
    if !cache_meta.is_dir() || cache_meta.uid() != game_uid {
        return Err("game cache ownership does not match target process".into());
    }
    let staged_lua = cache.join(format!("resistance-{nonce}.lua"));
    let game_result = cache.join(format!("resistance-{nonce}.json"));
    let panel_result = PathBuf::from(panel_result_path);
    if staged_lua.exists() || game_result.exists() || panel_result.exists() {
        return Err("nonce staging path already exists".into());
    }
    let _staging = Staging {
        lua: staged_lua.clone(),
        result: game_result.clone(),
    };
    let lua = fs::read_to_string(lua_source_path)
        .map_err(|e| format!("read Lua source: {e}"))?
        .replace(
            "__RESISTANCE_RESULT_PATH__",
            &lua_quote(&game_result.to_string_lossy()),
        );
    write_owned(&staged_lua, lua.as_bytes(), game_uid, game_gid)?;
    unsafe {
        run_attached(
            pid,
            start_ticks,
            battle,
            nonce,
            js_path,
            &staged_lua.to_string_lossy(),
            &game_result.to_string_lossy(),
        )?;
    }
    let output = fs::read(&game_result).map_err(|e| format!("read game query result: {e}"))?;
    if output.len() > 256 * 1024 {
        return Err("query result exceeded size limit".into());
    }
    let text = String::from_utf8(output.clone()).map_err(|_| "query result is not UTF-8")?;
    if !text.contains(&format!("\"nonce\":\"{nonce}\""))
        || !text.contains("\"ok\":true")
        || !text.contains("\"restored\":true")
    {
        return Err("game query result failed nonce or restoration validation".into());
    }
    write_owned(&panel_result, &output, panel_uid, panel_gid)
}
fn main() {
    let result = catch_unwind(|| {
        let a: Vec<String> = std::env::args().collect();
        if a.len() != 8 {
            return Err(
                "usage: resistance-query PID START_TICKS BATTLE NONCE JS_PATH LUA_PATH RESULT_PATH"
                    .to_owned(),
            );
        }
        let pid = a[1].parse().map_err(|_| "invalid pid")?;
        let ticks = a[2].parse().map_err(|_| "invalid start ticks")?;
        let battle = a[3].parse().map_err(|_| "invalid battle id")?;
        run(pid, ticks, battle, &a[4], &a[5], &a[6], &a[7])
    });
    match result {
        Ok(Ok(())) => std::process::exit(0),
        Ok(Err(e)) => {
            eprintln!("resistance query: {e}");
            std::process::exit(1);
        }
        Err(_) => std::process::exit(2),
    }
}
