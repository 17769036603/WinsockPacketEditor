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

mod native_drain_gate {
    use std::collections::HashSet;

    #[derive(Debug, Clone, PartialEq)]
    enum JsonValue {
        Object(Vec<(String, JsonValue)>),
        Array(Vec<JsonValue>),
        String(String),
        Bool(bool),
        Null,
        Number,
    }

    struct JsonParser<'a> {
        bytes: &'a [u8],
        at: usize,
    }

    impl<'a> JsonParser<'a> {
        fn parse(input: &'a str) -> Option<JsonValue> {
            let mut parser = Self {
                bytes: input.as_bytes(),
                at: 0,
            };
            let value = parser.value(0)?;
            parser.space();
            (parser.at == parser.bytes.len()).then_some(value)
        }
        fn space(&mut self) {
            while self.bytes.get(self.at).is_some_and(u8::is_ascii_whitespace) {
                self.at += 1;
            }
        }
        fn value(&mut self, depth: usize) -> Option<JsonValue> {
            if depth > 64 {
                return None;
            }
            self.space();
            match *self.bytes.get(self.at)? {
                b'{' => self.object(depth + 1),
                b'[' => self.array(depth + 1),
                b'"' => self.string().map(JsonValue::String),
                b't' => {
                    self.literal(b"true")?;
                    Some(JsonValue::Bool(true))
                }
                b'f' => {
                    self.literal(b"false")?;
                    Some(JsonValue::Bool(false))
                }
                b'n' => {
                    self.literal(b"null")?;
                    Some(JsonValue::Null)
                }
                b'-' | b'0'..=b'9' => {
                    self.number()?;
                    Some(JsonValue::Number)
                }
                _ => None,
            }
        }
        fn object(&mut self, depth: usize) -> Option<JsonValue> {
            self.at += 1;
            self.space();
            let mut entries = Vec::new();
            let mut keys = HashSet::new();
            if self.bytes.get(self.at) == Some(&b'}') {
                self.at += 1;
                return Some(JsonValue::Object(entries));
            }
            loop {
                self.space();
                let key = self.string()?;
                if !keys.insert(key.clone()) {
                    return None;
                }
                self.space();
                if self.bytes.get(self.at) != Some(&b':') {
                    return None;
                }
                self.at += 1;
                entries.push((key, self.value(depth)?));
                self.space();
                match self.bytes.get(self.at)? {
                    b',' => self.at += 1,
                    b'}' => {
                        self.at += 1;
                        break;
                    }
                    _ => return None,
                }
            }
            Some(JsonValue::Object(entries))
        }
        fn array(&mut self, depth: usize) -> Option<JsonValue> {
            self.at += 1;
            self.space();
            let mut values = Vec::new();
            if self.bytes.get(self.at) == Some(&b']') {
                self.at += 1;
                return Some(JsonValue::Array(values));
            }
            loop {
                values.push(self.value(depth)?);
                self.space();
                match self.bytes.get(self.at)? {
                    b',' => self.at += 1,
                    b']' => {
                        self.at += 1;
                        break;
                    }
                    _ => return None,
                }
            }
            Some(JsonValue::Array(values))
        }
        fn string(&mut self) -> Option<String> {
            if self.bytes.get(self.at) != Some(&b'"') {
                return None;
            }
            self.at += 1;
            let mut out = String::new();
            let mut plain = Vec::new();
            loop {
                let byte = *self.bytes.get(self.at)?;
                self.at += 1;
                match byte {
                    b'"' => {
                        out.push_str(std::str::from_utf8(&plain).ok()?);
                        return Some(out);
                    }
                    b'\\' => {
                        out.push_str(std::str::from_utf8(&plain).ok()?);
                        plain.clear();
                        let escaped = *self.bytes.get(self.at)?;
                        self.at += 1;
                        match escaped {
                            b'"' => out.push('"'),
                            b'\\' => out.push('\\'),
                            b'/' => out.push('/'),
                            b'b' => out.push('\u{0008}'),
                            b'f' => out.push('\u{000c}'),
                            b'n' => out.push('\n'),
                            b'r' => out.push('\r'),
                            b't' => out.push('\t'),
                            b'u' => {
                                let high = self.hex4()?;
                                let scalar = if (0xd800..=0xdbff).contains(&high) {
                                    if self.bytes.get(self.at..self.at + 2)? != b"\\u" {
                                        return None;
                                    }
                                    self.at += 2;
                                    let low = self.hex4()?;
                                    if !(0xdc00..=0xdfff).contains(&low) {
                                        return None;
                                    }
                                    0x10000
                                        + (((high - 0xd800) as u32) << 10)
                                        + (low - 0xdc00) as u32
                                } else if (0xdc00..=0xdfff).contains(&high) {
                                    return None;
                                } else {
                                    high as u32
                                };
                                out.push(char::from_u32(scalar)?);
                            }
                            _ => return None,
                        }
                    }
                    0x00..=0x1f => return None,
                    _ => plain.push(byte),
                }
            }
        }
        fn hex4(&mut self) -> Option<u16> {
            let slice = self.bytes.get(self.at..self.at + 4)?;
            let mut value = 0u16;
            for byte in slice {
                value = value.checked_mul(16)?
                    + match byte {
                        b'0'..=b'9' => (byte - b'0') as u16,
                        b'a'..=b'f' => (byte - b'a' + 10) as u16,
                        b'A'..=b'F' => (byte - b'A' + 10) as u16,
                        _ => return None,
                    };
            }
            self.at += 4;
            Some(value)
        }
        fn literal(&mut self, literal: &[u8]) -> Option<()> {
            if self.bytes.get(self.at..self.at + literal.len())? != literal {
                return None;
            }
            self.at += literal.len();
            Some(())
        }
        fn number(&mut self) -> Option<()> {
            let start = self.at;
            if self.bytes.get(self.at) == Some(&b'-') {
                self.at += 1;
            }
            match self.bytes.get(self.at)? {
                b'0' => self.at += 1,
                b'1'..=b'9' => {
                    self.at += 1;
                    while self.bytes.get(self.at).is_some_and(u8::is_ascii_digit) {
                        self.at += 1;
                    }
                }
                _ => return None,
            }
            if self.bytes.get(self.at) == Some(&b'.') {
                self.at += 1;
                let fraction = self.at;
                while self.bytes.get(self.at).is_some_and(u8::is_ascii_digit) {
                    self.at += 1;
                }
                if fraction == self.at {
                    return None;
                }
            }
            if matches!(self.bytes.get(self.at), Some(b'e' | b'E')) {
                self.at += 1;
                if matches!(self.bytes.get(self.at), Some(b'+' | b'-')) {
                    self.at += 1;
                }
                let exponent = self.at;
                while self.bytes.get(self.at).is_some_and(u8::is_ascii_digit) {
                    self.at += 1;
                }
                if exponent == self.at {
                    return None;
                }
            }
            (start < self.at).then_some(())
        }
    }

    fn object_field<'a>(value: &'a JsonValue, key: &str) -> Option<&'a JsonValue> {
        let JsonValue::Object(entries) = value else {
            return None;
        };
        entries
            .iter()
            .find_map(|(name, value)| (name == key).then_some(value))
    }
    fn event_payload(message: &str) -> Option<JsonValue> {
        let root = JsonParser::parse(message)?;
        if object_field(&root, "type")? != &JsonValue::String("send".into()) {
            return None;
        }
        object_field(&root, "payload").cloned()
    }
    pub fn is_event(message: &str, expected: &str) -> bool {
        event_payload(message).is_some_and(|payload| {
            object_field(&payload, "event") == Some(&JsonValue::String(expected.into()))
        })
    }
    pub fn drained_run_id(message: &str) -> Option<String> {
        let root = JsonParser::parse(message)?;
        if object_field(&root, "type")? != &JsonValue::String("send".into()) {
            return None;
        }
        let payload = object_field(&root, "payload")?;
        if object_field(payload, "event")? != &JsonValue::String("drained".into()) {
            return None;
        }
        if object_field(payload, "cleanupDone")? != &JsonValue::Bool(true) {
            return None;
        }
        match object_field(payload, "runId")? {
            JsonValue::String(run_id)
                if !run_id.is_empty() && run_id.bytes().all(|byte| byte.is_ascii_digit()) =>
            {
                Some(run_id.clone())
            }
            _ => None,
        }
    }
    pub fn fatal_queue_timeout(message: &str) -> bool {
        event_payload(message).is_some_and(|payload| {
            object_field(&payload, "event") == Some(&JsonValue::String("fatal".into()))
                && object_field(&payload, "error")
                    == Some(&JsonValue::String("gl_thread_queue_timeout".into()))
        })
    }
    pub fn result_field<'a>(contents: &'a str, key: &str) -> Option<&'a str> {
        let mut found = None;
        for line in contents.lines() {
            if let Some(value) = line.strip_prefix(&format!("{key}=")) {
                if found.is_some() {
                    return None;
                }
                found = Some(value);
            }
        }
        found
    }
    pub fn scope_result_to_run(contents: &str, run_id: &str) -> Option<String> {
        if run_id.is_empty()
            || result_field(contents, "status").is_none()
            || result_field(contents, "acceptedSequence").is_none()
            || result_field(contents, "hooksRemoved").is_none()
            || result_field(contents, "bindingCleared").is_none()
        {
            return None;
        }
        match result_field(contents, "runId") {
            Some(existing) if existing != run_id => None,
            Some(_) => Some(contents.to_owned()),
            None => Some(format!(
                "{}\nrunId={run_id}\n",
                contents.trim_end_matches('\n')
            )),
        }
    }
    pub fn result_has_status(contents: &str, expected: &str) -> bool {
        result_field(contents, "status") == Some(expected)
    }
    pub fn result_confirms_stopped_for_run(contents: &str, run_id: &str) -> bool {
        result_field(contents, "runId") == Some(run_id)
            && result_field(contents, "status") == Some("stopped")
            && result_field(contents, "acceptedSequence").is_some()
            && result_field(contents, "hooksRemoved") == Some("true")
            && result_field(contents, "bindingCleared") == Some("true")
    }
    pub fn draining_status(contents: &str) -> Option<String> {
        if result_field(contents, "status").is_none()
            || result_field(contents, "acceptedSequence").is_none()
            || result_field(contents, "hooksRemoved").is_none()
            || result_field(contents, "bindingCleared").is_none()
            || result_field(contents, "runId").is_none()
        {
            return None;
        }
        let mut fields = Vec::new();
        let (mut state_written, mut status_written, mut drain_written) = (false, false, false);
        for line in contents.lines() {
            if line.starts_with("state=") {
                if !state_written {
                    fields.push("state=stopping".to_owned());
                    state_written = true;
                }
            } else if line.starts_with("status=") {
                if !status_written {
                    fields.push("status=stopping".to_owned());
                    status_written = true;
                }
            } else if line.starts_with("agentDrainConfirmed=") {
                if !drain_written {
                    fields.push("agentDrainConfirmed=false".to_owned());
                    drain_written = true;
                }
            } else {
                fields.push(line.to_owned());
            }
        }
        if !state_written {
            fields.push("state=stopping".to_owned());
        }
        if !status_written {
            fields.push("status=stopping".to_owned());
        }
        if !drain_written {
            fields.push("agentDrainConfirmed=false".to_owned());
        }
        Some(format!("{}\n", fields.join("\n")))
    }
    pub fn preserve_drain_prompt(contents: &str, prompt: &str) -> String {
        let safe_prompt: String = prompt
            .chars()
            .map(|ch| {
                if matches!(ch, '\r' | '\n' | '=') {
                    ' '
                } else {
                    ch
                }
            })
            .collect();
        let mut prompt_written = false;
        let mut fields = Vec::new();
        for line in contents.lines() {
            if line.starts_with("reason=") {
                if !prompt_written {
                    fields.push(format!("reason={safe_prompt}"));
                    prompt_written = true;
                }
            } else {
                fields.push(line.to_owned());
            }
        }
        if !prompt_written {
            fields.push(format!("reason={safe_prompt}"));
        }
        format!("{}\n", fields.join("\n"))
    }
    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    pub enum DetachDecision {
        Wait,
        CleanupConfirmed,
        ProcessEnded,
    }
    pub fn detach_decision(
        process_ended: bool,
        result: &str,
        drained_ack: Option<&str>,
        run_id: &str,
    ) -> DetachDecision {
        if process_ended {
            return DetachDecision::ProcessEnded;
        }
        if drained_ack == Some(run_id) && result_confirms_stopped_for_run(result, run_id) {
            DetachDecision::CleanupConfirmed
        } else {
            DetachDecision::Wait
        }
    }
}
use native_drain_gate::{
    detach_decision, drained_run_id, draining_status, fatal_queue_timeout, is_event,
    preserve_drain_prompt, result_confirms_stopped_for_run, result_field as drain_result_field,
    result_has_status, scope_result_to_run, DetachDecision,
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
#[derive(Clone, Default)]
struct WorkerLifecycle {
    session_attached: bool,
    tick_loaded: bool,
    last_result: String,
}
static WORKER_LIFECYCLE: OnceLock<Mutex<WorkerLifecycle>> = OnceLock::new();
fn worker_lifecycle() -> &'static Mutex<WorkerLifecycle> {
    WORKER_LIFECYCLE.get_or_init(|| Mutex::new(WorkerLifecycle::default()))
}
fn reset_worker_session() {
    let mut lifecycle = worker_lifecycle().lock().unwrap_or_else(|e| e.into_inner());
    lifecycle.session_attached = false;
    lifecycle.tick_loaded = false;
}
fn set_worker_session_attached(attached: bool) {
    worker_lifecycle()
        .lock()
        .unwrap_or_else(|e| e.into_inner())
        .session_attached = attached;
}
fn record_lua_result(contents: &str) {
    if !is_complete_result(contents) {
        return;
    }
    let mut lifecycle = worker_lifecycle().lock().unwrap_or_else(|e| e.into_inner());
    lifecycle.tick_loaded = true;
    lifecycle.last_result = contents.to_owned();
}
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
fn status_raw(dir: &Path, s: &str) {
    let p = dir.join("status");
    if replace_atomic(&p, s.as_bytes(), 0o644).is_ok() {
        if let (Ok(owner), Ok(path)) = (
            fs::metadata(dir),
            CString::new(p.to_string_lossy().as_bytes()),
        ) {
            unsafe {
                let _ = chown(path.as_ptr(), owner.uid(), u32::MAX);
            }
        }
    }
}
fn status(dir: &Path, s: &str) {
    let current = fs::read_to_string(dir.join("status")).unwrap_or_default();
    let body = if is_complete_result(s) {
        s.to_owned()
    } else {
        merge_worker_status_message(&current, s).unwrap_or_else(|| s.to_owned())
    };
    status_raw(dir, &body);
    if is_complete_result(&body) {
        worker_lifecycle()
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .last_result = body;
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
        && (s == "/data/user/0/com.xnas.wpe.suite/files/modules/starhunt"
            || s == "/data/data/com.xnas.wpe.suite/files/modules/starhunt")
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
    command: &Path,
    stop: &Path,
    baseline_sequence: u64,
    run: &str,
    mode: &str,
    lease: &Path,
    worker: &Path,
    uid: u32,
) -> Result<(), String> {
    let cfg = format!(
        "{{resultPath={},bindingPath={},commandPath={},stopPath={},leasePath={},workerPath={},baselineSequence={},stateKey={},runId={},mode={}}}",
        lua_quote(&result.to_string_lossy()),
        lua_quote(&binding.to_string_lossy()),
        lua_quote(&command.to_string_lossy()),
        lua_quote(&stop.to_string_lossy()),
        lua_quote(&lease.to_string_lossy()),
        lua_quote(&worker.to_string_lossy()),
        baseline_sequence,
        lua_quote("__codex_starhunt_v1"),
        lua_quote(run),
        lua_quote(mode)
    );
    let source = template.replace("__CONFIG__", &cfg);
    create_atomic_owned(path, source.as_bytes(), 0o600, uid)
        .map_err(|e| format!("create {}: {e}", path.display()))
}

#[derive(Clone, Debug, PartialEq, Eq)]
struct StarHuntCommand {
    sequence: u64,
    mode: String,
    levels: Vec<u32>,
    map: u32,
}

fn parse_starhunt_command(contents: &str) -> Option<StarHuntCommand> {
    let mut sequence = None;
    let mut mode = None;
    let mut level = None;
    let mut levels = None;
    let mut map = None;
    for line in contents.lines().filter(|line| !line.is_empty()) {
        let (key, value) = line.split_once('=')?;
        match key {
            "sequence" if sequence.is_none() => {
                sequence = Some(parse_starhunt_unsigned(value, 9_007_199_254_740_991)?)
            }
            "mode" if mode.is_none() => mode = Some(value.to_owned()),
            "level" if level.is_none() => level = Some(parse_starhunt_unsigned(value, 999)? as u32),
            "levels" if levels.is_none() => levels = Some(parse_starhunt_levels(value)),
            "map" if map.is_none() => {
                map = Some(parse_starhunt_unsigned(value, 2_147_483_647)? as u32)
            }
            _ => return None,
        }
    }
    if level.is_some() && levels.is_some() {
        return None;
    }
    let mode_value = mode?;
    if !matches!(mode_value.as_str(), "scan" | "run" | "teleport") {
        return None;
    }
    let selected_levels = match (levels, level, mode_value.as_str()) {
        (Some(Some(values)), None, _) => values,
        (Some(None), None, _) => return None,
        (None, Some(0), "scan") => Vec::new(),
        (None, Some(value @ 1..=999), "scan") => vec![value as u32],
        (None, Some(value @ 1..=999), "run") => vec![value as u32],
        _ => return None,
    };
    let command = StarHuntCommand {
        sequence: sequence?,
        mode: mode_value,
        levels: selected_levels,
        map: map?,
    };
    if command.sequence == 0
        || (command.mode == "run" && (command.levels.is_empty() || command.map == 0))
        || (command.mode == "teleport" && (command.map == 0 || !command.levels.is_empty()))
    {
        return None;
    }
    Some(command)
}

fn parse_starhunt_unsigned(value: &str, maximum: u64) -> Option<u64> {
    if value.is_empty() || !value.bytes().all(|byte| byte.is_ascii_digit()) {
        return None;
    }
    let number = value.parse::<u64>().ok()?;
    (number <= maximum).then_some(number)
}

fn parse_starhunt_levels(value: &str) -> Option<Vec<u32>> {
    if value.is_empty() {
        return Some(Vec::new());
    }
    let mut levels = Vec::new();
    let mut previous = 0;
    for token in value.split(',') {
        if token.is_empty() || token.starts_with('0') {
            return None;
        }
        let level = parse_starhunt_unsigned(token, 999)? as u32;
        if level == 0 || level <= previous {
            return None;
        }
        levels.push(level);
        previous = level;
    }
    Some(levels)
}

fn game_module_dir(cache: &Path, uid: u32) -> Result<PathBuf, String> {
    let modules = cache.join("wpe-modules");
    let module = modules.join("starhunt");
    for path in [&modules, &module] {
        if let Ok(metadata) = fs::symlink_metadata(path) {
            if metadata.file_type().is_symlink() || !metadata.is_dir() {
                return Err(format!(
                    "refusing non-directory game path {}",
                    path.display()
                ));
            }
        }
    }
    let modules_existed = modules.is_dir();
    let module_existed = module.is_dir();
    fs::create_dir_all(&module)
        .map_err(|e| format!("create game module directory {}: {e}", module.display()))?;
    for (path, existed) in [(&modules, modules_existed), (&module, module_existed)] {
        if existed {
            continue;
        }
        fs::set_permissions(path, fs::Permissions::from_mode(0o755))
            .map_err(|e| format!("chmod {}: {e}", path.display()))?;
        let c = CString::new(path.to_string_lossy().as_bytes()).map_err(|e| e.to_string())?;
        if unsafe { chown(c.as_ptr(), uid, u32::MAX) } != 0 {
            return Err(format!(
                "chown {}: {}",
                path.display(),
                io::Error::last_os_error()
            ));
        }
    }
    Ok(module)
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
fn clean_result_value(value: &str) -> String {
    value
        .chars()
        .map(|ch| {
            if matches!(ch, '\r' | '\n' | '=') {
                ' '
            } else {
                ch
            }
        })
        .collect()
}
fn replace_result_fields(contents: &str, updates: &[(&str, &str)]) -> String {
    let mut found = vec![false; updates.len()];
    let mut lines = Vec::new();
    for line in contents.lines() {
        if let Some((index, (key, value))) = updates
            .iter()
            .enumerate()
            .find(|(_, (key, _))| line.starts_with(&format!("{key}=")))
        {
            if !found[index] {
                lines.push(format!("{key}={}", clean_result_value(value)));
                found[index] = true;
            }
        } else {
            lines.push(line.to_owned());
        }
    }
    for (index, (key, value)) in updates.iter().enumerate() {
        if !found[index] {
            lines.push(format!("{key}={}", clean_result_value(value)));
        }
    }
    format!("{}\n", lines.join("\n"))
}
fn has_result_field(contents: &str, key: &str) -> bool {
    contents
        .lines()
        .any(|line| line.starts_with(&format!("{key}=")))
}
fn is_complete_result(contents: &str) -> bool {
    has_result_field(contents, "status")
        && has_result_field(contents, "acceptedSequence")
        && has_result_field(contents, "hooksRemoved")
        && has_result_field(contents, "bindingCleared")
}
fn result_field<'a>(contents: &'a str, key: &str) -> Option<&'a str> {
    contents
        .lines()
        .find_map(|line| line.strip_prefix(&format!("{key}=")))
}
fn merge_worker_status_message(current: &str, message: &str) -> Option<String> {
    if !is_complete_result(current) {
        return None;
    }
    let current_status = result_field(current, "status").unwrap_or("waiting_role");
    let next_status = if message.starts_with("GL thread") {
        "waiting_role"
    } else if message.contains("game_gone") || message.contains("worker stopped") {
        "stopped"
    } else if message == "stopping"
        || message.contains("cleaning")
        || message.contains("cleanup")
        || message.contains("清理")
        || message.contains("停止")
    {
        "stopping"
    } else if message.starts_with("waiting_game") || message.contains("identity_") {
        "waiting_role"
    } else {
        current_status
    };
    Some(replace_result_fields(
        current,
        &[("status", next_status), ("reason", message)],
    ))
}
fn inactive_result_status(
    status: &str,
    reason: &str,
    accepted_sequence: u64,
    hooks_removed: bool,
    binding_cleared: bool,
) -> String {
    let updated_at = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_secs();
    format!(
        "status={status}\nmap=unknown\nmapName=unknown\nrole=unknown\nlevels=\nlevelNames=\nselectedLevel=0\ntargetId=unknown\ntargetName=unknown\ntargetX=unknown\ntargetY=unknown\ncalls=0\nteleports=0\nchallenges=0\nskipped=0\nreason={}\nupdatedAt={updated_at}\nhooksRemoved={hooks_removed}\nbindingCleared={binding_cleared}\nacceptedSequence={accepted_sequence}\n",
        clean_result_value(reason)
    )
}
fn cleanup_flags_before_lua(session_attached: bool, tick_loaded: bool) -> (bool, bool) {
    if !session_attached || !tick_loaded {
        (true, true)
    } else {
        (false, false)
    }
}
fn stopping_result_status(
    current: &str,
    accepted_sequence: u64,
    session_attached: bool,
    tick_loaded: bool,
) -> String {
    if is_complete_result(current) {
        return replace_result_fields(
            current,
            &[("status", "stopping"), ("reason", "worker_stop_requested")],
        );
    }
    let (hooks_removed, binding_cleared) = cleanup_flags_before_lua(session_attached, tick_loaded);
    inactive_result_status(
        "stopping",
        "worker_stop_requested",
        accepted_sequence,
        hooks_removed,
        binding_cleared,
    )
}
fn worker_error_status(
    error: &str,
    accepted_sequence: u64,
    current: &str,
    last_result: &str,
    session_attached: bool,
    tick_loaded: bool,
) -> String {
    let reason: String = clean_result_value(error).chars().take(220).collect();
    if !session_attached || !tick_loaded {
        let (hooks_removed, binding_cleared) =
            cleanup_flags_before_lua(session_attached, tick_loaded);
        return inactive_result_status(
            "fatal",
            &reason,
            accepted_sequence,
            hooks_removed,
            binding_cleared,
        );
    }
    let source = if is_complete_result(current) {
        current
    } else {
        last_result
    };
    if is_complete_result(source) {
        replace_result_fields(source, &[("status", "fatal"), ("reason", reason.as_str())])
    } else {
        inactive_result_status("fatal", &reason, accepted_sequence, false, false)
    }
}
fn publish_worker_error(dir: &Path, error: &str) {
    let accepted = fs::read_to_string(dir.join("command"))
        .ok()
        .and_then(|contents| parse_starhunt_command(&contents))
        .map(|command| command.sequence)
        .unwrap_or(0);
    let current = fs::read_to_string(dir.join("status")).unwrap_or_default();
    let lifecycle = worker_lifecycle()
        .lock()
        .unwrap_or_else(|e| e.into_inner())
        .clone();
    let body = worker_error_status(
        error,
        accepted,
        &current,
        &lifecycle.last_result,
        lifecycle.session_attached,
        lifecycle.tick_loaded,
    );
    status(dir, &body);
}
fn self_test() -> Result<(), String> {
    assert!(valid_dir(Path::new(
        "/data/user/0/com.xnas.wpe.suite/files/modules/starhunt"
    )));
    assert!(!valid_dir(Path::new(
        "/data/data/com.xnas.wpe.suite/files/modules/starhunt/run"
    )));
    assert!(!valid_dir(Path::new("/data/user/0/other/files")));
    assert!(!valid_dir(Path::new(
        "/data/user/0/com.xnas.wpe.suite/files/modules/starhunt/../x"
    )));
    assert_eq!(
        parse_starhunt_command("sequence=7\nmode=run\nlevel=21\nmap=1018\n"),
        Some(StarHuntCommand {
            sequence: 7,
            mode: "run".into(),
            levels: vec![21],
            map: 1018,
        })
    );
    assert_eq!(
        parse_starhunt_command("sequence=7\nmode=run\nlevels=8,9,10\nmap=1018\n")
            .unwrap()
            .levels,
        vec![8, 9, 10]
    );
    assert_eq!(
        parse_starhunt_command("sequence=7\nmode=scan\nlevels=\nmap=0\n")
            .unwrap()
            .levels,
        Vec::<u32>::new()
    );
    assert!(parse_starhunt_command("sequence=0\nmode=run\nlevels=1\nmap=1\n").is_none());
    assert!(parse_starhunt_command("sequence=7\nmode=run\nlevels=\nmap=1\n").is_none());
    assert!(parse_starhunt_command("sequence=7\nmode=run\nlevels=1\nmap=0\n").is_none());
    assert!(parse_starhunt_command("sequence=7\nmode=scan\nlevel=0\nmap=0\n").is_some());
    assert_eq!(
        parse_starhunt_command("sequence=7\nmode=scan\nlevel=1\nmap=0\n")
            .unwrap()
            .levels,
        vec![1]
    );
    for invalid in [
        "sequence=7\nmode=run\nlevels=9,8\nmap=1\n",
        "sequence=7\nmode=run\nlevels=8,8\nmap=1\n",
        "sequence=7\nmode=run\nlevels=0,8\nmap=1\n",
        "sequence=7\nmode=run\nlevels=8,1000\nmap=1\n",
        "sequence=7\nmode=run\nlevels=08\nmap=1\n",
        "sequence=7\nmode=run\nlevels=8,\nmap=1\n",
        "sequence=7\nmode=run\nlevels=8\nlevel=8\nmap=1\n",
        "sequence=7\nmode=run\nlevels=8\nmap=1\nextra=x\n",
        "sequence=7\nmode=run\nlevels=8\nlevels=8\nmap=1\n",
        "sequence=7\nmode=run\nlevel=0\nmap=1\n",
    ] {
        assert!(
            parse_starhunt_command(invalid).is_none(),
            "accepted invalid command: {invalid:?}"
        );
    }
    assert!(
        parse_starhunt_command("sequence=7\nsequence=8\nmode=scan\nlevel=0\nmap=0\n").is_none()
    );
    let replaced = replace_result_fields(
        "status=reading\nacceptedSequence=4\nacceptedSequence=5\n",
        &[
            ("status", "stopped"),
            ("acceptedSequence", "6"),
            ("hooksRemoved", "true"),
        ],
    );
    assert_eq!(
        replaced,
        "status=stopped\nacceptedSequence=6\nhooksRemoved=true\n"
    );
    assert!(inactive_result_status("stopped", "", 9, true, true)
        .contains("acceptedSequence=9\nhooksRemoved=true\nbindingCleared=true\n"));
    let pre_attach_error = worker_error_status("attach failed", 10, "starting", "", false, false);
    assert!(pre_attach_error.contains("hooksRemoved=true\nbindingCleared=true\n"));
    let pre_tick_error = worker_error_status("tick not started", 10, "starting", "", true, false);
    assert!(pre_tick_error.contains("hooksRemoved=true\nbindingCleared=true\n"));
    assert_eq!(cleanup_flags_before_lua(false, false), (true, true));
    assert_eq!(cleanup_flags_before_lua(true, false), (true, true));
    assert_eq!(cleanup_flags_before_lua(true, true), (false, false));
    let before_tick_stop = stopping_result_status("starting", 12, true, false);
    assert!(
        before_tick_stop.contains("acceptedSequence=12\nhooksRemoved=true\nbindingCleared=true\n")
    );
    let active_stop = stopping_result_status("starting", 12, true, true);
    assert!(active_stop.contains("hooksRemoved=false\nbindingCleared=false\n"));
    let active_result = inactive_result_status("teleport_wait", "", 10, false, false);
    let active_error = worker_error_status(
        "worker failed",
        11,
        "plain status",
        &active_result,
        true,
        true,
    );
    assert!(active_error.contains("hooksRemoved=false\nbindingCleared=false\n"));
    let cleaned_result = inactive_result_status("stopped", "worker_stop", 11, true, true);
    let confirmed_stop = stopping_result_status(&cleaned_result, 12, true, true);
    assert!(confirmed_stop.contains("hooksRemoved=true\nbindingCleared=true\n"));
    let preserved_cleanup =
        merge_worker_status_message(&cleaned_result, "GL thread恢复后已清理，等待重新绑定")
            .expect("complete result status should merge worker messages");
    assert!(preserved_cleanup.contains("status=waiting_role\n"));
    assert!(
        preserved_cleanup.contains("acceptedSequence=11\nhooksRemoved=true\nbindingCleared=true\n")
    );
    let post_cleanup_error = worker_error_status(
        "late worker error",
        12,
        "plain status",
        &cleaned_result,
        true,
        true,
    );
    assert!(post_cleanup_error.contains("hooksRemoved=true\nbindingCleared=true\n"));
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
    reset_worker_session();
    if !valid_dir(&dir) {
        return Err("--dir must be the app private files directory".into());
    }
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
    status_raw(&dir, "starting");
    license_lease::require_module_lease(&dir)?;
    let stop = dir.join("stop");
    let template = include_str!("../../../../app/src/main/assets/starhunt.lua.in");
    let agent = include_str!("../../../../app/src/main/assets/starhunt_agent.js");
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
    let mut last_mirrored_command: Option<String> = None;
    let mut stop_mirrored = false;
    let mut active_paths: Option<(PathBuf, PathBuf, PathBuf, PathBuf, PathBuf, PathBuf)> = None;
    loop {
        if active_paths.is_none() && stop.exists() {
            clear_stop_file(&stop)?;
            let accepted = fs::read_to_string(dir.join("command"))
                .ok()
                .and_then(|contents| parse_starhunt_command(&contents))
                .map(|command| command.sequence)
                .unwrap_or(0);
            status(
                &dir,
                &inactive_result_status("stopped", "nothing_bound", accepted, true, true),
            );
            return Ok(());
        }
        if !lease_fresh(&dir.join("lease")) {
            let accepted = fs::read_to_string(dir.join("command"))
                .ok()
                .and_then(|contents| parse_starhunt_command(&contents))
                .map(|command| command.sequence)
                .unwrap_or(0);
            status(
                &dir,
                &inactive_result_status("stopped", "lease_expired", accepted, true, true),
            );
            return Ok(());
        }
        if stop.exists() {
            if active_paths.is_some() {
                status(&dir, "stopping");
            } else {
                clear_stop_file(&stop)?;
                let accepted = fs::read_to_string(dir.join("command"))
                    .ok()
                    .and_then(|contents| parse_starhunt_command(&contents))
                    .map(|command| command.sequence)
                    .unwrap_or(0);
                status(
                    &dir,
                    &inactive_result_status("stopped", "nothing_bound", accepted, true, true),
                );
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
        let base = format!("codex-starhunt-{pid}-{ticks}");
        let same_identity = active_paths.as_ref().is_some_and(|(old, _, _, _, _, _)| {
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
            .is_none_or(|(old, _, _, _, _, _)| old != &work)
        {
            let tick = cache.join(format!("{prefix}-tick.lua"));
            let cleanup = cache.join(format!("{prefix}-cleanup.lua"));
            let result = cache.join(format!("{prefix}-result"));
            let binding = cache.join(format!("{prefix}-binding"));
            let lease_mirror = cache.join(format!("{prefix}-lease"));
            let worker_beat = cache.join(format!("{prefix}-worker-heartbeat"));
            let game_module = game_module_dir(&cache, uid)?;
            let command_mirror = game_module.join(format!("{prefix}-command"));
            let stop_mirror = game_module.join(format!("{prefix}-stop"));
            let initial_command = fs::read_to_string(dir.join("command")).unwrap_or_default();
            let baseline_sequence = parse_starhunt_command(&initial_command)
                .map(|command| command.sequence)
                .unwrap_or(0);
            if initial_command.is_empty() {
                let _ = fs::remove_file(&command_mirror);
                last_mirrored_command = None;
            } else {
                write_game_file(&command_mirror, &initial_command, uid)?;
                last_mirrored_command = Some(initial_command);
            }
            if stop.exists() {
                write_game_file(&stop_mirror, "stop\n", uid)?;
                stop_mirrored = true;
            } else {
                let _ = fs::remove_file(&stop_mirror);
                stop_mirrored = false;
            }
            for p in [
                &tick,
                &cleanup,
                &result,
                &binding,
                &lease_mirror,
                &worker_beat,
            ] {
                if p.exists() {
                    return Err(format!("refusing collision at {}", p.display()));
                }
            }
            write_lua(
                &tick,
                template,
                &result,
                &binding,
                &command_mirror,
                &stop_mirror,
                baseline_sequence,
                &run_id,
                "tick",
                &lease_mirror,
                &worker_beat,
                uid,
            )?;
            write_lua(
                &cleanup,
                template,
                &result,
                &binding,
                &command_mirror,
                &stop_mirror,
                baseline_sequence,
                &run_id,
                "stop",
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
            active_paths = Some((
                work,
                tick,
                result,
                binding,
                command_mirror.clone(),
                stop_mirror.clone(),
            ));
        }
        let (_, tick, result, binding, command_mirror, stop_mirror) =
            active_paths.as_ref().unwrap();
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
                &*command_mirror,
                &*stop_mirror,
                &lease_mirror,
                &worker_beat,
            ] {
                let _ = fs::remove_file(path);
            }
            active_paths = None;
            status(&dir, "identity_changed");
            continue;
        }
        reset_worker_session();
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
        set_worker_session_attached(true);
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
            set_worker_session_attached(false);
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
        let name = cstr("starhunt-worker")?;
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
        let mut drained_for_run = false;
        let mut drain_prompt_reason: Option<&'static str> = None;
        let mut cleanup_started = None;
        let mut cleanup_prompted = false;
        let mut identity_checked = std::time::Instant::now();
        let mut result_checked = std::time::Instant::now() - Duration::from_millis(100);
        let mut command_checked = std::time::Instant::now() - Duration::from_millis(100);
        let mut beat_at = std::time::Instant::now() - Duration::from_secs(1);
        let mut result_snapshot = String::new();
        let mut binding_snapshot = None;
        while !detached {
            unsafe { _frida_g_main_context_iteration(ptr::null_mut(), 0) };
            if beat_at.elapsed() >= Duration::from_secs(1) && !stopping {
                let app_lease =
                    fs::read_to_string(dir.join("lease")).unwrap_or_else(|_| "0".into());
                let beat = SystemTime::now()
                    .duration_since(UNIX_EPOCH)
                    .unwrap_or_default()
                    .as_secs()
                    .to_string();
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
                if !lease_fresh(&dir.join("lease"))
                    || license_lease::require_module_lease(&dir).is_err()
                {
                    status(&dir, "app lease expired; cleaning Lua state");
                    stopping = true;
                }
            }
            if command_checked.elapsed() >= Duration::from_millis(100) {
                command_checked = std::time::Instant::now();
                let current = fs::read_to_string(dir.join("command")).unwrap_or_default();
                if last_mirrored_command.as_deref() != Some(current.as_str()) {
                    if let Err(error) = write_game_file(command_mirror, &current, uid) {
                        fatal_exit = Some(format!("command mirror failed: {error}"));
                        status(
                            &dir,
                            "state=stopping\nstatus=stopping\nreason=command_mirror_failed",
                        );
                        stopping = true;
                    } else {
                        last_mirrored_command = Some(current);
                    }
                }
                let should_mirror_stop = stop.exists();
                if should_mirror_stop != stop_mirrored {
                    let stop_result = if should_mirror_stop {
                        write_game_file(stop_mirror, "stop\n", uid)
                    } else {
                        fs::remove_file(stop_mirror)
                            .or_else(|error| {
                                if error.kind() == io::ErrorKind::NotFound {
                                    Ok(())
                                } else {
                                    Err(error)
                                }
                            })
                            .map_err(|error| format!("remove stop mirror: {error}"))
                    };
                    if let Err(error) = stop_result {
                        fatal_exit = Some(format!("stop mirror failed: {error}"));
                        status(
                            &dir,
                            "state=stopping\nstatus=stopping\nreason=stop_mirror_failed",
                        );
                        stopping = true;
                    } else {
                        stop_mirrored = should_mirror_stop;
                    }
                }
            }
            while let Ok(msg) = rx.try_recv() {
                if is_event(&msg, "drained") {
                    if drained_run_id(&msg).as_deref() == Some(run_id.as_str()) {
                        drained_for_run = true;
                        stopping = true;
                    }
                    continue;
                }
                if is_event(&msg, "fatal") {
                    let timeout = fatal_queue_timeout(&msg);
                    retry_timeout |= timeout;
                    if timeout {
                        drain_prompt_reason = Some("请返回游戏以完成停止");
                    }
                    if !timeout {
                        fatal_exit = Some("StarHunt agent reported a fatal error".into())
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
                if let Some((_, _, result, binding, _, _)) = &active_paths {
                    let raw_result = fs::read_to_string(result).unwrap_or_default();
                    result_snapshot = scope_result_to_run(&raw_result, &run_id).unwrap_or_default();
                    record_lua_result(&result_snapshot);
                    binding_snapshot = read_kv(binding);
                    if result_has_status(&result_snapshot, "stopped") {
                        stopping = true;
                    }
                }
                result_checked = std::time::Instant::now();
            }
            if !result_snapshot.is_empty() && !stopping && !stop.exists() {
                status(&dir, &result_snapshot);
            }
            if stop.exists() || stopping {
                let accepted = fs::read_to_string(dir.join("command"))
                    .ok()
                    .and_then(|contents| parse_starhunt_command(&contents))
                    .map(|command| command.sequence)
                    .unwrap_or(0);
                let lifecycle = worker_lifecycle()
                    .lock()
                    .unwrap_or_else(|e| e.into_inner())
                    .clone();
                let stopping_status = stopping_result_status(
                    &result_snapshot,
                    accepted,
                    lifecycle.session_attached,
                    lifecycle.tick_loaded,
                );
                let scoped_status =
                    scope_result_to_run(&stopping_status, &run_id).unwrap_or(stopping_status);
                let mut public_stopping =
                    draining_status(&scoped_status).unwrap_or_else(|| scoped_status.clone());
                if let Some(prompt) = drain_prompt_reason {
                    public_stopping = preserve_drain_prompt(&public_stopping, prompt);
                }
                status(&dir, &public_stopping);
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
                    if detach_decision(
                        false,
                        &result_snapshot,
                        drained_for_run.then_some(run_id.as_str()),
                        &run_id,
                    ) == DetachDecision::CleanupConfirmed
                    {
                        detached = true;
                    } else if cleanup_started.unwrap().elapsed() > Duration::from_secs(5)
                        && !cleanup_prompted
                    {
                        drain_prompt_reason =
                            Some("请返回游戏以完成停止（worker仍保留，cleanup尚未确认）");
                        status(
                            &dir,
                            "请返回游戏以完成停止（worker仍保留，cleanup尚未确认）",
                        );
                        cleanup_prompted = true;
                    }
                }
            } else if active_paths.is_some() {
                if result_has_status(&result_snapshot, "fatal") {
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
                        let uid_ok =
                            active_paths
                                .as_ref()
                                .is_some_and(|(_, _, _, binding, _, _)| {
                                    fs::metadata(binding).is_ok_and(|m| m.uid() == uid)
                                });
                        if !uid_ok || player.is_empty() || manager_text.is_empty() {
                            fatal_exit = Some("binding owner or identity validation failed".into());
                            stopping = true;
                        }
                    }
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
        for p in [
            tick,
            result,
            binding,
            command_mirror,
            stop_mirror,
            &lease_mirror,
            &worker_beat,
        ] {
            let _ = fs::remove_file(p);
        }
        let _ = fs::remove_file(cache.join(format!("{prefix}-cleanup.lua")));
        let accepted = fs::read_to_string(dir.join("command"))
            .ok()
            .and_then(|contents| parse_starhunt_command(&contents))
            .map(|command| command.sequence)
            .unwrap_or(0);
        let final_status = if terminal_detach {
            let source = if is_complete_result(&result_snapshot) {
                result_snapshot.as_str()
            } else {
                ""
            };
            let base = if source.is_empty() {
                inactive_result_status(
                    "stopped",
                    "original_process_ended_cleanup_unconfirmed",
                    accepted,
                    false,
                    false,
                )
            } else {
                source.to_owned()
            };
            replace_result_fields(
                &base,
                &[
                    ("status", "stopped"),
                    ("state", "stopped"),
                    ("reason", "original_process_ended_cleanup_unconfirmed"),
                    ("hooksRemoved", "false"),
                    ("bindingCleared", "false"),
                    ("luaCleanupConfirmed", "false"),
                    ("agentDrainConfirmed", "false"),
                    ("sessionDetached", "true"),
                ],
            )
        } else if detach_decision(
            false,
            &result_snapshot,
            drained_for_run.then_some(run_id.as_str()),
            &run_id,
        ) == DetachDecision::CleanupConfirmed
        {
            replace_result_fields(
                &result_snapshot,
                &[
                    ("status", "stopped"),
                    ("state", "stopped"),
                    ("hooksRemoved", "true"),
                    ("bindingCleared", "true"),
                    ("agentDrainConfirmed", "true"),
                ],
            )
        } else {
            inactive_result_status("stopping", "drain_gate_unconfirmed", accepted, false, false)
        };
        status(&dir, &final_status);
        let stop_requested = stop.exists();
        if stop_requested {
            if let Err(clear_error) = clear_stop_file(&stop) {
                return Err(match unload_error {
                    Some(unload_error_message) => {
                        format!("{unload_error_message}; {clear_error}")
                    }
                    None => clear_error,
                });
            }
        }
        if stop_requested || terminal_detach {
            return match unload_error {
                Some(error) => Err(format!("unload after cleanup: {error}")),
                None => Ok(()),
            };
        }
        if let Some(e) = fatal_exit {
            if result_confirms_stopped_for_run(&result_snapshot, &run_id) && drained_for_run {
                status(
                    &dir,
                    &replace_result_fields(
                        &result_snapshot,
                        &[
                            ("status", "fatal"),
                            ("reason", &e),
                            ("agentDrainConfirmed", "true"),
                        ],
                    ),
                );
            }
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
        reset_worker_session();
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
        Err("usage: starhunt_worker --daemon --dir <private-files> | --stop --dir <private-files> | --self-test".into())
    });
    match r {
        Ok(Ok(())) => ExitCode::SUCCESS,
        Ok(Err(e)) => {
            if e != "worker already running" {
                if let Some(dir) = status_dir.as_deref().filter(|p| valid_dir(p)) {
                    publish_worker_error(dir, &e);
                }
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
