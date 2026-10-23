$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$javac = Get-Command javac -ErrorAction SilentlyContinue
$java = Get-Command java -ErrorAction SilentlyContinue
$javacPath = if ($null -ne $javac) { $javac.Source } else { $null }
$javaPath = if ($null -ne $java) { $java.Source } else { $null }
if ([string]::IsNullOrWhiteSpace($javacPath) -or [string]::IsNullOrWhiteSpace($javaPath)) {
    $knownJdkRoot = Join-Path $env:LOCALAPPDATA 'Programs\Microsoft'
    $knownJavac = Get-ChildItem -LiteralPath $knownJdkRoot -Filter javac.exe -File -Recurse `
            -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $knownJavac) {
        $knownJava = Join-Path $knownJavac.DirectoryName 'java.exe'
        if (Test-Path -LiteralPath $knownJava) {
            $javacPath = $knownJavac.FullName
            $javaPath = $knownJava
        }
    }
}
if ([string]::IsNullOrWhiteSpace($javacPath) -or [string]::IsNullOrWhiteSpace($javaPath)) {
    throw 'JDK javac/java are required to run the diagnostics self-test.'
}

$temporary = Join-Path ([IO.Path]::GetTempPath()) ("suite-diagnostics-" + [Guid]::NewGuid().ToString('N'))
$classes = Join-Path $temporary 'classes'
New-Item -ItemType Directory -Path $classes -Force | Out-Null

try {
    $sources = @(
        (Join-Path $root 'app\src\main\java\com\xnas\wpe\suite\ModuleStatus.java'),
        (Join-Path $root 'app\src\main\java\com\xnas\wpe\suite\ModuleStatusSnapshotStore.java'),
        (Join-Path $root 'app\src\main\java\com\xnas\wpe\suite\SuiteDiagnostics.java'),
        (Join-Path $root 'app\src\main\java\com\xnas\wpe\suite\DiagnosticsFormatter.java'),
        (Join-Path $PSScriptRoot 'DiagnosticsSelfTest.java')
    )
    & $javacPath --release 8 -encoding UTF-8 -d $classes @sources
    if ($LASTEXITCODE -ne 0) { throw "javac failed with exit code $LASTEXITCODE" }
    & $javaPath -cp $classes com.xnas.wpe.suite.DiagnosticsSelfTest
    if ($LASTEXITCODE -ne 0) { throw "DiagnosticsSelfTest failed with exit code $LASTEXITCODE" }
}
finally {
    if (Test-Path -LiteralPath $temporary) {
        Remove-Item -LiteralPath $temporary -Recurse -Force
    }
}
