'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const test = require('node:test');

const moduleRoot = path.resolve(__dirname, '..');
const rustPath = process.env.STARHUNT_RUST_SOURCE
  || path.join(moduleRoot, 'native', 'starhunt', 'worker-rust', 'src', 'main.rs');
const rustSource = fs.readFileSync(rustPath, 'utf8').replace(/\r\n/g, '\n');
const moduleStart = rustSource.indexOf('mod native_drain_gate {');
assert.notEqual(moduleStart, -1, `could not find production native drain gate in ${rustPath}`);
const moduleEndMarker = '\nuse native_drain_gate::';
const moduleEnd = rustSource.indexOf(moduleEndMarker, moduleStart);
assert.notEqual(moduleEnd, -1, `could not find production native drain gate end in ${rustPath}`);
const productionGate = rustSource.slice(moduleStart, moduleEnd + 1);

const rustTests = String.raw`

#[cfg(test)]
mod extracted_production_gate_tests {
    use super::native_drain_gate::{
        detach_decision, draining_status, drained_run_id, preserve_drain_prompt,
        result_confirms_stopped_for_run, scope_result_to_run, DetachDecision,
    };

    const LUA_STOPPED: &str = "state=stopped\nstatus=stopped\nacceptedSequence=7\nhooksRemoved=true\nbindingCleared=true\n";

    #[test]
    fn lua_stopped_and_cleanup_flags_alone_do_not_release_the_worker() {
        let scoped = scope_result_to_run(LUA_STOPPED, "123").unwrap();
        assert_eq!(
            detach_decision(false, &scoped, None, "123"),
            DetachDecision::Wait
        );
    }

    #[test]
    fn pending_cleanup_status_stays_stopping_until_the_run_ack_arrives() {
        let scoped = scope_result_to_run(LUA_STOPPED, "123").unwrap();
        let public_status = draining_status(&scoped).unwrap();
        assert_eq!(field(&public_status, "state"), Some("stopping"));
        assert_eq!(field(&public_status, "status"), Some("stopping"));
        assert_eq!(field(&public_status, "agentDrainConfirmed"), Some("false"));
        assert_eq!(field(&public_status, "hooksRemoved"), Some("true"));
        assert_eq!(field(&public_status, "bindingCleared"), Some("true"));
        assert!(!result_confirms_stopped_for_run(&public_status, "123"));
    }

    #[test]
    fn drain_prompts_survive_rebuilding_public_stopping_status() {
        let scoped = scope_result_to_run(LUA_STOPPED, "123").unwrap();
        for prompt in [
            "请返回游戏以完成停止",
            "请返回游戏以完成停止（worker仍保留，cleanup尚未确认）",
        ] {
            let public_status = draining_status(&scoped).unwrap();
            let prompted = preserve_drain_prompt(&public_status, prompt);
            assert_eq!(field(&prompted, "reason"), Some(prompt));
            assert_eq!(field(&prompted, "status"), Some("stopping"));
            assert_eq!(field(&prompted, "agentDrainConfirmed"), Some("false"));

            let next_cycle = preserve_drain_prompt(&draining_status(&scoped).unwrap(), prompt);
            assert_eq!(field(&next_cycle, "reason"), Some(prompt));
        }
    }

    #[test]
    fn only_an_exact_drained_message_for_the_current_run_opens_the_gate() {
        let scoped = scope_result_to_run(LUA_STOPPED, "123").unwrap();
        let ack = r#"{"type":"send","payload":{"event":"drained","runId":"123","cleanupDone":true}}"#;
        let old_ack = r#"{"type":"send","payload":{"event":"drained","runId":"122","cleanupDone":true}}"#;
        assert_eq!(drained_run_id(ack).as_deref(), Some("123"));
        assert_eq!(drained_run_id(old_ack).as_deref(), Some("122"));
        assert_eq!(
            detach_decision(false, &scoped, Some("122"), "123"),
            DetachDecision::Wait
        );
        assert_eq!(
            detach_decision(false, &scoped, Some("123"), "123"),
            DetachDecision::CleanupConfirmed
        );
    }

    #[test]
    fn malformed_or_unrelated_json_cannot_masquerade_as_a_drain_ack() {
        let decoys = [
            r#"{"type":"error","description":"payload says event drained runId 123"}"#,
            r#"{"type":"send","payload":{"event":"status","runId":"123","cleanupDone":true}}"#,
            r#"{"type":"send","payload":{"event":"drained","runId":"123","cleanupDone":false}}"#,
            r#"{"type":"send","payload":{"event":"drained","runId":"123x","cleanupDone":true}}"#,
            r#"{"type":"send","payload":{"event":"drained","event":"fatal","runId":"123","cleanupDone":true}}"#,
            r#"{"type":"send","payload":{"event":"drained","runId":"123","cleanupDone":true} trailing}"#,
        ];
        for decoy in decoys {
            assert_eq!(drained_run_id(decoy), None, "accepted decoy: {decoy}");
        }
    }

    #[test]
    fn result_must_be_complete_stopped_and_bound_to_the_current_run() {
        let scoped = scope_result_to_run(LUA_STOPPED, "123").unwrap();
        assert!(result_confirms_stopped_for_run(&scoped, "123"));
        assert!(!result_confirms_stopped_for_run(&scoped, "122"));
        let changed = scoped.replace("hooksRemoved=true", "hooksRemoved=false");
        assert!(!result_confirms_stopped_for_run(&changed, "123"));
        let missing = "status=stopped\nacceptedSequence=7\nhooksRemoved=true\nbindingCleared=true\n";
        assert!(!result_confirms_stopped_for_run(missing, "123"));
    }

    #[test]
    fn original_process_end_has_its_own_terminal_path() {
        assert_eq!(
            detach_decision(true, "", None, "123"),
            DetachDecision::ProcessEnded
        );
        assert_eq!(
            detach_decision(false, "", None, "123"),
            DetachDecision::Wait
        );
    }

    fn field<'a>(contents: &'a str, key: &str) -> Option<&'a str> {
        contents.lines().find_map(|line| line.strip_prefix(&format!("{key}=")))
    }
}
`;

function findRustc() {
  const candidates = [
    process.env.RUSTC,
    path.join(os.homedir(), '.cargo', 'bin', 'rustc.exe'),
    path.join(os.homedir(), '.rustup', 'toolchains', 'stable-x86_64-pc-windows-msvc', 'bin', 'rustc.exe'),
    path.join(os.homedir(), '.rustup', 'toolchains', 'stable-x86_64-pc-windows-gnu', 'bin', 'rustc.exe')
  ].filter(Boolean);
  for (const candidate of candidates) if (fs.existsSync(candidate)) return candidate;
  const lookup = spawnSync(process.platform === 'win32' ? 'where.exe' : 'which', ['rustc'], { encoding: 'utf8' });
  if (lookup.status === 0) return lookup.stdout.trim().split(/\r?\n/)[0];
  return null;
}

test('production Rust drain gate rejects early, stale, malformed, and terminally distinct cases', t => {
  const rustc = findRustc();
  assert.ok(rustc, 'rustc is required to compile the extracted production gate');
  const tempStem = path.join(os.tmpdir(), `starhunt-drain-gate-${process.pid}-${Date.now()}`);
  const harnessPath = `${tempStem}.rs`;
  const executablePath = process.platform === 'win32' ? `${tempStem}.exe` : tempStem;
  t.after(() => {
    for (const file of [harnessPath, executablePath, `${tempStem}.pdb`]) {
      try { fs.unlinkSync(file); } catch (error) { if (error.code !== 'ENOENT') throw error; }
    }
  });

  fs.writeFileSync(harnessPath, productionGate + rustTests, 'utf8');
  const compile = spawnSync(rustc, ['--edition=2021', '--test', harnessPath, '-o', executablePath], { encoding: 'utf8' });
  assert.equal(compile.status, 0, `${compile.stdout}\n${compile.stderr}`);
  const run = spawnSync(executablePath, ['--nocapture'], { encoding: 'utf8' });
  assert.equal(run.status, 0, `${run.stdout}\n${run.stderr}`);
  assert.match(run.stdout, /7 passed/);
});
