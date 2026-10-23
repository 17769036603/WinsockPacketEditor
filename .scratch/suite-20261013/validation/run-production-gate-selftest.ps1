$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$work = Join-Path $PSScriptRoot 'production-selftest-work'
New-Item -ItemType Directory -Path $work -Force | Out-Null
$javac = 'F:\AIReverseTools\jdk-21.0.12.1+1\bin\javac.exe'
$java = 'F:\AIReverseTools\jdk-21.0.12.1+1\bin\java.exe'
& $javac --release 11 -encoding UTF-8 -d $work `
  (Join-Path $repo 'mobile-suite\app\src\main\java\com\xnas\wpe\suite\BattleConflictGate.java') `
  (Join-Path $repo 'mobile-suite\tests\BattleConflictGateSelfTest.java')
if ($LASTEXITCODE -ne 0) { throw "javac exited with $LASTEXITCODE" }
& $java -cp $work BattleConflictGateSelfTest
if ($LASTEXITCODE -ne 0) { throw "production self-test exited with $LASTEXITCODE" }
Remove-Item -LiteralPath $work -Recurse -Force
