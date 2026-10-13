package com.xnas.wpe.battlepanel;

import android.content.Context;
import android.os.Handler;
import android.os.Looper;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;

final class ResistanceQuery {
    interface Callback { void complete(ResistanceSnapshot snapshot, String error); }

    private static final String DHXY = "com.gdoo.dhxy";
    private final Context context;
    private final Handler main = new Handler(Looper.getMainLooper());
    private final ExecutorService worker = Executors.newSingleThreadExecutor(r -> {
        Thread thread = new Thread(r, "battle-panel-resistance-query");
        thread.setDaemon(true);
        return thread;
    });
    private final AtomicInteger generation = new AtomicInteger();

    ResistanceQuery(Context context) { this.context = context; }

    void query(BattleFrameProtocol.Frame frame, String packageName, Callback callback) {
        final int request = generation.incrementAndGet();
        if (!DHXY.equals(packageName)) {
            post(request, callback, null, "抗性查询仅支持 com.gdoo.dhxy");
            return;
        }
        if (frame == null || frame.status != BattleFrameProtocol.STATUS_LIVE
                || frame.pid == 0 || frame.startTicks == 0 || frame.battleId == 0) {
            post(request, callback, null, "没有有效的 EBP2 战斗帧");
            return;
        }
        worker.execute(() -> {
            if (request != generation.get()) return;
            Files files = null;
            try {
                files = prepare(frame);
                String binary = new File(context.getApplicationInfo().nativeLibraryDir,
                        "libresistance_query.so").getAbsolutePath();
                if (!new File(binary).canExecute()) throw new IllegalStateException("抗性查询 worker 不存在");
                String command = shellQuote(binary) + " " + frame.pid + " " + frame.startTicks
                        + " " + frame.battleId + " " + files.nonce + " "
                        + shellQuote(files.js.getAbsolutePath()) + " "
                        + shellQuote(files.lua.getAbsolutePath()) + " "
                        + shellQuote(files.result.getAbsolutePath());
                Process process = new ProcessBuilder("su", "-c", command)
                        .redirectErrorStream(true).redirectOutput(files.log).start();
                boolean exited = process.waitFor(15, TimeUnit.SECONDS);
                if (!exited) {
                    process.destroy();
                    throw new IllegalStateException("抗性查询超过有限等待时间");
                }
                if (process.exitValue() != 0) {
                    String detail = readLogTail(files.log);
                    throw new IllegalStateException("抗性查询失败（worker " + process.exitValue() + "）"
                            + (detail.isEmpty() ? "" : ": " + detail));
                }
                if (request != generation.get()) return;
                ResistanceSnapshot snapshot = parse(frame, files, new String(read(files.result), StandardCharsets.UTF_8));
                post(request, callback, snapshot, null);
            } catch (Exception error) {
                post(request, callback, null, error.getMessage() == null ? "抗性查询失败" : error.getMessage());
            } finally {
                if (files != null) files.delete();
            }
        });
    }

    /** Invalidates queued or running results; the Lua hook restores after 50 active game-thread ticks. */
    void cancel() { generation.incrementAndGet(); }

    /** Invalidates results and lets any running worker finish its bounded cleanup. */
    void shutdown() {
        generation.incrementAndGet();
        worker.shutdown();
    }

    private void post(int request, Callback callback, ResistanceSnapshot snapshot, String error) {
        main.post(() -> { if (request == generation.get()) callback.complete(snapshot, error); });
    }

    private Files prepare(BattleFrameProtocol.Frame frame) throws Exception {
        String nonce = UUID.randomUUID().toString().replace("-", "");
        File dir = context.getCacheDir();
        Files files = new Files(nonce, new File(dir, "resistance-" + nonce + ".js"),
                new File(dir, "resistance-" + nonce + ".lua"),
                new File(dir, "resistance-" + nonce + ".json"),
                new File(dir, "resistance-" + nonce + ".log"));
        try {
            copyAsset("resistance_agent.js", files.js);
            String template = new String(asset("role_resistance.lua.in"), StandardCharsets.UTF_8);
            String cfg = "{path=__RESISTANCE_RESULT_PATH__,nonce=" + luaQuote(nonce)
                    + ",pid=" + frame.pid + ",startTicks=" + frame.startTicks + ",battle="
                    + frame.battleId + ",round=" + frame.round + "}";
            write(files.lua, template.replace("__RESISTANCE_CONFIG__", cfg).getBytes(StandardCharsets.UTF_8));
            return files;
        } catch (Exception error) {
            files.delete();
            throw error;
        }
    }

    private ResistanceSnapshot parse(BattleFrameProtocol.Frame frame, Files files, String text) throws Exception {
        JSONObject json = new JSONObject(text);
        if (!files.nonce.equals(json.getString("nonce")) || json.getLong("pid") != frame.pid
                || json.getLong("startTicks") != frame.startTicks || json.getLong("battle") != frame.battleId
                || json.getLong("round") != frame.round || !json.getBoolean("ok")
                || !json.getBoolean("restored")) throw new IllegalArgumentException("抗性结果未通过 nonce、身份、战斗或恢复校验");
        Set<Long> allowedPositions = new HashSet<>();
        for (BattleFrameProtocol.Slot slot : frame.slots) allowedPositions.add(slot.position);
        Map<Long, Map<Integer, Double>> values = new HashMap<>();
        JSONArray roles = json.getJSONArray("roles");
        for (int i = 0; i < roles.length(); i++) {
            JSONObject role = roles.getJSONObject(i);
            long position = role.getLong("position");
            if (!allowedPositions.contains(position) || values.containsKey(position))
                throw new IllegalArgumentException("抗性角色位置不属于当前 EBP2 阵型");
            JSONObject properties = role.getJSONObject("values");
            Map<Integer, Double> row = new HashMap<>();
            java.util.Iterator<String> keys = properties.keys();
            while (keys.hasNext()) {
                String key = keys.next();
                int code = Integer.parseInt(key);
                double value = properties.getDouble(key);
                if (code < ResistanceSnapshot.FIRST_CODE || code > ResistanceSnapshot.LAST_CODE
                        || !Double.isFinite(value)) throw new IllegalArgumentException("抗性属性编码或数值无效");
                row.put(code, value);
            }
            values.put(position, row);
        }
        if (values.isEmpty()) throw new IllegalArgumentException("抗性响应没有有效角色");
        return new ResistanceSnapshot(frame.pid, frame.startTicks, frame.battleId, frame.round, values);
    }

    private byte[] asset(String name) throws Exception {
        try (InputStream input = context.getAssets().open(name); ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[8192]; int n;
            while ((n = input.read(buffer)) >= 0) output.write(buffer, 0, n);
            return output.toByteArray();
        }
    }

    private void copyAsset(String name, File path) throws Exception { write(path, asset(name)); }
    private static byte[] read(File path) throws Exception {
        try (InputStream input = new java.io.FileInputStream(path); ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[8192]; int n;
            while ((n = input.read(buffer)) >= 0) output.write(buffer, 0, n);
            return output.toByteArray();
        }
    }
    private static String readLogTail(File path) {
        if (!path.isFile()) return "";
        try (java.io.RandomAccessFile input = new java.io.RandomAccessFile(path, "r")) {
            long length = Math.min(input.length(), 64 * 1024L);
            long offset = Math.max(0L, input.length() - length);
            input.seek(offset);
            byte[] bytes = new byte[(int) length];
            input.readFully(bytes);
            String text = new String(bytes, StandardCharsets.UTF_8).trim();
            if (text.length() > 512) text = text.substring(text.length() - 512);
            return text;
        } catch (Exception ignored) { return ""; }
    }
    private static void write(File path, byte[] data) throws Exception {
        try (FileOutputStream output = new FileOutputStream(path, false)) { output.write(data); output.getFD().sync(); }
        if (!path.setReadable(true, true) || !path.setWritable(true, true)) throw new IllegalStateException("无法设置临时文件权限");
    }
    private static String luaQuote(String value) { return "'" + value.replace("\\", "\\\\").replace("'", "\\'") + "'"; }
    private static String shellQuote(String value) { return "'" + value.replace("'", "'\\''") + "'"; }

    private static final class Files {
        final String nonce; final File js; final File lua; final File result; final File log;
        Files(String nonce, File js, File lua, File result, File log) { this.nonce = nonce; this.js = js; this.lua = lua; this.result = result; this.log = log; }
        void delete() { js.delete(); lua.delete(); result.delete(); log.delete(); new File(result.getPath() + ".tmp").delete(); }
    }
}
