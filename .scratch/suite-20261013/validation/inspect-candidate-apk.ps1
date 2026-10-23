$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$apk = Join-Path $repo 'mobile-suite\app\build\outputs\apk\debug\app-debug.apk'
$copy = Join-Path $env:TEMP ('suite-candidate-' + [Guid]::NewGuid().ToString('N') + '.apk')
$sdk = 'C:\Users\Administrator\AppData\Local\Android\Sdk'
$buildTools = Join-Path $sdk 'build-tools\35.0.0'
$javaHome = 'F:\AIReverseTools\jdk-21.0.12.1+1'
$previousJavaHome = $env:JAVA_HOME
$previousAndroidHome = $env:ANDROID_HOME
try {
  Copy-Item -LiteralPath $apk -Destination $copy
  $sourceHash = (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash
  $copyHash = (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash
  Write-Output "Candidate APK bytes=$((Get-Item -LiteralPath $copy).Length) SHA256=$copyHash"
  if ($sourceHash -ne $copyHash) { throw 'ASCII-path candidate copy hash does not match build output.' }
  $env:JAVA_HOME = $javaHome
  $env:ANDROID_HOME = $sdk

  $badging = & (Join-Path $buildTools 'aapt.exe') dump badging $copy 2>&1
  if ($LASTEXITCODE -ne 0) { throw "aapt badging failed: $badging" }
  $launchable = @($badging | Where-Object { $_ -match '^launchable-activity:' })
  if ($launchable.Count -ne 1 -or $launchable[0] -notmatch "name='com\.xnas\.wpe\.suite\.MainActivity'") { throw "Expected one suite launcher, got: $launchable" }
  $badging | Where-Object { $_ -match '^(package:|sdkVersion:|targetSdkVersion:|native-code:|launchable-activity:|application-label:)' }

  $xmltree = & (Join-Path $buildTools 'aapt.exe') dump xmltree $copy AndroidManifest.xml 2>&1
  if ($LASTEXITCODE -ne 0) { throw "aapt manifest dump failed: $xmltree" }
  $apkanalyzer = Join-Path $sdk 'cmdline-tools\latest\bin\apkanalyzer.bat'
  $decodedManifest = & $apkanalyzer manifest print $copy 2>&1
  if ($LASTEXITCODE -ne 0) { throw "apkanalyzer manifest print failed: $decodedManifest" }
  $binaryManifest = [xml]($decodedManifest -join "`n")
  $androidNs = 'http://schemas.android.com/apk/res/android'
  $launcherActivities = @()
  foreach ($activity in $binaryManifest.SelectNodes("//*[local-name()='activity']")) {
    $actions = @($activity.SelectNodes(".//*[local-name()='action']") | ForEach-Object { $_.GetAttribute('name', $androidNs) })
    $categories = @($activity.SelectNodes(".//*[local-name()='category']") | ForEach-Object { $_.GetAttribute('name', $androidNs) })
    if ($actions -contains 'android.intent.action.MAIN' -and $categories -contains 'android.intent.category.LAUNCHER') { $launcherActivities += $activity }
  }
  if ($launcherActivities.Count -ne 1 -or $launcherActivities[0].GetAttribute('name', $androidNs) -ne 'com.xnas.wpe.suite.MainActivity') { throw 'Binary manifest must contain exactly one suite launcher.' }
  foreach ($id in @('anlei','mounthold','battleloop','battleskip','jinchan','battlepanel')) {
    $name = "com.xnas.wpe.$id.MainActivity"
    $child = $binaryManifest.SelectNodes("//*[local-name()='activity']") | Where-Object { $_.GetAttribute('name', $androidNs) -eq $name }
    if ($null -eq $child -or $child.GetAttribute('exported', $androidNs) -ne 'false') { throw "Binary manifest child activity is missing or exported: $name" }
  }
  $xmltree | Where-Object { $_ -match 'com\.xnas\.wpe\.(suite\.MainActivity|(anlei|mounthold|battleloop|battleskip|jinchan|battlepanel)\.MainActivity)|android\.intent\.category\.LAUNCHER' }

  $signature = & (Join-Path $buildTools 'apksigner.bat') verify --verbose --print-certs $copy 2>&1
  if ($LASTEXITCODE -ne 0) { throw "apksigner failed: $signature" }
  if (-not ($signature -match 'Verified using v1 scheme \(JAR signing\): true') -or
      -not ($signature -match 'Verified using v2 scheme \(APK Signature Scheme v2\): true') -or
      -not ($signature -match 'Number of signers: 1')) { throw "Signature verification did not meet expected v1/v2 single-signer checks: $signature" }
  $signature | Where-Object { $_ -match '^(Verifies|Verified using v1|Verified using v2|Number of signers|Signer #1 certificate DN|Signer #1 certificate SHA-256 digest)' }

  $dexPackages = & $apkanalyzer dex packages --defined-only $copy 2>&1
  if ($LASTEXITCODE -ne 0) { throw "apkanalyzer dex packages failed: $dexPackages" }
  foreach ($id in @('anlei','mounthold','battleloop','battleskip','jinchan','battlepanel')) {
    if (-not ($dexPackages | Where-Object { $_ -match "com\.xnas\.wpe\.$id\.MainActivity" })) { throw "DEX does not contain $id MainActivity" }
  }
  $dexPackages | Where-Object { $_ -match '^C\s' -and $_ -match 'com\.xnas\.wpe\.(suite\.MainActivity|(anlei|mounthold|battleloop|battleskip|jinchan|battlepanel)\.MainActivity$)' }

  $entries = & (Join-Path $javaHome 'bin\jar.exe') tf $copy
  $native = @($entries | Where-Object { $_ -match '^lib/x86_64/[^/]+\.so$' } | Sort-Object)
  $assets = @($entries | Where-Object { $_ -match '^assets/[^/]+$' } | Sort-Object)
  $classes = @($entries | Where-Object { $_ -match '^classes([0-9]+)?\.dex$' } | Sort-Object)
  $expectedNative = @('lib/x86_64/libanlei_worker.so','lib/x86_64/libbattle_loop_worker.so','lib/x86_64/libbattle_skip_launcher.so','lib/x86_64/libbattle_skip_worker.so','lib/x86_64/libenemy_reader.so','lib/x86_64/libjinchan_worker.so','lib/x86_64/libmount_hold_worker.so','lib/x86_64/libresistance_query.so' | Sort-Object)
  $expectedAssets = @('assets/anlei.lua.in','assets/anlei_agent.js','assets/battle_loop.lua.in','assets/battle_loop_agent.js','assets/battle_skip.lua.in','assets/battle_skip_agent.js','assets/jinchan.lua.in','assets/jinchan_agent.js','assets/resistance_agent.js','assets/role_resistance.lua.in' | Sort-Object)
  if (Compare-Object $expectedNative $native) { throw "Native library inventory mismatch: $native" }
  if (Compare-Object $expectedAssets $assets) { throw "Asset inventory mismatch: $assets" }
  Write-Output "DEX entries=$($classes.Count) native libs=$($native.Count) assets=$($assets.Count)"
  $native
  $assets
  $finalItem = Get-Item -LiteralPath $apk
  Write-Output "Build output after inspection bytes=$($finalItem.Length) LastWriteTime=$($finalItem.LastWriteTime)"
}
finally {
  if (Test-Path -LiteralPath $copy) { Remove-Item -LiteralPath $copy -Force }
  if ($null -eq $previousJavaHome) { Remove-Item Env:\JAVA_HOME -ErrorAction SilentlyContinue } else { $env:JAVA_HOME = $previousJavaHome }
  if ($null -eq $previousAndroidHome) { Remove-Item Env:\ANDROID_HOME -ErrorAction SilentlyContinue } else { $env:ANDROID_HOME = $previousAndroidHome }
}
