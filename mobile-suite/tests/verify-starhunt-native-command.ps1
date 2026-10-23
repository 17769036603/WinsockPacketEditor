$ErrorActionPreference = 'Stop'
$moduleRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$rustSourcePath = Join-Path $moduleRoot 'native\starhunt\worker-rust\src\main.rs'
$evidenceDirectory = Join-Path $repositoryRoot '.scratch\starhunt-multilevel-20261018\native-lua'
$logPath = Join-Path $evidenceDirectory 'native-command-self-test.log'
$originalPath = $env:PATH
if (Test-Path -LiteralPath 'C:\Users\Administrator\.cargo\bin\rustc.exe') {
    $env:PATH = 'C:\Users\Administrator\.cargo\bin;' + $env:PATH
}
$rustc = Get-Command rustc -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
if (-not $rustc) {
    [System.IO.File]::WriteAllText($logPath, 'rustc is unavailable; native command parser self-test was not run.', [System.Text.UTF8Encoding]::new($false))
    $env:PATH = $originalPath
    throw "rustc is unavailable; cannot run the native command parser self-test. Evidence: $logPath"
}
$rustcPath = $rustc.Source

$source = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $rustSourcePath), [System.Text.Encoding]::UTF8).Replace("`r`n", "`n")
$startMarker = "#[derive(Clone, Debug, PartialEq, Eq)]`nstruct StarHuntCommand {"
$endMarker = "`nfn game_module_dir("
$start = $source.IndexOf($startMarker, [System.StringComparison]::Ordinal)
if ($start -lt 0) { throw 'Could not find the production StarHuntCommand/parser start marker.' }
$end = $source.IndexOf($endMarker, $start, [System.StringComparison]::Ordinal)
if ($end -lt 0) { throw 'Could not find the production parser end marker before game_module_dir.' }
$productionParser = $source.Substring($start, $end - $start)

$assertions = @'

#[test]
fn accepts_canonical_multi_level_run_and_scan_commands() {
    assert_eq!(parse_starhunt_command("sequence=7\nmode=run\nlevels=8,9,10\nmap=1018\n").unwrap().levels, vec![8, 9, 10]);
    assert_eq!(parse_starhunt_command("sequence=7\nmode=run\nlevels=1,999\nmap=1018\n").unwrap().levels, vec![1, 999]);
    assert_eq!(parse_starhunt_command("sequence=7\nmode=scan\nlevels=\nmap=0\n").unwrap().levels, Vec::<u32>::new());
    assert_eq!(parse_starhunt_command("sequence=7\nmode=scan\nlevels=8,9\nmap=1018\n").unwrap().levels, vec![8, 9]);
}

#[test]
fn accepts_legacy_single_level_and_empty_scan_commands() {
    assert_eq!(parse_starhunt_command("sequence=7\nmode=run\nlevel=21\nmap=1018\n").unwrap().levels, vec![21]);
    assert_eq!(parse_starhunt_command("sequence=7\nmode=run\nlevel=0008\nmap=1018\n").unwrap().levels, vec![8]);
    assert_eq!(parse_starhunt_command("sequence=7\nmode=scan\nlevel=0\nmap=0\n").unwrap().levels, Vec::<u32>::new());
    assert_eq!(parse_starhunt_command("sequence=7\nmode=scan\nlevel=1\nmap=0\n").unwrap().levels, vec![1]);
}

#[test]
fn rejects_invalid_collections_and_command_shapes() {
    let invalid = [
        "sequence=0\nmode=run\nlevels=8\nmap=1\n",
        "sequence=+7\nmode=run\nlevels=8\nmap=1\n",
        "sequence=9007199254740992\nmode=run\nlevels=8\nmap=1\n",
        "sequence=7\nmode=run\nlevels=\nmap=1\n",
        "sequence=7\nmode=run\nlevels=0\nmap=1\n",
        "sequence=7\nmode=run\nlevels=1000\nmap=1\n",
        "sequence=7\nmode=run\nlevels=08\nmap=1\n",
        "sequence=7\nmode=run\nlevels=9,8\nmap=1\n",
        "sequence=7\nmode=run\nlevels=8,8\nmap=1\n",
        "sequence=7\nmode=run\nlevels=8,,9\nmap=1\n",
        "sequence=7\nmode=run\nlevels=8,\nmap=1\n",
        "sequence=7\nmode=run\nlevels=\u{0668}\nmap=1\n",
        "sequence=7\nmode=run\nlevels=8\nlevel=8\nmap=1\n",
        "sequence=7\nmode=run\nlevels=8\nmap=0\n",
        "sequence=7\nmode=run\nlevel=0\nmap=1\n",
        "sequence=7\nmode=run\nlevels=8\nlevels=8\nmap=1\n",
        "sequence=7\nsequence=8\nmode=scan\nlevels=\nmap=0\n",
        "sequence=7\nmode=run\nlevels=8\nmap=2147483648\n",
        "sequence=7\nmode=run\nlevels=8\nmap=1\nunknown=x\n",
    ];
    for command in invalid {
        assert!(parse_starhunt_command(command).is_none(), "accepted invalid command: {command:?}");
    }
}
'@
$harness = $productionParser + $assertions
$tempRoot = [System.IO.Path]::GetTempPath()
$tempStem = 'starhunt-native-command-' + [guid]::NewGuid().ToString('N')
$harnessPath = Join-Path $tempRoot ($tempStem + '.rs')
$executablePath = Join-Path $tempRoot ($tempStem + '.exe')

New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
try {
    [System.IO.File]::WriteAllText($harnessPath, $harness, [System.Text.UTF8Encoding]::new($false))
    $logLines = @('Source: production StarHuntCommand/parser/helpers extracted from native/starhunt/worker-rust/src/main.rs', (& $rustcPath --version 2>&1))
    $compileOutput = & $rustcPath --edition=2021 --test $harnessPath -o $executablePath 2>&1
    $compileExitCode = $LASTEXITCODE
    $logLines += $compileOutput
    if ($compileExitCode -ne 0) { throw "rustc extraction harness compilation failed with exit code $compileExitCode." }
    $testOutput = & $executablePath --nocapture 2>&1
    $testExitCode = $LASTEXITCODE
    $logLines += $testOutput
    $logLines += "Harness exit code: $testExitCode"
    [System.IO.File]::WriteAllLines($logPath, $logLines, [System.Text.UTF8Encoding]::new($false))
    if ($testExitCode -ne 0) { throw "Native command parser self-test failed with exit code $testExitCode. See $logPath." }
    Write-Output 'Native command parser self-test passed using extracted production code.'
    Write-Output "Evidence: $logPath"
}
finally {
    Remove-Item -LiteralPath $harnessPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $executablePath -Force -ErrorAction SilentlyContinue
    $env:PATH = $originalPath
}
