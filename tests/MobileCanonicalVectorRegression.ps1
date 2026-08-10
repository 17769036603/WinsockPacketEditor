param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

function Get-TextFile {
    param([string]$Path)
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

function Get-Sha256Hex {
    param([byte[]]$Bytes)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

$expectedCanonical = '{"assistant":[],"assistantGroups":[],"progression":[],"progressionGroups":[],"schemaVersion":2,"send":[{"id":"a","name":"x","sortOrder":2}],"sendGroups":[]}'
$expectedHash = '84a366e6cb1ae692c94639ca25726920d622983f59b781b2cf067c77aecffbe2'
$payloadJson = '{"sendGroups":[],"schemaVersion":2,"assistant":[],"send":[{"sortOrder":2,"name":"x","id":"a"}],"progressionGroups":[],"assistantGroups":[],"progression":[]}'

$modelsPath = Join-Path $RepositoryRoot 'mobile\app\src\main\java\com\xnas\wpe\mobile\SyncModels.java'
$javaHome = $env:JAVA_HOME
$sdkRoot = if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { $env:ANDROID_HOME }
if ([string]::IsNullOrWhiteSpace($javaHome) -or [string]::IsNullOrWhiteSpace($sdkRoot)) {
    throw 'JAVA_HOME and ANDROID_SDK_ROOT/ANDROID_HOME are required for the canonical vector regression.'
}
$javac = Join-Path $javaHome 'bin\javac.exe'
$java = Join-Path $javaHome 'bin\java.exe'
$androidJar = Join-Path $sdkRoot 'platforms\android-35\android.jar'
foreach ($required in @($modelsPath, $javac, $java, $androidJar)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Missing canonical vector dependency: $required"
    }
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('wpe-mobile-vector-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $jsonPackage = Join-Path $temporaryRoot 'org\json'
    New-Item -ItemType Directory -Path $jsonPackage | Out-Null
    $jsonExceptionPath = Join-Path $jsonPackage 'JSONException.java'
    $jsonObjectPath = Join-Path $jsonPackage 'JSONObject.java'
    $jsonArrayPath = Join-Path $jsonPackage 'JSONArray.java'
    $mainPath = Join-Path $temporaryRoot 'VectorMain.java'
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($jsonExceptionPath, @'
package org.json;
public class JSONException extends Exception {
    public JSONException(String message) { super(message); }
}
'@, $utf8NoBom)
    [IO.File]::WriteAllText($jsonObjectPath, @'
package org.json;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.Map;
public class JSONObject {
    public static final Object NULL = new Object() { public String toString() { return "null"; } };
    private final Map<String, Object> values = new LinkedHashMap<>();
    public JSONObject() { }
    public JSONObject put(String key, Object value) throws JSONException {
        if (key == null) throw new JSONException("null key");
        values.put(key, value == null ? NULL : value);
        return this;
    }
    public JSONArray optJSONArray(String key) {
        Object value = values.get(key);
        return value instanceof JSONArray ? (JSONArray)value : null;
    }
    public JSONObject optJSONObject(String key) {
        Object value = values.get(key);
        return value instanceof JSONObject ? (JSONObject)value : null;
    }
    public Object opt(String key) { return values.get(key); }
    public boolean has(String key) { return values.containsKey(key); }
    public boolean isNull(String key) { return !values.containsKey(key) || values.get(key) == NULL; }
    public String optString(String key, String fallback) {
        Object value = opt(key);
        return value == null || value == NULL ? fallback : String.valueOf(value);
    }
    public boolean optBoolean(String key, boolean fallback) {
        Object value = opt(key);
        return value instanceof Boolean ? ((Boolean)value).booleanValue() : fallback;
    }
    public int optInt(String key, int fallback) {
        Object value = opt(key);
        return value instanceof Number ? ((Number)value).intValue() : fallback;
    }
    public Iterator<String> keys() { return values.keySet().iterator(); }
}
'@, $utf8NoBom)
    [IO.File]::WriteAllText($jsonArrayPath, @'
package org.json;
import java.util.ArrayList;
import java.util.List;
public class JSONArray {
    private final List<Object> values = new ArrayList<>();
    public JSONArray() { }
    public JSONArray put(Object value) { values.add(value == null ? JSONObject.NULL : value); return this; }
    public int length() { return values.size(); }
    public Object opt(int index) { return index < 0 || index >= values.size() ? null : values.get(index); }
    public String optString(int index, String fallback) {
        Object value = opt(index);
        return value == null || value == JSONObject.NULL ? fallback : String.valueOf(value);
    }
    public JSONObject optJSONObject(int index) {
        Object value = opt(index);
        return value instanceof JSONObject ? (JSONObject)value : null;
    }
}
'@, $utf8NoBom)

    [IO.File]::WriteAllText($mainPath, @'
import org.json.JSONArray;
import org.json.JSONObject;
import com.xnas.wpe.mobile.SyncModels;

public class VectorMain {
    public static void main(String[] args) throws Exception {
        JSONObject value = new JSONObject();
        value.put("assistant", new JSONArray());
        value.put("assistantGroups", new JSONArray());
        value.put("progression", new JSONArray());
        value.put("progressionGroups", new JSONArray());
        value.put("schemaVersion", Integer.valueOf(2));
        JSONArray send = new JSONArray();
        JSONObject item = new JSONObject();
        item.put("id", "a");
        item.put("name", "x");
        item.put("sortOrder", Integer.valueOf(2));
        send.put(item);
        value.put("send", send);
        value.put("sendGroups", new JSONArray());
        String canonical = SyncModels.canonicalPayload(value);
        System.out.println("CANONICAL=" + canonical);
        System.out.println("SHA=" + SyncModels.sha256Hex(canonical.getBytes(java.nio.charset.StandardCharsets.UTF_8)));
    }
}
'@, $utf8NoBom)

    & $javac -encoding UTF-8 -cp $androidJar -d $temporaryRoot $jsonExceptionPath $jsonObjectPath $jsonArrayPath $modelsPath $mainPath
    if ($LASTEXITCODE -ne 0) {
        throw 'SyncModels.java could not be compiled for the canonical vector regression.'
    }

    $javaOutput = @(& $java '-cp' ($temporaryRoot + ';' + $androidJar) 'VectorMain' 2>&1)
    $javaCanonicalLine = $javaOutput | Where-Object { $_.ToString().StartsWith('CANONICAL=') } | Select-Object -Last 1
    $javaHashLine = $javaOutput | Where-Object { $_.ToString().StartsWith('SHA=') } | Select-Object -Last 1
    if ($null -eq $javaCanonicalLine -or $javaCanonicalLine.ToString() -ne ('CANONICAL=' + $expectedCanonical)) {
        $javaOutput | Select-Object -Last 40
        throw 'Android canonical JSON does not match the fixed protocol vector.'
    }
    if ($null -eq $javaHashLine -or $javaHashLine.ToString() -ne ('SHA=' + $expectedHash)) {
        $javaOutput | Select-Object -Last 40
        throw 'Android canonical SHA-256 does not match the fixed protocol vector.'
    }

    $desktopDll = $null
    foreach ($buildName in @('DesktopAuditBuildFinal3', 'DesktopAuditBuildFinal2')) {
        $candidate = Join-Path $RepositoryRoot ('WPELibrary\work\' + $buildName + '\WPELibrary.dll')
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $desktopDll = $candidate
            break
        }
    }
    if ($null -eq $desktopDll) {
        $desktopDll = Join-Path $RepositoryRoot 'WPELibrary\bin\Release\WPELibrary.dll'
    }
    if (-not (Test-Path -LiteralPath $desktopDll -PathType Leaf)) {
        throw 'A built WPELibrary.dll is required for the C# canonical vector regression.'
    }
    $newtonsoftPath = Join-Path (Split-Path -Parent $desktopDll) 'Newtonsoft.Json.dll'
    if (-not (Test-Path -LiteralPath $newtonsoftPath -PathType Leaf)) {
        $newtonsoft = Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'packages') -Recurse -Filter 'Newtonsoft.Json.dll' -File |
            Select-Object -First 1
        if ($null -ne $newtonsoft) {
            $newtonsoftPath = $newtonsoft.FullName
        }
    }
    if (-not (Test-Path -LiteralPath $newtonsoftPath -PathType Leaf)) {
        throw 'Newtonsoft.Json.dll is required for the C# canonical vector regression.'
    }
    $newtonsoftAssembly = [Reflection.Assembly]::LoadFrom($newtonsoftPath)
    $desktopAssembly = [Reflection.Assembly]::LoadFrom($desktopDll)
    $jObjectType = $newtonsoftAssembly.GetType('Newtonsoft.Json.Linq.JObject', $true)
    $parseFlags = [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static
    $parseMethod = $jObjectType.GetMethod('Parse', $parseFlags, $null, [Type[]]@([string]), $null)
    $parseArguments = New-Object object[] 1
    $parseArguments[0] = $payloadJson
    $jsonToken = $parseMethod.Invoke($null, $parseArguments)
    $builderType = $desktopAssembly.GetType('WPELibrary.Lib.WebAPI.MobilePresetSnapshotBuilder', $true)
    $canonicalFlags = [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::NonPublic
    $canonicalMethod = $builderType.GetMethod('Canonicalize', $canonicalFlags)
    $canonicalArguments = New-Object object[] 1
    $canonicalArguments[0] = $jsonToken
    $desktopCanonical = [string]$canonicalMethod.Invoke($null, $canonicalArguments)
    if ($desktopCanonical -ne $expectedCanonical) {
        throw 'Desktop canonical JSON does not match the fixed protocol vector.'
    }
    $desktopHash = Get-Sha256Hex ([Text.Encoding]::UTF8.GetBytes($desktopCanonical))
    if ($desktopHash -ne $expectedHash) {
        throw 'Desktop canonical SHA-256 does not match the fixed protocol vector.'
    }
    Write-Output 'Mobile C#/Java canonical vector regression passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
