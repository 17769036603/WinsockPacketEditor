"""Compile and test the production Rust parser/guard extracted from main.rs."""
import subprocess
import sys
import tempfile
import time
from pathlib import Path

sys.dont_write_bytecode = True
from compiler_paths import rustc_tool

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "native" / "battleloop" / "worker-rust" / "src" / "main.rs"


def extract(source: str, start: str, end: str) -> str:
    first = source.index(start)
    last = source.index(end, first)
    return source[first:last]


def main():
    RUSTC = rustc_tool()
    source = SOURCE.read_text(encoding="utf-8")
    parser = extract(
        source,
        "#[derive(Clone, Copy, Debug, PartialEq, Eq)]\nstruct BattleOptions",
        "\nfn read_battle_options",
    )
    reader = extract(source, "fn read_battle_options", "\n/// A no-hook terminal result")
    guard = extract(source, "fn may_publish_no_hook_terminal", "\nfn status_with_modes")
    status_helpers = extract(source, "fn parse_result_field<'a>", "\nfn cleanup_confirmed")
    cleanup = extract(source, "fn cleanup_confirmed", "\nfn terminal_loop_status")
    terminal = extract(source, "fn terminal_loop_status", "\nfn publish_loop_status")
    publish = extract(source, "fn publish_loop_status", "\nfn no_hook_terminal_status")
    incomplete = extract(source, "fn incomplete_cleanup_status", "\nfn self_test")
    tests = r'''
#[test]
fn accepts_all_four_explicit_combinations() {
    for (skip, hide) in [(false,false),(false,true),(true,false),(true,true)] {
        let input = format!("version=1\nskipBattle={}\nhideBattle={}\n", if skip {1} else {0}, if hide {1} else {0});
        assert_eq!(parse_battle_options(&input), Ok(BattleOptions { skip_battle: skip, hide_battle: hide }));
    }
}
#[test]
fn malformed_new_config_fails_closed() {
    for input in [
        "version=1\nhideBattle=0\n",
        "version=1\nskipBattle=1\n",
        "version=1\nskipBattle=maybe\nhideBattle=0\n",
        "version=1\nskipBattle=1\nhideBattle=true\n",
        "version=1\nskipBattle=0\nhideBattle=0\nother=1\n",
        "version=1\nskipBattle=0\nskipBattle=1\nhideBattle=0\n",
        "version=1\nskipBattle=0\nhideBattle=0\nhideBattle=1\n",
        "version=2\nskipBattle=1\nhideBattle=1\n",
        "skipBattle=1\nhideBattle=1\n",
        "version=1\nskipBattle=0\nhideBattle=0\ninvalid\n",
    ] {
        assert!(parse_battle_options(input).is_err(), "accepted malformed input: {input:?}");
    }
}
#[test]
fn legacy_fallback_is_only_used_when_new_file_is_absent() {
    use std::fs;
    let dir = std::env::temp_dir().join(format!("battle-options-test-{}", std::process::id()));
    let _ = fs::remove_dir_all(&dir);
    fs::create_dir_all(&dir).unwrap();
    assert_eq!(read_battle_options(&dir).unwrap(), BattleOptions { skip_battle: true, hide_battle: false });
    fs::write(dir.join("hide-battle"), "1\n").unwrap();
    assert_eq!(read_battle_options(&dir).unwrap(), BattleOptions { skip_battle: true, hide_battle: true });
    fs::write(dir.join("battle-options"), "version=broken\n").unwrap();
    assert!(read_battle_options(&dir).is_err(), "malformed new config incorrectly fell back to legacy hide-battle");
    fs::remove_dir_all(&dir).unwrap();
}
#[test]
fn no_hook_terminal_requires_fresh_empty_state() {
    assert!(may_publish_no_hook_terminal(None, false));
    assert!(!may_publish_no_hook_terminal(Some("unknown"), false));
    assert!(!may_publish_no_hook_terminal(Some("stopped"), false));
    assert!(!may_publish_no_hook_terminal(None, true));
    assert!(!may_publish_no_hook_terminal(Some("running"), true));
}
#[test]
fn cleanup_requirements_follow_independent_modes() {
    let base = "status=stopped\nhooksRemoved=true\nbindingCleared=true\n";
    assert!(cleanup_confirmed(base, BattleOptions { skip_battle:false, hide_battle:false }));
    assert!(!cleanup_confirmed(base, BattleOptions { skip_battle:true, hide_battle:false }));
    assert!(cleanup_confirmed(&(base.to_owned()+"restoreConfirmed=true\n"), BattleOptions { skip_battle:true, hide_battle:false }));
    let hide_missing = base.to_owned()+"restoreConfirmed=true\nhideBattleRequested=true\n";
    assert!(!cleanup_confirmed(&hide_missing, BattleOptions { skip_battle:false, hide_battle:true }));
    let hide_restored = hide_missing+"visibilityRestoreConfirmed=true\n";
    assert!(cleanup_confirmed(&hide_restored, BattleOptions { skip_battle:false, hide_battle:true }));
    assert!(!cleanup_confirmed("status=stopped\nhooksRemoved=true\n", BattleOptions { skip_battle:false, hide_battle:false }));
}
#[test]
fn all_status_formatters_fallback_to_options_and_preserve_snapshot_values() {
    let options = BattleOptions { skip_battle:false, hide_battle:true };
    let absent = "status=stopped\n";
    assert_eq!(parse_result_field(&terminal_loop_status(absent, 2, options), "skipBattleRequested="), Some("false"));
    assert_eq!(parse_result_field(&incomplete_cleanup_status(absent, 2, options, "test"), "skipBattleRequested="), Some("false"));
    PUBLISHED.with(|value| *value.borrow_mut() = None);
    publish_loop_status(Path::new("."), absent, 2, options);
    let published = PUBLISHED.with(|value| value.borrow().clone().unwrap());
    assert_eq!(parse_result_field(&published, "skipBattleRequested="), Some("false"));

    let existing = "status=stopped\nskipBattleRequested=true\n";
    assert_eq!(parse_result_field(&terminal_loop_status(existing, 2, options), "skipBattleRequested="), Some("true"));
    assert_eq!(parse_result_field(&incomplete_cleanup_status(existing, 2, options, "test"), "skipBattleRequested="), Some("true"));
    PUBLISHED.with(|value| *value.borrow_mut() = None);
    publish_loop_status(Path::new("."), existing, 2, options);
    let published = PUBLISHED.with(|value| value.borrow().clone().unwrap());
    assert_eq!(parse_result_field(&published, "skipBattleRequested="), Some("true"));
}
'''
    with tempfile.TemporaryDirectory(prefix="battle-modes-rust-") as directory:
        test_rs = Path(directory) / "production_options_test.rs"
        binary = Path(directory) / "production_options_test.exe"
        imports = "use std::{cell::RefCell, fs, io::ErrorKind, path::Path};\nthread_local! { static PUBLISHED: RefCell<Option<String>> = const { RefCell::new(None) }; }\nfn status(_: &Path, contents: &str) { PUBLISHED.with(|value| *value.borrow_mut() = Some(contents.to_owned())); }\n"
        test_rs.write_text(imports + parser + "\n" + reader + "\n" + guard + "\n" + status_helpers + "\n" + cleanup + "\n" + terminal + "\n" + publish + "\n" + incomplete + "\n" + tests, encoding="utf-8")
        subprocess.run([str(RUSTC), "--edition=2021", "--test", str(test_rs), "-o", str(binary)], check=True)
        subprocess.run([str(binary), "--nocapture"], check=True)
        for output in (binary, binary.with_suffix(".pdb")):
            for _ in range(20):
                try:
                    output.unlink(missing_ok=True)
                    break
                except PermissionError:
                    time.sleep(0.1)


if __name__ == "__main__":
    main()
