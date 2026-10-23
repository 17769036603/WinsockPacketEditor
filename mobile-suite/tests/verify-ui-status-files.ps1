param(
    [string]$OutputDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\.scratch\ui-status-files')),
    [string]$SourcePath = (Join-Path $PSScriptRoot '..\app\src\main\java\com\xnas\wpe\suite\UiStatusFiles.java'),
    [string]$SelfTestPath = (Join-Path $PSScriptRoot 'UiStatusFilesSelfTest.java')
)
$ErrorActionPreference = 'Stop'
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { throw 'OutputDirectory must be absolute.' }

if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
    $javac = Join-Path $env:JAVA_HOME 'bin\javac.exe'
    $java = Join-Path $env:JAVA_HOME 'bin\java.exe'
} else {
    $javac = (Get-Command javac -CommandType Application -ErrorAction Stop).Source
    $java = (Get-Command java -CommandType Application -ErrorAction Stop).Source
}
if (-not (Test-Path -LiteralPath $javac -PathType Leaf) -or -not (Test-Path -LiteralPath $java -PathType Leaf)) {
    throw 'Set JAVA_HOME to a JDK or put javac and java on PATH.'
}

$out = [IO.Path]::GetFullPath($OutputDirectory)
$classes = Join-Path $out 'classes'
$data = Join-Path $out 'data'
$androidStubDir = Join-Path $out 'src\android\os'
$null = New-Item -ItemType Directory -Path $classes -Force
$null = New-Item -ItemType Directory -Path $data -Force
$null = New-Item -ItemType Directory -Path $androidStubDir -Force
$clockStubPath = Join-Path $androidStubDir 'SystemClock.java'
$clockStub = @'
package android.os;
public final class SystemClock {
    private SystemClock() { }
    public static long elapsedRealtime() { return System.nanoTime() / 1_000_000L; }
}
'@
[IO.File]::WriteAllText($clockStubPath, $clockStub, [Text.UTF8Encoding]::new($false))
& $javac --release 8 -encoding UTF-8 -d $classes (Resolve-Path $SourcePath) (Resolve-Path $SelfTestPath) $clockStubPath
if ($LASTEXITCODE -ne 0) { throw "javac failed with exit code $LASTEXITCODE" }
& $java -cp $classes com.xnas.wpe.suite.UiStatusFilesSelfTest $data
if ($LASTEXITCODE -ne 0) { throw "UI status cache self-test failed with exit code $LASTEXITCODE" }
