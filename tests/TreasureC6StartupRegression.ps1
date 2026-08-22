param(
    [string]$BuildDirectory = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = Join-Path $PSScriptRoot "..\WPELibrary\bin\Debug"
}

$libraryDll = Join-Path $BuildDirectory "WPELibrary.dll"
if (-not (Test-Path -LiteralPath $libraryDll)) {
    throw "WPELibrary.dll was not found: $libraryDll"
}

Add-Type -Path $libraryDll

function Assert-Equal($expected, $actual, [string]$message) {
    if ($expected -ne $actual) {
        throw "ASSERT FAILED: $message (expected=$expected actual=$actual)"
    }
}

$controllerType = [WPELibrary.Lib.Vision.TreasureC6ServiceController]
$flags = [System.Reflection.BindingFlags]::NonPublic -bor
    [System.Reflection.BindingFlags]::Static
$extractor = $controllerType.GetMethod("ExtractForegroundPackage", $flags)
if ($null -eq $extractor) {
    throw "ASSERT FAILED: foreground package extractor was not found."
}

$currentPackageDump = @"
topResumedActivity=ActivityRecord{5b9bcef u0 com.gdoo.yzqcxy/org.cocos2dx.lua.AppActivity t20}
ResumedActivity: ActivityRecord{5b9bcef u0 com.gdoo.yzqcxy/org.cocos2dx.lua.AppActivity t20}
mCurrentFocus=Window{18cc08e u0 com.gdoo.yzqcxy/org.cocos2dx.lua.AppActivity}
"@
$actual = $extractor.Invoke($null, @($currentPackageDump))
Assert-Equal "com.gdoo.yzqcxy" $actual `
    "the current game package is extracted from the foreground activity"

$changedPackageDump = @"
topResumedActivity=ActivityRecord{abcd123 u0 com.future.game/com.example.MainActivity t20}
"@
$actual = $extractor.Invoke($null, @($changedPackageDump))
Assert-Equal "com.future.game" $actual `
    "a changed non-system game package is not hardcoded"

$launcherDump = @"
topResumedActivity=ActivityRecord{launcher u0 com.android.launcher3/.Launcher t1}
"@
$actual = $extractor.Invoke($null, @($launcherDump))
Assert-Equal "" $actual `
    "the Android launcher is not treated as the game process"

Write-Output "TreasureC6StartupRegression: PASS"
