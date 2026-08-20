param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

function Get-TextFile {
    param([string]$Path)
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

$modelsPath = Join-Path $RepositoryRoot 'mobile\app\src\main\java\com\xnas\wpe\mobile\SyncModels.java'
$clientPath = Join-Path $RepositoryRoot 'mobile\app\src\main\java\com\xnas\wpe\mobile\WpeSyncClient.java'
$javaHome = $env:JAVA_HOME
$sdkRoot = if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { $env:ANDROID_HOME }
if ([string]::IsNullOrWhiteSpace($javaHome) -or [string]::IsNullOrWhiteSpace($sdkRoot)) {
    throw 'JAVA_HOME and ANDROID_SDK_ROOT/ANDROID_HOME are required for the model contract regression.'
}
$javac = Join-Path $javaHome 'bin\javac.exe'
$java = Join-Path $javaHome 'bin\java.exe'
$androidJar = Join-Path $sdkRoot 'platforms\android-35\android.jar'
foreach ($required in @($modelsPath, $clientPath, $javac, $java, $androidJar)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Missing model contract dependency: $required"
    }
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('wpe-mobile-model-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $jsonPackage = Join-Path $temporaryRoot 'org\json'
    $androidPackage = Join-Path $temporaryRoot 'android\util'
    New-Item -ItemType Directory -Path $jsonPackage, $androidPackage | Out-Null
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)

    $jsonExceptionPath = Join-Path $jsonPackage 'JSONException.java'
    $jsonObjectPath = Join-Path $jsonPackage 'JSONObject.java'
    $jsonArrayPath = Join-Path $jsonPackage 'JSONArray.java'
    $base64Path = Join-Path $androidPackage 'Base64.java'
    $mainPath = Join-Path $temporaryRoot 'ModelContractMain.java'

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
    public JSONObject(String ignored) throws JSONException { throw new JSONException("parser not needed"); }
    public JSONObject put(String key, Object value) throws JSONException {
        if (key == null) throw new JSONException("null key");
        values.put(key, value == null ? NULL : value);
        return this;
    }
    public Object remove(String key) { return values.remove(key); }
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
    @Override public String toString() { return values.toString(); }
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
    [IO.File]::WriteAllText($base64Path, @'
package android.util;
public final class Base64 {
    public static final int DEFAULT = 0;
    public static final int NO_WRAP = 2;
    private Base64() { }
    public static byte[] decode(String value, int flags) {
        return java.util.Base64.getDecoder().decode(value);
    }
    public static String encodeToString(byte[] value, int flags) {
        return java.util.Base64.getEncoder().encodeToString(value);
    }
}
'@, $utf8NoBom)
    [IO.File]::WriteAllText($mainPath, @'
import java.lang.reflect.InvocationTargetException;
import java.nio.charset.StandardCharsets;
import java.util.UUID;
import org.json.JSONArray;
import org.json.JSONObject;
import com.xnas.wpe.mobile.SyncModels;
import com.xnas.wpe.mobile.WpeSyncClient;

public final class ModelContractMain {
    private interface Checked { void run() throws Exception; }
    private static int assertions;

    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
        assertions++;
    }

    private static void expectSync(String name, Checked action) throws Exception {
        try {
            action.run();
            throw new AssertionError("accepted invalid input: " + name);
        } catch (SyncModels.SyncException expected) {
            assertions++;
        }
    }

    private static void expectArgument(String name, Checked action) throws Exception {
        try {
            action.run();
            throw new AssertionError("accepted invalid argument: " + name);
        } catch (IllegalArgumentException expected) {
            assertions++;
        }
    }

    private static void expectIOException(String name, Checked action) throws Exception {
        try {
            action.run();
            throw new AssertionError("accepted invalid route: " + name);
        } catch (java.io.IOException expected) {
            assertions++;
        }
    }

    private static JSONObject runtime(String state, boolean busy, boolean identity) throws Exception {
        JSONObject value = new JSONObject();
        value.put("state", state);
        value.put("starting", "STARTING".equals(state));
        value.put("running", "RUNNING".equals(state));
        value.put("pausing", "PAUSING".equals(state));
        value.put("paused", "PAUSED".equals(state));
        value.put("stopping", "STOPPING".equals(state));
        value.put("isBusy", busy);
        if (identity) {
            value.put("presetId", "11111111-1111-1111-1111-111111111111");
            value.put("jobId", "22222222-2222-2222-2222-222222222222");
            value.put("revision", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            value.put("completedCount", 1);
            value.put("totalCount", 2);
        }
        return value;
    }

    private static JSONObject runtimeBundle(JSONObject send) throws Exception {
        return new JSONObject()
                .put("send", send)
                .put("progression", runtime("IDLE", false, false))
                .put("assistant", runtime("IDLE", false, false));
    }

    private static JSONObject payload() throws Exception {
        return new JSONObject()
                .put("schemaVersion", 2)
                .put("sendGroups", new JSONArray())
                .put("progressionGroups", new JSONArray())
                .put("assistantGroups", new JSONArray())
                .put("send", new JSONArray())
                .put("progression", new JSONArray())
                .put("assistant", new JSONArray());
    }

    private static JSONObject manifestFor(JSONObject value) throws Exception {
        String canonical = SyncModels.canonicalPayload(value);
        String hash = SyncModels.sha256Hex(canonical.getBytes(StandardCharsets.UTF_8));
        value.put("revision", hash);
        value.put("payloadSha256", hash);
        value.put("payloadBytes", canonical.getBytes(StandardCharsets.UTF_8).length);
        return new JSONObject()
                .put("schemaVersion", 2)
                .put("revision", hash)
                .put("payloadSha256", hash)
                .put("payloadBytes", canonical.getBytes(StandardCharsets.UTF_8).length)
                .put("sendCount", value.optJSONArray("send").length())
                .put("progressionCount", value.optJSONArray("progression").length())
                .put("assistantCount", value.optJSONArray("assistant").length())
                .put("capabilities", new JSONArray().put("fullSnapshot").put("expectedRevision"));
    }

    private static JSONObject sendPreset(boolean validLength) throws Exception {
        JSONObject packet = new JSONObject()
                .put("packetType", "TCP")
                .put("from", "local")
                .put("to", "remote")
                .put("length", validLength ? 1 : 2)
                .put("dataBase64", "AQ==")
                .put("byteAnnotations", new JSONArray());
        return new JSONObject()
                .put("id", "33333333-3333-3333-3333-333333333333")
                .put("groupId", "44444444-4444-4444-4444-444444444444")
                .put("groupName", "demo")
                .put("name", "demo preset")
                .put("sortOrder", 1)
                .put("enabled", true)
                .put("loopCount", 1)
                .put("intervalMs", 10)
                .put("systemSocket", false)
                .put("notes", "")
                .put("packets", new JSONArray().put(packet));
    }

    private static JSONObject sendPayload(boolean validLength) throws Exception {
        return payload()
                .put("sendGroups", new JSONArray().put(new JSONObject()
                        .put("id", "44444444-4444-4444-4444-444444444444")
                        .put("name", "demo")
                        .put("sortOrder", 1)))
                .put("send", new JSONArray().put(sendPreset(validLength)));
    }

    private static void testRuntime() throws Exception {
        SyncModels.RuntimeBundle parsed = SyncModels.RuntimeBundle.parseStrict(
                runtimeBundle(runtime("RUNNING", true, true)));
        check(parsed.send.state == SyncModels.RuntimeState.RUNNING, "valid runtime state");
        check(parsed.send.isBusy(), "valid runtime busy state");

        JSONObject missingJob = runtime("RUNNING", true, true);
        missingJob.remove("jobId");
        expectSync("busy runtime without job id", () ->
                SyncModels.RuntimeBundle.parseStrict(runtimeBundle(missingJob)));

        JSONObject aliasMismatch = runtime("RUNNING", true, true)
                .put("CompletedCount", 2);
        expectSync("runtime count aliases disagree", () ->
                SyncModels.RuntimeBundle.parseStrict(runtimeBundle(aliasMismatch)));

        JSONObject badRevision = runtime("RUNNING", true, true)
                .put("revision", Integer.valueOf(1));
        expectSync("runtime revision has wrong type", () ->
                SyncModels.RuntimeBundle.parseStrict(runtimeBundle(badRevision)));

        JSONObject shortUuid = runtime("RUNNING", true, true)
                .put("jobId", "1-1-1-1-1");
        expectSync("runtime UUID is not canonical", () ->
                SyncModels.RuntimeBundle.parseStrict(runtimeBundle(shortUuid)));

        JSONObject paddedRevision = runtime("RUNNING", true, true)
                .put("revision", " aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        expectSync("runtime revision has surrounding whitespace", () ->
                SyncModels.RuntimeBundle.parseStrict(runtimeBundle(paddedRevision)));

        JSONObject badState = runtime("UNKNOWN_STATE", false, false);
        expectSync("unknown runtime state", () ->
                SyncModels.RuntimeBundle.parseStrict(runtimeBundle(badState)));
    }

    private static void testSnapshot() throws Exception {
        JSONObject empty = payload();
        JSONObject emptyManifest = manifestFor(empty);
        SyncModels.Snapshot parsed = SyncModels.Snapshot.parse(
                empty, SyncModels.Manifest.parse(emptyManifest, SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES),
                SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES);
        check(parsed.send.isEmpty() && parsed.progression.isEmpty() && parsed.assistant.isEmpty(),
                "valid empty snapshot");

        JSONObject conflictingManifest = manifestFor(payload());
        conflictingManifest.put("PayloadBytes",
                conflictingManifest.optInt("payloadBytes", 0) + 1);
        expectSync("manifest integer aliases disagree", () ->
                SyncModels.Manifest.parse(conflictingManifest,
                        SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES));

        JSONObject duplicateGroups = payload().put("sendGroups", new JSONArray()
                .put(new JSONObject().put("id", "55555555-5555-5555-5555-555555555555")
                        .put("name", "one").put("sortOrder", 1))
                .put(new JSONObject().put("id", "55555555-5555-5555-5555-555555555555")
                        .put("name", "two").put("sortOrder", 2)));
        JSONObject duplicateManifest = manifestFor(duplicateGroups);
        expectSync("duplicate group id", () -> SyncModels.Snapshot.parse(
                duplicateGroups, SyncModels.Manifest.parse(duplicateManifest,
                        SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES), SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES));

        JSONObject badPacket = sendPayload(false);
        JSONObject badPacketManifest = manifestFor(badPacket);
        expectSync("packet length does not match Base64", () -> SyncModels.Snapshot.parse(
                badPacket, SyncModels.Manifest.parse(badPacketManifest,
                        SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES), SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES));

        JSONObject conflictingGroup = sendPayload(true);
        conflictingGroup.optJSONArray("sendGroups").optJSONObject(0)
                .put("Id", "55555555-5555-5555-5555-555555555555");
        JSONObject conflictingGroupManifest = manifestFor(conflictingGroup);
        expectSync("group UUID aliases disagree", () -> SyncModels.Snapshot.parse(
                conflictingGroup, SyncModels.Manifest.parse(conflictingGroupManifest,
                        SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES), SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES));

        JSONObject conflictingPreset = sendPayload(true);
        conflictingPreset.optJSONArray("send").optJSONObject(0)
                .put("ID", "66666666-6666-6666-6666-666666666666");
        JSONObject conflictingPresetManifest = manifestFor(conflictingPreset);
        expectSync("preset UUID aliases disagree", () -> SyncModels.Snapshot.parse(
                conflictingPreset, SyncModels.Manifest.parse(conflictingPresetManifest,
                        SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES), SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES));

        JSONObject validSend = sendPayload(true);
        JSONObject validSendManifest = manifestFor(validSend);
        SyncModels.Snapshot sendSnapshot = SyncModels.Snapshot.parse(
                validSend, SyncModels.Manifest.parse(validSendManifest,
                        SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES), SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES);
        check(sendSnapshot.send.size() == 1
                && sendSnapshot.send.get(0).raw.optJSONArray("packets").length() == 1,
                "valid send snapshot");
    }

    private static void testClientBoundaries() throws Exception {
        expectArgument("cleartext URL", () -> new WpeSyncClient("http://127.0.0.1"));
        expectArgument("URL query", () -> new WpeSyncClient("https://127.0.0.1:1/?x=1"));
        WpeSyncClient client = new WpeSyncClient("https://127.0.0.1:1");
        expectIOException("parent route", () -> client.get("../manifest"));
        expectIOException("absolute route", () -> client.get("https://127.0.0.1/manifest"));
        expectIOException("query route", () -> client.get("manifest?x=1"));
        expectIOException("backslash route", () -> client.get("MobileSync\\manifest"));
    }

    public static void main(String[] args) throws Exception {
        testRuntime();
        testSnapshot();
        testClientBoundaries();
        System.out.println("Mobile model contract regression passed: " + assertions + " assertions.");
    }
}
'@, $utf8NoBom)

    $sources = @($jsonExceptionPath, $jsonObjectPath, $jsonArrayPath, $base64Path,
        $modelsPath, $clientPath, $mainPath)
    & $javac -encoding UTF-8 -cp $androidJar -d $temporaryRoot $sources
    if ($LASTEXITCODE -ne 0) {
        throw 'Mobile model contract Java sources could not be compiled.'
    }
    $output = @(& $java '-cp' ($temporaryRoot + ';' + $androidJar) 'ModelContractMain' 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $output | Select-Object -Last 80
        throw 'Mobile model contract regression failed.'
    }
    $output
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
