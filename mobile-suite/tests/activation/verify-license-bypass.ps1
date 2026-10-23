$ErrorActionPreference = 'Stop'
$suite = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$gradle = [IO.File]::ReadAllText((Join-Path $suite 'app\build.gradle'))
$nativeScript = [IO.File]::ReadAllText((Join-Path $suite 'build-native.ps1'))
$lease = [IO.File]::ReadAllText((Join-Path $suite 'native\license_lease.rs'))
$java = Join-Path $suite 'app\src\main\java\com\xnas\wpe\suite'
$manager = [IO.File]::ReadAllText((Join-Path $java 'LicenseManager.java'))
$application = [IO.File]::ReadAllText((Join-Path $java 'SuiteApplication.java'))
$activation = [IO.File]::ReadAllText((Join-Path $java 'ActivationActivity.java'))

# The friend bypass is opt-in, version-bound, and must not alter normal licensing.
foreach ($required in @(
    "getOrElse('false').toBoolean()",
    "providers.gradleProperty('piaomiaoVersionCode').getOrElse('16')",
    "providers.gradleProperty('piaomiaoVersionName').getOrElse('2026.10.19.1-friends')",
    "providers.gradleProperty('piaomiaoReleaseChannel').getOrElse('stable')",
    'versionCode piaomiaoVersionCode',
    'versionName piaomiaoVersionName',
    "buildConfigField 'boolean', 'PIAOMIAO_LICENSE_BYPASS', licenseBypass ? 'true' : 'false'",
    "buildConfigField 'String', 'PIAOMIAO_BYPASS_GAME_PACKAGES'",
    "buildConfigField 'int', 'PIAOMIAO_BYPASS_GAME_VERSION_CODE', '1'",
    "buildConfigField 'String', 'PIAOMIAO_BYPASS_GAME_VERSION_NAME'",
    "args '-LicenseBypass'"
)) {
    if (-not $gradle.Contains($required)) { throw "Missing gradle bypass wiring: $required" }
}
if ($gradle -notmatch 'piaomiaoVersionName ==~ /\\d\{4\}\\.\\d\{2\}\\.\\d\{2\}\\.\\d\+-friends\(\?:-open-v\\d\+\)\?\(\?:-test\)\?/') {
    throw 'Release version name must retain the validated stable/test naming format'
}
if ($gradle -notmatch "getOrElse\('stable'\)" -or $gradle -notmatch "getOrElse\('2026\.10\.19\.1-friends'\)") {
    throw 'Default release must remain stable, with the code-16 friend version name'
}
if ($gradle.IndexOf('def licenseBypass = providers.gradleProperty', [StringComparison]::Ordinal) -gt
    $gradle.IndexOf('versionCode piaomiaoVersionCode', [StringComparison]::Ordinal)) {
    throw 'License bypass must not control release versioning'
}
if ($gradle.IndexOf("args '-LicenseBypass'", [StringComparison]::Ordinal) -lt
    $gradle.IndexOf('if (licenseBypass) {', [StringComparison]::Ordinal)) {
    throw 'License bypass native argument must stay inside the licenseBypass branch'
}

$flagAt = $nativeScript.IndexOf("`$env:PIAOMIAO_LICENSE_BYPASS = '1'", [StringComparison]::Ordinal)
$clearAt = $nativeScript.IndexOf('Remove-Item Env:PIAOMIAO_LICENSE_BYPASS', [StringComparison]::Ordinal)
$cargoAt = $nativeScript.IndexOf('function Invoke-CargoBinary', [StringComparison]::Ordinal)
if ($flagAt -lt 0 -or $clearAt -lt 0 -or $cargoAt -lt 0) {
    throw 'build-native.ps1 must set and clear PIAOMIAO_LICENSE_BYPASS before any cargo build'
}
if ($flagAt -gt $cargoAt -or $clearAt -gt $cargoAt) {
    throw 'PIAOMIAO_LICENSE_BYPASS handling must run before the first cargo build'
}
if (-not $nativeScript.Contains('if ($LicenseBypass) {')) {
    throw 'build-native.ps1 must gate the bypass on the -LicenseBypass switch'
}

foreach ($required in @(
    'const LICENSE_BYPASS: bool = bypass_flag(option_env!("PIAOMIAO_LICENSE_BYPASS"));',
    'fn bypass_lease() -> Option<Lease> {',
    'checked_add(BYPASS_LEASE_MS)'
)) {
    if (-not $lease.Contains($required)) { throw "Missing native bypass contract: $required" }
}
$nativeGate = $lease.IndexOf('if LICENSE_BYPASS {', [StringComparison]::Ordinal)
$nativeRead = $lease.IndexOf('fs::read_to_string(path)', [StringComparison]::Ordinal)
if ($nativeGate -lt 0 -or $nativeRead -lt 0 -or $nativeGate -gt $nativeRead) {
    throw 'Native bypass must short-circuit before any lease file read'
}
if (-not $lease.Contains('const BYPASS_LEASE_MS: u64 = 110_000;')) {
    throw 'Bypass lease must stay inside the 120 second window required by the battle skip Lua gate'
}

foreach ($required in @(
    'public static final boolean BYPASS = BuildConfig.PIAOMIAO_LICENSE_BYPASS;',
    'if (BYPASS) return bypassGameVersionAllowed(applicationContext);',
    'info.versionCode == BuildConfig.PIAOMIAO_BYPASS_GAME_VERSION_CODE',
            'BuildConfig.PIAOMIAO_BYPASS_GAME_VERSION_NAME.equals(info.versionName)',
    'public static void enforceGameVersionRestriction(Context context)',
    'versionRestrictionStopsRequested.compareAndSet(false, true)',
    'StarHuntService.ACTION_STOP_RUN',
    'StarHuntService.ACTION_STOP_READ',
    'public static void expire(Context c) { if (BYPASS) return; clearLease(c, false); }',
    'if (BYPASS) return;'
)) {
    if (-not $manager.Contains($required)) { throw "Missing Java bypass contract: $required" }
}
$activeGate = $manager.IndexOf('public static boolean isActive() {', [StringComparison]::Ordinal)
$bypassReturn = $manager.IndexOf('if (BYPASS) return bypassGameVersionAllowed(applicationContext);', $activeGate, [StringComparison]::Ordinal)
$deadlineCheck = $manager.IndexOf('deadlineElapsed <= SystemClock.elapsedRealtime()', $activeGate, [StringComparison]::Ordinal)
if ($activeGate -lt 0 -or $bypassReturn -lt 0 -or $deadlineCheck -lt 0 -or $bypassReturn -gt $deadlineCheck) {
    throw 'LicenseManager.isActive must check the selected game version before bypassing the license deadline'
}
foreach ($method in @('public static void activate(Context c, String rawCode, Callback cb) {', 'public static void verify(Context c, Callback cb) {')) {
    $start = $manager.IndexOf($method, [StringComparison]::Ordinal)
    $guard = $manager.IndexOf('if (BYPASS) {', $start, [StringComparison]::Ordinal)
    $request = $manager.IndexOf('requestActive.compareAndSet', $start, [StringComparison]::Ordinal)
    if ($start -lt 0 -or $guard -lt 0 -or $guard -gt $request) {
        throw "Missing bypass short-circuit without a license request: $method"
    }
}

if (-not $application.Contains('if (LicenseManager.BYPASS) {') -or
    -not $application.Contains('LicenseManager.enforceGameVersionRestriction(this);')) {
    throw 'SuiteApplication must enforce game version restrictions on bypass builds'
}
if (-not $application.Contains('LicenseManager.initialize(this);')) {
    throw 'SuiteApplication must initialize LicenseManager with application context'
}
if (-not $activation.Contains('if (!LicenseManager.BYPASS && LicenseManager.configured()) {')) {
    throw 'ActivationActivity must not auto verify on bypass builds'
}
if (-not $activation.Contains('if (LicenseManager.BYPASS) {')) {
    throw 'ActivationActivity must hide activation controls for the bypass build'
}
if (-not $activation.Contains('LicenseManager.displayStatus(this)')) {
    throw 'ActivationActivity must show the current version-bound bypass status'
}
if (-not $activation.Contains('BuildConfig.PIAOMIAO_BYPASS_GAME_VERSION_NAME') -or
    -not $activation.Contains('BuildConfig.PIAOMIAO_BYPASS_GAME_VERSION_CODE')) {
    throw 'ActivationActivity must show the version values from the bypass build configuration'
}
$main = [IO.File]::ReadAllText((Join-Path $java 'MainActivity.java'))
if (-not $main.Contains('if (LicenseManager.BYPASS) return LicenseManager.isActive() ?')) {
    throw 'MainActivity must label bypass builds and show when the game version is restricted'
}
Write-Output 'LicenseBypassContract PASS (build switch default off, native lease short-circuit, Java gate without requests, activation page notice)'
