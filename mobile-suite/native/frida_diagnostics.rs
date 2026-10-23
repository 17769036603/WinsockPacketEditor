use std::{
    ffi::CStr,
    io::{self, Write},
    thread,
    time::Duration,
};

pub struct Api {
    pub init: unsafe extern "C" fn(),
    pub deinit: unsafe extern "C" fn(),
    pub version_string: unsafe extern "C" fn() -> *const std::ffi::c_char,
}

const FLAG: &str = "--suite-native-self-test";

pub fn parse_hold_ms(args: &[String]) -> Result<u64, &'static str> {
    match args {
        [flag] if flag == FLAG => Ok(0),
        [flag, hold, value] if flag == FLAG && hold == "--hold-ms" => {
            if value.is_empty() || !value.bytes().all(|b| b.is_ascii_digit()) {
                return Err("--hold-ms requires an integer from 0 to 60000");
            }
            let value = value
                .parse::<u64>()
                .map_err(|_| "--hold-ms requires an integer from 0 to 60000")?;
            if value > 60_000 {
                return Err("--hold-ms requires an integer from 0 to 60000");
            }
            Ok(value)
        }
        _ => Err("usage: --suite-native-self-test [--hold-ms 0..60000]"),
    }
}

fn emit(pid: u32, version: &str, phase: &str) -> io::Result<()> {
    let mut out = io::stdout().lock();
    let mut escaped = String::with_capacity(version.len());
    for ch in version.chars() {
        match ch {
            '"' => escaped.push_str("\\\""),
            '\\' => escaped.push_str("\\\\"),
            '\u{0000}'..='\u{001f}' => escaped.push_str(&format!("\\u{:04x}", ch as u32)),
            _ => escaped.push(ch),
        }
    }
    writeln!(
        out,
        "{{\"pid\":{pid},\"version\":\"{escaped}\",\"phase\":\"{phase}\"}}"
    )?;
    out.flush()
}

/// Returns Some(exit code) only when argv[1] exactly selects this diagnostic.
/// It validates all options before touching Frida, and never enters worker logic.
pub fn dispatch(args: &[String], pid: u32, api: Api) -> Option<i32> {
    if args.first().map(String::as_str) != Some(FLAG) {
        return None;
    }
    let hold = match parse_hold_ms(args) {
        Ok(value) => value,
        Err(error) => {
            eprintln!("{error}");
            return Some(2);
        }
    };

    unsafe { (api.init)() };
    let raw_version = unsafe { (api.version_string)() };
    let version = if raw_version.is_null() {
        None
    } else {
        unsafe { CStr::from_ptr(raw_version) }.to_str().ok()
    };
    let Some(version) = version.map(str::to_owned) else {
        unsafe { (api.deinit)() };
        eprintln!("Frida returned a null or non-UTF-8 version string");
        return Some(1);
    };

    if emit(pid, &version, "ready").is_err() {
        unsafe { (api.deinit)() };
        return Some(1);
    }
    if hold != 0 {
        thread::sleep(Duration::from_millis(hold));
    }
    unsafe { (api.deinit)() };
    if emit(pid, &version, "finished").is_err() {
        return Some(1);
    }
    Some(0)
}
