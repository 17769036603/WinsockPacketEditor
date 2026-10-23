$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$script:passed = 0
$script:failed = @()
function Assert-True([bool]$Condition, [string]$Name) { if ($Condition) { $script:passed++ } else { $script:failed += $Name } }
$jinchan = [IO.File]::ReadAllText((Join-Path $repo 'mobile-suite\app\src\main\java\com\xnas\wpe\jinchan\MainActivity.java'), [Text.Encoding]::UTF8)
Assert-True ($jinchan.Contains('new AlertDialog.Builder(dialogContext())')) 'Jinchan dialogs use dialogContext'
Assert-True ($jinchan.Contains('Activity parent = getParent();') -and $jinchan.Contains('return parent == null ? this : parent;')) 'Jinchan dialogContext selects LocalActivityManager parent when present'
$anlei = [IO.File]::ReadAllText((Join-Path $repo 'mobile-suite\native\anlei\worker-rust\src\main.rs'), [Text.Encoding]::UTF8)
$guardStart = $anlei.IndexOf('// These files were staged by this exact run_id', [StringComparison]::Ordinal)
$guardEnd = $anlei.IndexOf('active_paths = None;', $guardStart, [StringComparison]::Ordinal)
$guard = $anlei.Substring($guardStart, $guardEnd - $guardStart)
Assert-True ($guardStart -ge 0 -and $guard.Contains('run_id') -and $guard.Contains('remove_file(path)')) 'Anlei pre-attach identity mismatch cleanup is scoped to current run inputs'
Assert-True ($guard.Contains('&*tick') -and $guard.Contains('&*result') -and $guard.Contains('&*binding')) 'Anlei removes the current run path tuple'
Assert-True ($guard.Contains('cleanup.lua') -and $guard.Contains('lease_mirror') -and $guard.Contains('worker_beat')) 'Anlei mismatch cleanup includes current-run sidecar files'
$panel = [IO.File]::ReadAllText((Join-Path $repo 'mobile-suite\native\battlepanel\rust-reader\src\battle.rs'), [Text.Encoding]::UTF8)
$panelFailClosed = $panel.Contains('Err(RoleReadError::IdentityMismatch) => return Err(identity_mismatch_error(position))')
$panelEvidence = $panel.Contains('any_role_identity_mismatch_marks_the_snapshot_unconfirmed') -and $panel.Contains('snapshot is unconfirmed')
Assert-True ($panelFailClosed -and $panelEvidence) 'Battle panel identity mismatch marks the snapshot unconfirmed and fails closed'
$total = $script:passed + $script:failed.Count
if ($script:failed.Count -eq 0) { Write-Output "ReviewFollowups: PASS ($total assertions)" } else { Write-Output "ReviewFollowups: FAIL ($($script:passed)/$total assertions passed)"; $script:failed | ForEach-Object { Write-Output "FAIL: $_" }; exit 1 }
