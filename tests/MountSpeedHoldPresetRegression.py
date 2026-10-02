from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
cache = (ROOT / "WPELibrary/Lib/Socket_Cache.cs").read_text(encoding="utf-8")
form = (ROOT / "WPELibrary/Socket_RobotForm.cs").read_text(encoding="utf-8")
robot = (ROOT / "WPELibrary/Lib/Socket_Robot.cs").read_text(encoding="utf-8")

assert "MountSpeedHold = 16" in cache
assert 'MountSpeedHoldPresetName = "坐骑速度保持"' in cache
assert 'row["Content"] = "500";' in cache
assert "double.IsNaN(target)" in cache and "double.IsInfinity(target)" in cache
assert "CultureInfo.InvariantCulture" in cache
assert "listMountSpeedHold.Count != 1 || dtRInstruction.Rows.Count != 1" in cache
assert "EnsureBuiltInMountSpeedHoldPreset()" in cache
assert 'parameters["MountStatusAndroidSnapshotReader"]' in form
assert "InstructionType.MountSpeedHold" in robot
assert 'gamePackage = "com.gdoo.dhxy"' in robot

print("MountSpeedHoldPresetRegression: PASS")
