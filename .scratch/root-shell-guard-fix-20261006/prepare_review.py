"""Create a local review-only Git fixture for this task's exact changes."""
from pathlib import Path
import subprocess

repo = Path(__file__).resolve().parents[2]
review = Path(__file__).with_name('review-fixture')
assert not review.exists(), 'do not overwrite an existing review fixture'
review.mkdir()
speed_path = Path('tools/mount-reader/mount_speed_memory_test.py')
test_path = Path('tests/MountSpeedMemoryTestRegression.py')
new_path = Path('tests/MountSpeedRootShellIsolationRegression.py')
current_speed = (repo / speed_path).read_text(encoding='utf-8')
old_speed = (repo / 'releases/2026.10.6.4' / speed_path).read_text(encoding='utf-8')
assert current_speed.replace('import shlex\n', '', 1).replace(
    '        if self.root_broker is not None:\n            command = f"sh -c {shlex.quote(command)}"\n', '', 1) == old_speed
current_test = (repo / test_path).read_text(encoding='utf-8')
unwrap = '        if command.startswith("sh -c "):\n            command = shlex.split(command)[2]\n'
assert current_test.count(unwrap) == 1 and current_test.count('import shlex\n') == 1
old_test = current_test.replace('import shlex\n', '', 1).replace(unwrap, '', 1)
for path, content in ((speed_path, old_speed), (test_path, old_test)):
    target = review / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(content, encoding='utf-8')
subprocess.run(['git', 'init', '--initial-branch=review-baseline', str(review)], check=True, capture_output=True)
def git(*args):
    subprocess.run(['git', '-C', str(review), '-c', f'safe.directory={review.as_posix()}',
                    '-c', 'user.name=Local review fixture', '-c', 'user.email=review@localhost', *args],
                   check=True, capture_output=True)
git('add', '--', str(speed_path), str(test_path))
git('commit', '-m', 'Local baseline before root guard isolation')
for path, content in ((speed_path, current_speed), (test_path, current_test),
                      (new_path, (repo / new_path).read_text(encoding='utf-8'))):
    (review / path).write_text(content, encoding='utf-8')
print(f'review_fixture={review}')
print('scope=2 tracked task-only modifications plus 1 new regression; no user Git index changes')
