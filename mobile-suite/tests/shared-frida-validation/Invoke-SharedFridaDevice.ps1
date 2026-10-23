[CmdletBinding()]
param(
    [switch]$Ready,
    [string]$StaticDir = (Join-Path $PSScriptRoot '..\..\build\shared-frida-validation\static'),
    [string]$SharedDir = (Join-Path $PSScriptRoot '..\..\build\shared-frida-validation\shared'),
    [string]$Adb = 'C:\Users\Administrator\AppData\Local\Android\Sdk\platform-tools\adb.exe',
    [string]$Serial = 'emulator-5554',
    [string]$OutputJson = (Join-Path $PSScriptRoot 'device-results.json'),
    [int]$HoldMs = 30000,
    [int]$StartupTimeoutSeconds = 10
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DevicePreflight.ps1')
. (Join-Path $PSScriptRoot 'ParseSmapsRollup.ps1')
$workerNames = @('libanlei_worker.so','libbattle_loop_worker.so','libbattle_skip_worker.so','libjinchan_worker.so','libresistance_query.so')
if (-not $Ready) { throw 'Device actions are disabled. Re-run with -Ready only after the parent signals ready and confirms all build inputs.' }
if ($Serial -ne 'emulator-5554') { throw 'This validation is restricted to emulator-5554.' }
if ($HoldMs -lt 20000 -or $HoldMs -gt 60000) { throw 'HoldMs must be between 20000 and 60000.' }
if (-not (Test-Path -LiteralPath $Adb -PathType Leaf)) { throw "adb not found: $Adb" }
foreach ($group in @(@{Name='static';Path=$StaticDir},@{Name='shared';Path=$SharedDir})) {
    foreach ($name in $workerNames) {
        $p = Join-Path $group.Path $name
        if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { throw "Missing $($group.Name) worker: $p" }
    }
}
$core = Join-Path $SharedDir 'libfrida-core.so'
if (-not (Test-Path -LiteralPath $core -PathType Leaf)) { throw "Missing shared core: $core" }

function New-AdbProcess([string[]]$Arguments, [bool]$CaptureOutput = $true) {
    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Adb
    foreach ($arg in (@('-s',$Serial) + $Arguments)) { $psi.ArgumentList.Add([string]$arg) }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $CaptureOutput
    $psi.RedirectStandardError = $CaptureOutput
    if ($CaptureOutput) { $psi.StandardOutputEncoding = [Text.Encoding]::UTF8; $psi.StandardErrorEncoding = [Text.Encoding]::UTF8 }
    $p = [Diagnostics.Process]::new(); $p.StartInfo = $psi
    if (-not $p.Start()) { throw "Could not start adb: $($Arguments -join ' ')" }
    return $p
}
function Invoke-Adb([string[]]$Arguments, [int]$TimeoutSeconds = 15) {
    $p = New-AdbProcess $Arguments $true
    $stdoutTask = $p.StandardOutput.ReadToEndAsync(); $stderrTask = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) { try { $p.Kill() } catch {}; throw "adb timeout after ${TimeoutSeconds}s: $($Arguments -join ' ')" }
    $out = $stdoutTask.GetAwaiter().GetResult(); $err = $stderrTask.GetAwaiter().GetResult()
    if ($p.ExitCode -ne 0) { throw "adb failed ($($p.ExitCode)): $($Arguments -join ' ') :: $err $out" }
    return $out.Trim()
}
function Quote-Shell([string]$Value) { return "'" + $Value.Replace("'", "'\''") + "'" }
function Invoke-Shell([string]$Command, [int]$TimeoutSeconds = 8) { Invoke-Adb @('shell',$Command) $TimeoutSeconds }
function Get-NextWorkerLine($Entry, [int]$TimeoutSeconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Entry.LineTask.IsCompleted) {
            $line = $Entry.LineTask.GetAwaiter().GetResult()
            if ($null -eq $line) { return $null }
            $Entry.Lines.Add([string]$line)
            $Entry.LineTask = $Entry.Process.StandardOutput.ReadLineAsync()
            return [string]$line
        }
        Start-Sleep -Milliseconds 20
    }
    throw "Timed out waiting for worker output from $($Entry.Name); captured: $($Entry.Lines -join ' | ')"
}
function Get-Identity([int]$WorkerPid) {
    # Hex output avoids shell/PowerShell multi-layer NUL escaping.
    $raw = Invoke-Shell "od -An -v -t x1 /proc/$WorkerPid/cmdline 2>/dev/null; echo __STAT__; cat /proc/$WorkerPid/stat 2>/dev/null; true" 8
    $parts = $raw -split '__STAT__',2
    if ($parts.Count -lt 2) { return $null }
    $bytes = @([regex]::Matches($parts[0], '\b[0-9a-fA-F]{2}\b') | ForEach-Object { [Convert]::ToInt32($_.Value,16) })
    if (-not $bytes.Count) { return $null }
    $cmdline = [Text.Encoding]::ASCII.GetString([byte[]]$bytes).Replace([char]0,' ').Trim()
    $stat = ($parts[1] -split "`n" | Where-Object { $_ -match '^\s*\d+\s+\(' } | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($stat)) { return $null }
    $tail = $stat.Substring($stat.LastIndexOf(')') + 1).Trim() -split '\s+'
    if ($tail.Count -lt 20) { return $null }
    [pscustomobject]@{Cmdline=$cmdline;StartTicks=$tail[19]}
}
function Assert-Identity([int]$WorkerPid,[string]$StartTicks,[string]$Needle) {
    $id = Get-Identity $WorkerPid
    if ($null -eq $id -or $id.StartTicks -ne $StartTicks -or $id.Cmdline -notlike "*$Needle*" -or $id.Cmdline -notlike '*--suite-native-self-test*' -or $id.Cmdline -notlike "*--hold-ms $HoldMs*") {
        throw "Process identity changed for PID $WorkerPid; expected startTicks=$StartTicks worker=$Needle self-test and ${HoldMs}ms hold; actual=$($id | ConvertTo-Json -Compress)"
    }
    return $id
}
function Read-Metrics([int]$WorkerPid) {
    $rollup = Invoke-Shell "cat /proc/$WorkerPid/smaps_rollup 2>/dev/null; true" 8
    return ConvertFrom-SharedFridaSmapsRollup $rollup
}
function Stop-TrackedRemote([int]$WorkerPid,[string]$StartTicks,[string]$Needle) {
    $id = Get-Identity $WorkerPid
    if ($null -eq $id) { return $true }
    if ($id.StartTicks -ne $StartTicks -or $id.Cmdline -notlike "*$Needle*" -or $id.Cmdline -notlike '*--suite-native-self-test*') { return $false }
    Invoke-Shell "kill -TERM $WorkerPid" | Out-Null
    $deadline = [DateTime]::UtcNow.AddSeconds(2)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($null -eq (Get-Identity $WorkerPid)) { return $true }
        Start-Sleep -Milliseconds 100
    }
    $id = Get-Identity $WorkerPid
    if ($null -eq $id) { return $true }
    if ($id.StartTicks -ne $StartTicks -or $id.Cmdline -notlike "*$Needle*" -or $id.Cmdline -notlike '*--suite-native-self-test*') { return $false }
    Invoke-Shell "kill -KILL $WorkerPid" | Out-Null
    return $null -eq (Get-Identity $WorkerPid)
}

$sdk = Invoke-Shell 'getprop ro.build.version.sdk'
$abi = Invoke-Shell 'getprop ro.product.cpu.abi'
$uid = Invoke-Shell 'id -u'
$smaps = Invoke-Shell 'test -r /proc/self/smaps_rollup && echo readable'
if (-not (Test-SharedFridaDevicePreflight $uid $abi $sdk $smaps)) { throw "Device preflight failed: uid=$uid abi=$abi sdk=$sdk smaps=$smaps" }

$remoteRoot = '/data/local/tmp/piaomiao-shared-' + [Guid]::NewGuid().ToString('N')
Invoke-Shell "umask 077; mkdir -m 700 $(Quote-Shell $remoteRoot)" | Out-Null
$allSamples = [Collections.Generic.List[object]]::new()
$tracked = [Collections.Generic.List[object]]::new()
$canRemoveRoot = $true
try {
    $groups = @(
        @{Name='static';Path=$StaticDir;Remote="$remoteRoot/static"},
        @{Name='shared';Path=$SharedDir;Remote="$remoteRoot/shared"}
    )
    foreach ($group in $groups) {
        Invoke-Shell "mkdir -m 700 $(Quote-Shell $group.Remote)" | Out-Null
        foreach ($name in $workerNames) { Invoke-Adb @('push',(Join-Path $group.Path $name),"$($group.Remote)/$name") 60 | Out-Null }
        if ($group.Name -eq 'shared') { Invoke-Adb @('push',$core,"$($group.Remote)/libfrida-core.so") 60 | Out-Null }
        Invoke-Shell "chmod 700 $($group.Remote)/*.so" | Out-Null

        $entries = [Collections.Generic.List[object]]::new()
        foreach ($name in $workerNames) {
            $remoteExe = "$($group.Remote)/$name"
            $command = "export LD_LIBRARY_PATH=$(Quote-Shell $group.Remote); exec $remoteExe --suite-native-self-test --hold-ms $HoldMs"
            $p = New-AdbProcess @('shell',$command) $true
            $entry = [pscustomobject]@{Name=$name;Process=$p;Lines=[Collections.Generic.List[string]]::new();LineTask=$p.StandardOutput.ReadLineAsync();StderrTask=$p.StandardError.ReadToEndAsync();Pid=$null;StartTicks=$null;Cmdline=$null;Samples=[Collections.Generic.List[object]]::new()}
            $entries.Add($entry)
        }

        # All five are live together before any memory readings are retained.
        foreach ($entry in $entries) {
            $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
            $ready = $false
            while (-not $ready -and [DateTime]::UtcNow -lt $deadline) {
                $remaining = [Math]::Max(1,[Math]::Ceiling(($deadline-[DateTime]::UtcNow).TotalSeconds))
                $line = Get-NextWorkerLine $entry $remaining
                if ($null -eq $line) { break }
                try { $json = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
                if ($json.phase -eq 'ready') { $entry.Pid = [int]$json.pid; $ready = $true }
                elseif ($json.phase -eq 'error') { throw "Worker $($entry.Name) reported error: $line" }
            }
            if (-not $ready) { throw "Worker $($entry.Name) did not report JSON ready. Output: $($entry.Lines -join ' | ')" }
            $identity = Get-Identity $entry.Pid
            if ($null -eq $identity -or $identity.Cmdline -notlike "*$($entry.Name)*" -or $identity.Cmdline -notlike '*--suite-native-self-test*' -or $identity.Cmdline -notlike "*--hold-ms $HoldMs*") { throw "Worker JSON PID failed cmdline binding: name=$($entry.Name) pid=$($entry.Pid) identity=$($identity | ConvertTo-Json -Compress)" }
            $entry.StartTicks = $identity.StartTicks; $entry.Cmdline = $identity.Cmdline
            $tracked.Add([pscustomobject]@{Pid=$entry.Pid;StartTicks=$entry.StartTicks;Needle=$entry.Name})
            Write-Output "READY $($group.Name)/$($entry.Name) pid=$($entry.Pid) startTicks=$($entry.StartTicks)"
        }

        foreach ($entry in $entries) { $null = Assert-Identity $entry.Pid $entry.StartTicks $entry.Name; $null = Read-Metrics $entry.Pid }
        foreach ($entry in $entries) { $null = Assert-Identity $entry.Pid $entry.StartTicks $entry.Name; $entry.Samples.Add((Read-Metrics $entry.Pid)) }
        Start-Sleep -Seconds 1
        foreach ($entry in $entries) { $null = Assert-Identity $entry.Pid $entry.StartTicks $entry.Name; $entry.Samples.Add((Read-Metrics $entry.Pid)); $null = Assert-Identity $entry.Pid $entry.StartTicks $entry.Name }

        $groupSamples = [Collections.Generic.List[object]]::new()
        foreach ($entry in $entries) {
            $finishDeadline = [DateTime]::UtcNow.AddSeconds(40)
            $finished = $false
            while (-not $finished -and [DateTime]::UtcNow -lt $finishDeadline) {
                $remaining = [Math]::Max(1,[Math]::Ceiling(($finishDeadline-[DateTime]::UtcNow).TotalSeconds))
                $line = Get-NextWorkerLine $entry $remaining
                if ($null -eq $line) { break }
                try { $json = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
                if ($json.phase -eq 'finished') { $finished = $true }
                if ($json.phase -eq 'error') { throw "Worker $($entry.Name) reported error: $line" }
            }
            if (-not $finished) { throw "Worker $($entry.Name) omitted finished phase. Output: $($entry.Lines -join ' | ')" }
            $entry.Process.WaitForExit(5000) | Out-Null
            $stderr = $entry.StderrTask.GetAwaiter().GetResult()
            if (-not $entry.Process.HasExited -or $entry.Process.ExitCode -ne 0) { throw "Worker $($entry.Name) process failed; exit=$($entry.Process.ExitCode) stderr=$stderr" }
            if ($entry.Samples.Count -ne 2) { throw "Worker $($entry.Name) has $($entry.Samples.Count) samples, expected two." }
            $record = [pscustomobject]@{Group=$group.Name;Worker=$entry.Name;Pid=$entry.Pid;StartTicks=$entry.StartTicks;Cmdline=$entry.Cmdline;Sample1=$entry.Samples[0];Sample2=$entry.Samples[1];Output=$entry.Lines;ExitCode=$entry.Process.ExitCode;Stderr=$stderr;EffectScope='init-version-deinit-only; no game, manager enumeration, or six-feature workload'}
            $groupSamples.Add($record); $allSamples.Add($record)
            $tracked.Remove(($tracked | Where-Object Pid -eq $entry.Pid | Select-Object -First 1))
        }
        $pssOk = @($groupSamples | Where-Object { -not $_.Sample1.PssConfirmed -or -not $_.Sample2.PssConfirmed }).Count -eq 0
        $privateOk = @($groupSamples | Where-Object { -not $_.Sample1.PrivateDirtyConfirmed -or -not $_.Sample2.PrivateDirtyConfirmed }).Count -eq 0
        if ($pssOk) {
            $sum1 = [int64]0; $sum2 = [int64]0
            foreach ($record in $groupSamples) { $sum1 += [int64]$record.Sample1.PssKiB; $sum2 += [int64]$record.Sample2.PssKiB }
        } else { $sum1=$null; $sum2=$null }
        Write-Output "GROUP $($group.Name) PSS confirmed=$pssOk PrivateDirty confirmed=$privateOk PSS sum KiB sample1=$sum1 sample2=$sum2"
    }
} finally {
    foreach ($entry in @($entries)) { if ($null -ne $entry.Process -and -not $entry.Process.HasExited) { try { $entry.Process.Kill() } catch {} } }
    foreach ($owned in @($tracked)) {
        try { if (-not (Stop-TrackedRemote $owned.Pid $owned.StartTicks $owned.Needle)) { $canRemoveRoot=$false; Write-Warning "Retaining test files: identity did not match PID $($owned.Pid)" } } catch { $canRemoveRoot=$false; Write-Warning "Could not confirm termination of owned test PID $($owned.Pid): $_" }
    }
    if ($canRemoveRoot) { try { Invoke-Shell "rm -rf $(Quote-Shell $remoteRoot)" | Out-Null } catch { Write-Warning "Could not remove invocation-owned temporary path $remoteRoot" } }
    else { Write-Warning "Retaining invocation-owned temporary path $remoteRoot because a PID remains unconfirmed." }
}

$result = [pscustomobject]@{Device=$Serial;Api=[int]$sdk;Abi=$abi;ShellUid=[int]$uid;RemoteTestDirectory=$remoteRoot;HoldMs=$HoldMs;MetricUnits='KiB from smaps_rollup; decimal MB is KiB*0.001024 and binary MiB is KiB/1024';Samples=$allSamples;AllInitOnly=$true}
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputJson -Encoding UTF8
Write-Output "Wrote $OutputJson"
Write-Output "Completed $($allSamples.Count) processes; all memory effects are init-only and do not establish six-feature savings."
