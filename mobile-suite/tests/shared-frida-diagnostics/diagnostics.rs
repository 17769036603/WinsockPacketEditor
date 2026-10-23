#[path = "../../native/frida_diagnostics.rs"]
mod diagnostics;

use std::{
    ffi::c_char,
    sync::{Mutex, MutexGuard},
};

static LOCK: Mutex<()> = Mutex::new(());
static EVENTS: Mutex<Vec<&'static str>> = Mutex::new(Vec::new());
static VERSION: &[u8] = b"17.17.0-test\0";
static BAD_UTF8_VERSION: &[u8] = b"\xff\0";

fn guard() -> MutexGuard<'static, ()> {
    LOCK.lock().unwrap_or_else(|e| e.into_inner())
}

unsafe extern "C" fn init() {
    EVENTS.lock().unwrap().push("init");
}

unsafe extern "C" fn deinit() {
    EVENTS.lock().unwrap().push("deinit");
}

unsafe extern "C" fn version() -> *const c_char {
    EVENTS.lock().unwrap().push("version");
    VERSION.as_ptr().cast()
}

unsafe extern "C" fn bad_utf8_version() -> *const c_char {
    BAD_UTF8_VERSION.as_ptr().cast()
}

unsafe extern "C" fn null_version() -> *const c_char {
    std::ptr::null()
}

fn api() -> diagnostics::Api {
    diagnostics::Api {
        init,
        deinit,
        version_string: version,
    }
}

fn args(parts: &[&str]) -> Vec<String> {
    parts.iter().map(|part| (*part).to_owned()).collect()
}

#[test]
fn exact_selector_short_circuits_worker_dispatch() {
    let _guard = guard();
    EVENTS.lock().unwrap().clear();
    let input = args(&["--suite-native-self-test"]);
    let mut business_path_entered = false;
    let diagnostic = diagnostics::dispatch(&input, 123, api());
    if diagnostic.is_none() {
        business_path_entered = true;
    }
    assert_eq!(diagnostic, Some(0));
    assert!(!business_path_entered);
    assert_eq!(*EVENTS.lock().unwrap(), ["init", "version", "deinit"]);
}

#[test]
fn other_commands_leave_frida_untouched_for_normal_worker_path() {
    let _guard = guard();
    EVENTS.lock().unwrap().clear();
    assert_eq!(
        diagnostics::dispatch(&args(&["--self-test"]), 9, api()),
        None
    );
    assert_eq!(
        diagnostics::dispatch(&args(&["--suite-native-self-test-extra"]), 9, api()),
        None
    );
    assert!(EVENTS.lock().unwrap().is_empty());
}

#[test]
fn malformed_hold_values_fail_before_frida_initialization() {
    let _guard = guard();
    for value in ["", "-1", "+1", "1x", "60001", "999999999999999999999"] {
        EVENTS.lock().unwrap().clear();
        let input = args(&["--suite-native-self-test", "--hold-ms", value]);
        assert_eq!(
            diagnostics::dispatch(&input, 9, api()),
            Some(2),
            "{value:?}"
        );
        assert!(EVENTS.lock().unwrap().is_empty(), "{value:?}");
    }
    for suffix in [
        vec!["--suite-native-self-test", "--hold-ms"],
        vec!["--suite-native-self-test", "--hold-ms", "0", "extra"],
        vec!["--suite-native-self-test", "--hold", "0"],
    ] {
        let input = args(&suffix);
        assert_eq!(diagnostics::dispatch(&input, 9, api()), Some(2));
        assert!(EVENTS.lock().unwrap().is_empty());
    }
}

#[test]
fn hold_parser_accepts_both_inclusive_bounds_without_running_the_delay() {
    assert_eq!(
        diagnostics::parse_hold_ms(&args(&["--suite-native-self-test", "--hold-ms", "0"])),
        Ok(0)
    );
    assert_eq!(
        diagnostics::parse_hold_ms(&args(&["--suite-native-self-test", "--hold-ms", "60000"])),
        Ok(60_000)
    );
}

#[test]
fn hold_zero_is_accepted_and_library_lifetime_is_balanced() {
    let _guard = guard();
    EVENTS.lock().unwrap().clear();
    let input = args(&["--suite-native-self-test", "--hold-ms", "0"]);
    assert_eq!(diagnostics::dispatch(&input, 9, api()), Some(0));
    assert_eq!(*EVENTS.lock().unwrap(), ["init", "version", "deinit"]);
}

#[test]
fn invalid_utf8_version_still_deinitializes() {
    let _guard = guard();
    EVENTS.lock().unwrap().clear();
    let input = args(&["--suite-native-self-test"]);
    let bad = diagnostics::Api {
        init,
        deinit,
        version_string: bad_utf8_version,
    };
    assert_eq!(diagnostics::dispatch(&input, 9, bad), Some(1));
    assert_eq!(*EVENTS.lock().unwrap(), ["init", "deinit"]);
}

#[test]
fn null_version_still_deinitializes() {
    let _guard = guard();
    EVENTS.lock().unwrap().clear();
    let input = args(&["--suite-native-self-test"]);
    let null = diagnostics::Api {
        init,
        deinit,
        version_string: null_version,
    };
    assert_eq!(diagnostics::dispatch(&input, 9, null), Some(1));
    assert_eq!(*EVENTS.lock().unwrap(), ["init", "deinit"]);
}
