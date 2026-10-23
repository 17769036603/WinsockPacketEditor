$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$sourceRoot = Join-Path $repo 'mobile-suite\app\src\main\java'
$work = Join-Path $PSScriptRoot 'work'
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
$src = Join-Path $work 'src'
$classes = Join-Path $work 'classes'
New-Item -ItemType Directory -Path $src,$classes -Force | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Write-Utf8NoBom([string]$Path, [string]$Text) {
  $parent = Split-Path -Parent $Path
  New-Item -ItemType Directory -Path $parent -Force | Out-Null
  [System.IO.File]::WriteAllText($Path, $Text, $utf8)
}
Write-Utf8NoBom (Join-Path $src 'android\content\Context.java') 'package android.content; public class Context { }'
$packages = @('anlei','battleloop','battlepanel','battleskip','jinchan','mounthold')
foreach ($package in $packages) {
  $probe = "package com.xnas.wpe.$package; public final class ModuleProbe { }"
  Write-Utf8NoBom (Join-Path $src "com\xnas\wpe\$package\ModuleProbe.java") $probe
}
$testSource = Join-Path $PSScriptRoot 'BattleConflictGateOfflineTest.java'
$javac = 'F:\AIReverseTools\jdk-21.0.12.1+1\bin\javac.exe'
$java = 'F:\AIReverseTools\jdk-21.0.12.1+1\bin\java.exe'
$files = @(
  (Join-Path $src 'android\content\Context.java'),
  (Join-Path $sourceRoot 'com\xnas\wpe\suite\BattleConflictGate.java'),
  (Join-Path $sourceRoot 'com\xnas\wpe\suite\ModulePaths.java'),
  $testSource
)
foreach ($package in $packages) { $files += Join-Path $src "com\xnas\wpe\$package\ModuleProbe.java" }
& $javac --release 11 -encoding UTF-8 -d $classes $files
if ($LASTEXITCODE -ne 0) { throw "javac exited with $LASTEXITCODE" }
$tempRoot = Join-Path $work 'cases'
& $java -cp $classes com.xnas.wpe.suite.BattleConflictGateOfflineTest $tempRoot $classes $java
if ($LASTEXITCODE -ne 0) { throw "offline behavior test exited with $LASTEXITCODE" }
Remove-Item -LiteralPath $work -Recurse -Force
