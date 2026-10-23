const ANLEI: &str = include_str!("../../../native/anlei/worker-rust/src/main.rs");
const BATTLE_LOOP: &str = include_str!("../../../native/battleloop/worker-rust/src/main.rs");
const BATTLE_SKIP: &str = include_str!("../../../native/battleskip/worker-rust/src/main.rs");
const LAUNCHER: &str = include_str!("../../../native/battleskip/launcher-rust/src/lib.rs");
const MOUNT_HOLD: &str = include_str!("../../../native/mounthold/rust-reader/src/main.rs");

fn main_body(source: &str) -> &str {
    source
        .rsplit_once("fn main()")
        .map(|(_, body)| body)
        .or_else(|| {
            source
                .rsplit_once("fn main() -> ExitCode")
                .map(|(_, body)| body)
        })
        .expect("native entrypoint")
}

#[test]
fn worker_stop_and_reconcile_commands_remain_available_without_lease_checks() {
    for source in [ANLEI, BATTLE_LOOP, BATTLE_SKIP] {
        let body = main_body(source);
        assert!(body.contains("Some(\"--stop\")"));
        assert!(!body.contains("require_module_lease"));
    }
    for source in [BATTLE_LOOP, BATTLE_SKIP] {
        assert!(main_body(source).contains("Some(\"--reconcile\")"));
    }
}

#[test]
fn launcher_checks_only_the_start_alias_before_root_dispatch() {
    let start = LAUNCHER
        .find("com.xnas.wpe.suite.BattleSkipStart")
        .expect("start alias");
    let stop = LAUNCHER
        .find("com.xnas.wpe.suite.BattleSkipStop")
        .expect("stop alias");
    let start_section = &LAUNCHER[start..stop];
    let stop_section = &LAUNCHER[stop..];
    assert!(start_section.contains("require_suite_lease"));
    assert!(!stop_section.contains("require_suite_lease"));
}

#[test]
fn mount_hold_recovery_only_bypasses_lease_without_starting_new_work() {
    let recovery = MOUNT_HOLD
        .find("} else if recover_only {")
        .expect("empty recovery-only branch");
    let start_lease = MOUNT_HOLD
        .find("license_lease::require_module_lease(&dir)")
        .expect("normal start lease gate");
    assert!(recovery < start_lease);
    assert!(MOUNT_HOLD[recovery..start_lease].contains("stopped_without_changes"));
    assert!(MOUNT_HOLD.contains("--recover-only must be true or false"));
    assert!(MOUNT_HOLD.contains("if recover_only {"));
}

#[test]
fn attached_workers_require_cleanup_evidence_before_clean_stop() {
    assert!(BATTLE_LOOP.contains("attach_attempted = true;\n        let session"));
    assert!(BATTLE_LOOP.contains("restoreConfirmed=") && BATTLE_LOOP.contains("attach_attempted = false"));
    assert!(BATTLE_SKIP.contains("attach_attempted = true;\n        let session"));
    assert!(BATTLE_SKIP.contains("if cleanup_confirmed {\n            attach_attempted = false;"));
    assert!(BATTLE_SKIP.contains("reason=cleanup_unconfirmed"));
}

#[test]
fn battleskip_last_lease_gate_releases_resources_and_fails_closed() {
    let gate = BATTLE_SKIP
        .find("if let Err(lease_error) = license_lease::require_module_lease(&dir)")
        .expect("last pre-attach lease gate");
    let end = BATTLE_SKIP[gate..]
        .find("if before_attach.len() != 1")
        .map(|offset| gate + offset)
        .expect("identity-change path after lease gate");
    let branch = &BATTLE_SKIP[gate..end];
    assert!(branch.contains("frida_unref(dev.cast())"));
    assert!(branch.contains("frida_device_manager_close_sync(manager"));
    assert!(branch.contains("frida_unref(manager.cast())"));
    assert!(branch.contains("remove_file(path)"));
    assert!(branch.contains("work_path"));
    assert!(branch.contains("remove_file(work)"));
    assert!(branch.contains("if attach_attempted"));
    assert!(branch.contains("state=stopping"));
    assert!(branch.contains("state=stopped\\nhooksRemoved=true\\nbindingCleared=true"));
}
