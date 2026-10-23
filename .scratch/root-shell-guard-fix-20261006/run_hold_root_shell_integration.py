from __future__ import annotations
import importlib.util, json, struct, tempfile, shlex
from pathlib import Path
from types import SimpleNamespace

ROOT=Path(__file__).resolve().parents[2]
test_path=ROOT/'tests/MountSpeedRootShellIsolationRegression.py'
spec=importlib.util.spec_from_file_location('isolation_fixture',test_path)
iso=importlib.util.module_from_spec(spec); spec.loader.exec_module(iso)
client=iso.client
probe_spec=importlib.util.spec_from_file_location('mount_probe_integration',ROOT/'tools/mount-reader/mount_status_luajit_probe.py')
probe=importlib.util.module_from_spec(probe_spec); probe_spec.loader.exec_module(probe)
identity={'pid':424242,'startTicks':314159,'exe':'/system/bin/app_process64'}
player={'playerId':7,'isLocalPlayer':True,'isMounted':True,'mountId':42,'references':{'m_RoleMoveSpeed':{'nodeValueAddress':'0x2'}}}
snapshot={'status':'ok','candidateCount':1,'process':identity,'player':player}
baseline=struct.pack('<d',296.7); desired=struct.pack('<d',5000.0)

class RaceBroker(iso.Broker):
  def __init__(self,shell,mem):
    super().__init__(shell); self.mem=mem; self.injected=False
  def execute(self,command,timeout):
    # Cause a real compare-and-swap miss after run_hold has validated binding,
    # immediately before its first actual write command enters the long shell.
    if not self.injected and ' of=' in command:
      self.injected=True
      data=bytearray(self.mem.read_bytes()); data[2:10]=desired; self.mem.write_bytes(data)
    return super().execute(command,timeout)

with tempfile.TemporaryDirectory(prefix="run hold ' root fixture ") as td:
  root=Path(td); mem=root/'fixture memory.bin'; mem.write_bytes(b'xx'+baseline)
  pid=424242; shell=iso.PersistentShell(iso.find_bash(),pid,314159,mem); iso.write_stat(shell.stat_path,pid,314159)
  journal=root/'restore journal.json'; log=root/'hold log.jsonl'
  broker=RaceBroker(shell,mem)
  transport=client.AdbTransport('unused','fixture',pid,root_broker=broker,dd_command='dd')
  args=SimpleNamespace(action='hold',write=True,pid=pid,mount_id=42,speed=5000.0,
    duration=.2,interval=.1,persistent=False,journal=str(journal),adb='unused',serial='fixture',
    run_as_package='',root_broker_pipe='',preflight_player_id=7,preflight_start_ticks=314159,
    preflight_exe=identity['exe'],preflight_speed_address=2,log_path='',_hold_logger=client.HoldLogger(str(log),'offline','fixture','fixture-guid'))
  provider_calls=[]
  def snapshot_provider(): provider_calls.append(1); return snapshot
  error=None
  try:
    client.run_hold(args,transport,snapshot_provider,probe,'')
  except Exception as exc:
    error=str(exc)
  events=[json.loads(line) for line in log.read_text(encoding='utf-8').splitlines()]
  run_end=[e for e in events if e['event']=='runEnd'][-1]
  reads=[(e['data'].get('phase'),e['data'].get('status')) for e in events if e['event']=='speedRead']
  writes=[(e['data'].get('phase'),e['data'].get('status')) for e in events if e['event']=='speedWrite']
  print(f'run_hold_exception={error!r}')
  print(f'provider_calls={len(provider_calls)}; write_commands={len([c for c in broker.commands if " of=" in c])}; broker_statuses={[broker.status]}')
  print(f'fixture_final={mem.read_bytes()[2:10]!r}; expected_baseline={baseline!r}')
  print(f'runEnd_outcome={run_end["data"].get("outcome")}; runEnd_journal={run_end["data"].get("journal")}; journal_exists={journal.exists()}; parent_shell_alive={shell.proc.poll() is None}')
  print(f'reads={reads}; writes={writes}')
  # Recovered failures intentionally propagate to caller, but the active hold
  # must record verified recovery and remove its journal without killing shell.
  assert error and 'recovery restored and verified' in error, error
  assert run_end['data']['outcome']=='failure-recovered'
  assert run_end['data']['journal']=='deleted' and not journal.exists()
  assert mem.read_bytes()[2:10]==baseline
  assert shell.proc.poll() is None
  assert len([c for c in broker.commands if ' of=' in c])==2
  shell.close()
print('GREEN_INTEGRATION_CONFIRMED: CAS54 -> actual target read -> CAS restore -> baseline verified; journal deleted; parent shell alive')

