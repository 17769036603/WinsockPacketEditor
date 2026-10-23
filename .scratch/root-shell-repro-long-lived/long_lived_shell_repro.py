import base64, importlib.util, json, os, queue, re, shutil, subprocess, tempfile, threading, time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / 'tools/mount-reader/mount_speed_memory_test.py'
spec = importlib.util.spec_from_file_location('mount_speed_memory_test', SCRIPT)
client = importlib.util.module_from_spec(spec); spec.loader.exec_module(client)
bash = Path(r'C:\Program Files\Git\bin\bash.exe')
assert bash.exists(), f'Git Bash missing: {bash}'

def run_case():
  with tempfile.TemporaryDirectory(prefix='root-shell-case-') as td:
    td = Path(td); mem = td/'fixture.mem'; mem.write_bytes(b'ABCDEFGH')
    fakebin = td/'bin'; fakebin.mkdir()
    pid, ticks, addr = 424242, 314159, 2
    # Fake toybox dd accesses only this test's byte file. The shell command and
    # its status marker are the same form as AndroidRootShellSession.Execute.
    dd = fakebin/'toybox'
    dd.write_text('''#!/usr/bin/env python3\nimport sys\nfrom pathlib import Path\na=sys.argv[1:]\nif a[0] != "dd": sys.exit(2)\nopts=dict(x.split("=",1) for x in a[1:] if "=" in x)\nif "if" in opts:\n d=Path(opts["if"]).read_bytes(); off=int(opts.get("skip",0)); n=int(opts.get("count",len(d))); sys.stdout.buffer.write(d[off:off+n])\nelif "of" in opts:\n d=sys.stdin.buffer.read(); p=Path(opts["of"]); b=bytearray(p.read_bytes()); off=int(opts.get("seek",0)); b[off:off+len(d)]=d; p.write_bytes(b)\nelse: sys.exit(2)\n''', encoding='utf-8'); dd.chmod(0o755)
    env=os.environ.copy(); env['PATH']=str(fakebin)+os.pathsep+env.get('PATH','')
    proc=subprocess.Popen([str(bash),'--noprofile','--norc'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,bufsize=1,env=env)
    lines=queue.Queue()
    threading.Thread(target=lambda: [lines.put(x) for x in proc.stdout],daemon=True).start()
    def execute(command, timeout=3):
      # Broker maps only the fixture process paths to the disposable local file
      # while retaining production command exit and marker control flow.
      command=command.replace(f'/proc/{pid}/mem',str(mem).replace('\\','/'))
      command=re.sub(r"stat=\$\(cat /proc/\d+/stat\)", f"stat='{pid} (fixture) S 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 {ticks}'", command)
      command=re.sub(r"if=/proc/\$\$/fd/3",f"if={str(mem).replace('\\','/')}",command)
      marker='WPE_TEST_END_'+str(time.time_ns())
      proc.stdin.write('set +e\n'+command.rstrip()+'\nwpe_status=$?\nprintf \''+marker+':%s\\n\' "$wpe_status"\n'); proc.stdin.flush()
      deadline=time.time()+timeout; seen=[]
      while time.time()<deadline:
        if proc.poll() is not None: break
        try: line=lines.get(timeout=.02)
        except queue.Empty: continue
        seen.append(line.rstrip())
        if line.startswith(marker+':'): return int(line.split(':',1)[1]),proc.poll() is None,seen
      return None,proc.poll() is None,seen
    class Broker:
      def __init__(self): self.last=None
      def execute(self, command, timeout):
        code, alive, seen = execute(command, timeout)
        self.last = code
        if code is None:
          raise RuntimeError(f'Android Root shell 已退出。 exit={proc.poll()} output={seen!r}')
        return b''
    broker=Broker(); transport=client.AdbTransport('unused','fixture',pid,root_broker=broker)
    try:
      transport.write(addr,b'12345678',ticks,expected_raw=b'ZZZZZZZZ')
    except Exception as e: print('transport_write_exception='+repr(e))
    else: print('transport_write_exception=None')
    state=proc.poll()
    print(f'bash_exit_code={state}')
    print(f'cas_status={broker.last}; bash_alive_after_write={state is None}; fixture_bytes={mem.read_bytes()!r}')
    if state is None:
      status,alive,output=execute('printf READ_ONLY_OK',2)
      print(f'followup_readonly_status={status}; followup_marker_seen={status is not None}; bash_alive_after_followup={alive}; output={output!r}')
    if state is None: proc.terminate(); proc.wait(timeout=2)
    return broker.last, state

status, state = run_case()
# Current production Execute wrapper causes `exit 54` to terminate its long-lived shell.
assert state == 54, 'expected old behavior: CAS 54 terminates shell (red regression baseline)'
print('RED_BASELINE_CONFIRMED: EOF/no status marker; bash exit code 54; shell dead')

