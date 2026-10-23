"""Exercise production root-broker writes inside a persistent POSIX shell."""
from __future__ import annotations

import base64
import importlib.util
import os
import queue
import re
import shlex
import shutil
import subprocess
import tempfile
import threading
import time
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools" / "mount-reader" / "mount_speed_memory_test.py"
MODULE_PATH = Path(os.environ.get("MOUNT_SPEED_ROOT_SHELL_MODULE", str(SCRIPT)))
SPEC = importlib.util.spec_from_file_location("mount_speed_memory_test_root_shell_test", MODULE_PATH)
client = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(client)


def find_bash() -> str:
    if os.name == "nt":
        candidate = Path(r"C:\Program Files\Git\bin\bash.exe")
        if candidate.is_file():
            return str(candidate)
    found = shutil.which("bash")
    if found:
        return found
    if os.name == "nt":
        candidate = Path(r"C:\Program Files\Git\bin\bash.exe")
        if candidate.is_file():
            return str(candidate)
    raise unittest.SkipTest("a real Bash executable is required for the root-shell regression")


def bash_path(bash: str, path: Path) -> str:
    if os.name != "nt":
        return path.as_posix()
    result = subprocess.run([bash, "-lc", "cygpath -u \"$1\"", "test", str(path)],
                            capture_output=True, text=True, check=True)
    return result.stdout.strip()


class PersistentShell:
    def __init__(self, bash: str, pid: int, ticks: int, mem: Path):
        self.pid, self.ticks, self.mem = pid, ticks, mem
        self.stat_path = mem.with_name("fixture process 'stat file")
        self.proc = subprocess.Popen([bash, "--noprofile", "--norc"], stdin=subprocess.PIPE,
                                     stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                     text=True, encoding="utf-8", errors="replace", bufsize=1)
        self.lines: queue.Queue[str] = queue.Queue()
        self.last_output: list[str] = []
        self.reader = threading.Thread(target=self._read, daemon=True)
        self.reader.start()

    def _read(self):
        assert self.proc.stdout is not None
        for line in self.proc.stdout:
            self.lines.put(line)

    def execute(self, command: str, timeout: float = 3.0) -> tuple[int, bool, list[str]]:
        # Translate only fixture process paths. /proc/$$/fd/3 must stay real so
        # the test verifies the child shell's own fd binding.
        wrapped = command.startswith("sh -c ")
        if wrapped:
            command = shlex.split(command)[2]
        mem = bash_path(find_bash(), self.mem)
        command = command.replace(f"/proc/{self.pid}/mem", shlex.quote(mem))
        stat_path = bash_path(find_bash(), self.stat_path)
        if "/proc/424242/stat" in command:
            command = command.replace("/proc/424242/stat", shlex.quote(stat_path))
        if wrapped:
            command = f"sh -c {shlex.quote(command)}"
        marker = "WPE_TEST_END_" + str(time.time_ns())
        assert self.proc.stdin is not None
        self.proc.stdin.write("set +e\n" + command.rstrip() + "\nwpe_status=$?\nprintf '" +
                              marker + ":%s\\n' \"$wpe_status\"\n")
        self.proc.stdin.flush()
        deadline = time.monotonic() + timeout
        seen: list[str] = []
        while time.monotonic() < deadline:
            if self.proc.poll() is not None:
                break
            try:
                line = self.lines.get(timeout=0.03)
            except queue.Empty:
                continue
            seen.append(line.rstrip("\r\n"))
            if marker + ":" in line:
                prefix, status = line.split(marker + ":", 1)
                if prefix:
                    seen[-1] = prefix
                    seen.append(marker + ":" + status.rstrip("\r\n"))
                return int(status.split()[0]), self.proc.poll() is None, seen
        return -1, self.proc.poll() is None, seen

    def close(self):
        if self.proc.poll() is None:
            self.proc.terminate()
        try:
            self.proc.wait(timeout=2)
        except subprocess.TimeoutExpired:
            self.proc.kill()
            self.proc.wait(timeout=2)


class Broker:
    def __init__(self, shell: PersistentShell):
        self.shell = shell
        self.commands: list[str] = []
        self.status: int | None = None
        self.pending_guard: int | None = None

    def execute(self, command: str, _timeout: float) -> bytes:
        self.commands.append(command)
        if self.pending_guard is not None and " of=" in command:
            guard = self.pending_guard
            self.pending_guard = None
            if guard == 51:
                command = command.replace(f"/proc/{self.shell.pid}/mem", "/missing/fixture/mem")
            elif guard == 52:
                command = command.replace(f"/proc/{self.shell.pid}/stat", "/missing/fixture/stat")
        else:
            guard = None
        self.status = None
        if guard == 53:
            self.shell.ticks = 1
            write_stat(self.shell.stat_path, self.shell.pid, self.shell.ticks)
        elif guard == 54:
            self.shell.ticks = 314159
            write_stat(self.shell.stat_path, self.shell.pid, self.shell.ticks)
        code, alive, output = self.shell.execute(command)
        self.status = code
        self.shell.last_output = output
        if not alive:
            raise RuntimeError(f"root shell exited with {self.shell.proc.returncode}; output={output!r}")
        if code:
            raise RuntimeError(f"root shell command failed with exit {code}; output={output!r}")
        if " if=" in command and output:
            data_lines = [line for line in output if not line.startswith("WPE_TEST_END_")]
            return (data_lines[-1] if data_lines else "").encode("ascii")
        return b""


def write_stat(path: Path, pid: int, ticks: int):
    fields = " ".join(str(value) for value in range(1, 19))
    path.write_text(f"{pid} (fixture process) S {fields} {ticks}\n", encoding="ascii")


class RootShellIsolationRegression(unittest.TestCase):
    def test_guarded_write_isolated_and_errors_preserved(self):
        bash = find_bash()
        pid = 424242
        address = 2
        with tempfile.TemporaryDirectory(prefix="root shell ' fixture ") as directory:
            fixture = Path(directory) / "fixture memory.bin"
            ticks = 314159
            shell = PersistentShell(bash, pid, ticks, fixture)
            try:
                broker = Broker(shell)
                transport = client.AdbTransport("unused", "fixture", pid, root_broker=broker, dd_command="dd")

                for code, expected in ((51, b"\0" * 8), (52, b"\0" * 8),
                                       (53, b"\0" * 8), (54, b"ABCDEFGH")):
                    fixture.write_bytes(b"xx" + expected)
                    write_stat(shell.stat_path, pid, ticks)
                    broker.pending_guard = code
                    expected_raw = b"ZZZZZZZZ" if code == 54 else expected
                    with self.assertRaises(RuntimeError):
                        transport.write(address, b"12345678", ticks, expected_raw=expected_raw)
                    # Special-builtin redirection failures can return shell-specific errors.
                    self.assertIn(broker.status, (1, 2, 51) if code == 51 else (code,), shell.last_output)
                    self.assertIsNone(shell.proc.poll(), "the persistent parent shell must remain alive")
                    self.assertEqual(fixture.read_bytes(), b"xx" + expected, "failed guards must perform zero writes")
                    self.assertEqual(transport.read(address, 8), expected)

                fixture.write_bytes(b"xxABCDEFGH")
                write_stat(shell.stat_path, pid, ticks)
                transport.write(address, b"12345678", ticks, expected_raw=b"ABCDEFGH")
                self.assertEqual(fixture.read_bytes(), b"xx12345678")
                self.assertEqual(transport.read(address, 8), b"12345678")
                self.assertIsNone(shell.proc.poll())
            finally:
                shell.close()

    def test_non_broker_write_command_is_not_wrapped(self):
        transport = client.AdbTransport("adb", "serial", 25)
        calls = []
        transport.shell = calls.append
        try:
            transport.write(1, b"12345678", 99)
        except Exception:
            pass
        self.assertEqual(len(calls), 1)
        self.assertFalse(calls[0].startswith("sh -c "))


if __name__ == "__main__":
    unittest.main()
