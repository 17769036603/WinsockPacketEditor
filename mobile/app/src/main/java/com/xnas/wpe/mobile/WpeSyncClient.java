package com.xnas.wpe.mobile;

import org.json.JSONException;
import org.json.JSONObject;
import android.util.Base64;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.zip.GZIPInputStream;

/** HTTPS-only transport. It does not own sync policy or update the UI. */
public final class WpeSyncClient {
    // The response contains a small metadata envelope in addition to the
    // canonical payload whose limit is enforced by SyncModels.
    public static final int MAX_RESPONSE_BYTES = SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES + 64 * 1024;
    private final String baseUrl;
    private final String username;
    private final String password;

    public WpeSyncClient(String baseUrl) {
        this(baseUrl, "", "");
    }

    public WpeSyncClient(String baseUrl, String username, String password) {
        String normalized = baseUrl == null ? "" : baseUrl.trim();
        if (!normalized.regionMatches(true, 0, "https://", 0, "https://".length())) {
            throw new IllegalArgumentException("远程管理必须使用 HTTPS 地址。");
        }
        while (normalized.endsWith("/")) {
            normalized = normalized.substring(0, normalized.length() - 1);
        }
        if (normalized.length() <= "https://".length()) {
            throw new IllegalArgumentException("电脑端地址无效。");
        }
        try {
            URL parsed = new URL(normalized);
            if (!"https".equalsIgnoreCase(parsed.getProtocol())
                    || parsed.getHost() == null || parsed.getHost().isEmpty()
                    || parsed.getUserInfo() != null || parsed.getQuery() != null
                    || parsed.getRef() != null) {
                throw new IllegalArgumentException("电脑端地址无效。");
            }
        } catch (java.net.MalformedURLException ex) {
            throw new IllegalArgumentException("电脑端地址无效。", ex);
        }
        this.baseUrl = normalized + "/";
        this.username = username == null ? "" : username;
        this.password = password == null ? "" : password;
    }

    public String get(String path) throws IOException {
        return requestWithRetry("GET", path, null);
    }

    public JSONObject getJson(String path) throws IOException {
        return parseJson(get(path));
    }

    public JSONObject getJson(String path, int maxResponseBytes) throws IOException {
        return parseJson(requestWithRetry("GET", path, null, 2,
                normalizeResponseLimit(maxResponseBytes)));
    }

    public String post(String path) throws IOException {
        return postJson(path, new JSONObject());
    }

    public String postJson(String path, JSONObject body) throws IOException {
        return requestWithRetry("POST", path, body == null ? "{}" : body.toString());
    }

    public JSONObject postJsonObject(String path, JSONObject body) throws IOException {
        return parseJson(postJson(path, body));
    }

    public JSONObject postJsonObjectWithRetry(String path, JSONObject body) throws IOException {
        return parseJson(requestWithRetry("POST", path,
                body == null ? "{}" : body.toString(), 2));
    }

    private String requestWithRetry(String method, String path, String body) throws IOException {
        return requestWithRetry(method, path, body, "GET".equals(method) ? 2 : 1);
    }

    private String requestWithRetry(String method, String path, String body, int attempts)
            throws IOException {
        return requestWithRetry(method, path, body, attempts, MAX_RESPONSE_BYTES);
    }

    private String requestWithRetry(String method, String path, String body, int attempts,
                                    int maxResponseBytes) throws IOException {
        IOException last = null;
        for (int attempt = 0; attempt < attempts; attempt++) {
            try {
                return request(method, path, body, maxResponseBytes);
            } catch (WpeHttpException ex) {
                last = ex;
                if (ex.statusCode >= 400 && ex.statusCode < 500
                        && ex.statusCode != 408 && ex.statusCode != 429) {
                    break;
                }
            } catch (IOException ex) {
                last = ex;
            }
            if (attempt + 1 < attempts) {
                try {
                    Thread.sleep(150L * (attempt + 1));
                } catch (InterruptedException interrupted) {
                    Thread.currentThread().interrupt();
                    break;
                }
            }
        }
        throw last == null ? new IOException("请求失败") : last;
    }

    private String request(String method, String path, String body) throws IOException {
        return request(method, path, body, MAX_RESPONSE_BYTES);
    }

    private String request(String method, String path, String body, int maxResponseBytes)
            throws IOException {
        HttpURLConnection connection = null;
        try {
            URL url = new URL(buildUrl(path));
            connection = (HttpURLConnection) url.openConnection();
            connection.setRequestMethod(method);
            connection.setConnectTimeout(6000);
            connection.setReadTimeout(12000);
            connection.setUseCaches(false);
            connection.setInstanceFollowRedirects(false);
            connection.setRequestProperty("Accept", "application/json");
            connection.setRequestProperty("Accept-Encoding", "gzip");
            if (!username.isEmpty() && !password.isEmpty()) {
                String credentials = username + ":" + password;
                String encoded = Base64.encodeToString(
                        credentials.getBytes(StandardCharsets.UTF_8), Base64.NO_WRAP);
                connection.setRequestProperty("Authorization", "Basic " + encoded);
            }
            if ("POST".equals(method)) {
                connection.setDoOutput(true);
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
                byte[] bytes = (body == null ? "{}" : body).getBytes(StandardCharsets.UTF_8);
                try (OutputStream output = connection.getOutputStream()) {
                    output.write(bytes);
                }
            }

            int responseCode = connection.getResponseCode();
            InputStream stream = responseCode >= 400
                    ? connection.getErrorStream() : connection.getInputStream();
            String response = readResponse(stream, "gzip".equalsIgnoreCase(
                    connection.getContentEncoding()), maxResponseBytes);
            if (responseCode < 200 || responseCode >= 300) {
                String detail = response.isEmpty() ? connection.getResponseMessage() : response;
                String code = businessCode(detail);
                throw new WpeHttpException(responseCode,
                        code.isEmpty() ? classify(responseCode) : code,
                        safeErrorDetail(responseCode, detail));
            }
            return response;
        } finally {
            if (connection != null) {
                connection.disconnect();
            }
        }
    }

    private String buildUrl(String path) throws IOException {
        String relative = path == null ? "" : path.trim();
        while (relative.startsWith("/")) {
            relative = relative.substring(1);
        }
        if (relative.isEmpty() || relative.contains("..") ||
                relative.contains("://") || relative.contains("//")
                || relative.indexOf('\\') >= 0 || relative.indexOf('?') >= 0
                || relative.indexOf('#') >= 0 || relative.indexOf(':') >= 0
                || relative.indexOf('%') >= 0) {
            throw new IOException("移动端路由无效");
        }
        return baseUrl + relative;
    }

    private static String readResponse(InputStream source, boolean gzip, int maxResponseBytes)
            throws IOException {
        if (source == null) {
            return "";
        }
        InputStream stream = gzip ? new GZIPInputStream(source) : source;
        ByteArrayOutputStream result = new ByteArrayOutputStream();
        int bytes = 0;
        try (InputStream input = stream) {
            byte[] buffer = new byte[8192];
            int count;
            while ((count = input.read(buffer)) != -1) {
                if (count == 0) {
                    continue;
                }
                bytes += count;
                if (bytes > maxResponseBytes) {
                    throw new IOException("电脑端响应过大");
                }
                result.write(buffer, 0, count);
            }
        }
        return new String(result.toByteArray(), StandardCharsets.UTF_8);
    }

    private static int normalizeResponseLimit(int requested) {
        if (requested <= 0) {
            return MAX_RESPONSE_BYTES;
        }
        return Math.min(MAX_RESPONSE_BYTES, requested);
    }

    private static JSONObject parseJson(String value) throws IOException {
        try {
            return new JSONObject(value == null ? "{}" : value);
        } catch (JSONException ex) {
            throw new IOException("电脑端响应格式无效", ex);
        }
    }

    private static String classify(int statusCode) {
        if (statusCode == 401 || statusCode == 403) {
            return "mobile_auth_unexpected";
        }
        if (statusCode == 404) {
            return "mobile_api_missing";
        }
        if (statusCode == 409) {
            return "runtime_conflict";
        }
        if (statusCode >= 500) {
            return "server_error";
        }
        return "request_failed";
    }

    private static String safeErrorDetail(int statusCode, String value) {
        if (statusCode == 401 || statusCode == 403) {
            return "电脑端版本仍要求登录，请更新桌面端";
        }
        if (statusCode == 404) {
            return "电脑端版本未提供移动接口";
        }
        String code = businessCode(value);
        if ("preset_revision_mismatch".equals(code)) {
            return "电脑端预设已变化，请先更新预设";
        }
        if ("runtime_revision_mismatch".equals(code)) {
            return "电脑端任务对应旧预设，暂停或恢复前请先停止旧任务，再更新预设";
        }
        if ("preset_not_found".equals(code)) {
            return "电脑端找不到当前预设";
        }
        if ("preset_invalid".equals(code)) {
            String detail = businessMessage(value);
            return detail.isEmpty()
                    ? "电脑端无法执行当前预设，请检查目标连接和封包参数"
                    : "电脑端无法执行当前预设：" + detail;
        }
        if ("runtime_busy".equals(code)) {
            return "电脑端已有任务运行，请等待完成";
        }
        if ("runtime_not_connected".equals(code)) {
            return "电脑端当前没有可用的目标连接";
        }
        if ("runtime_route_ambiguous".equals(code)) {
            return "当前封包对应多个候选连接，请关闭其他连接或重新抓包";
        }
        if ("send_failed".equals(code)) {
            return "电脑端已接收发送请求，但当前连接未实际发送封包；请重新捕获当前连接后重试";
        }
        if ("pause_not_allowed".equals(code) || "pause_not_found".equals(code)) {
            return "当前任务无法暂停，请刷新运行状态";
        }
        if ("stop_not_found".equals(code)) {
            return "当前任务已经停止或不存在";
        }
        if ("job_not_found".equals(code)) {
            return "当前任务已结束，请刷新运行状态";
        }
        if ("request_invalid".equals(code)) {
            return "运行请求格式无效，请重试";
        }
        if ("runtime_faulted".equals(code)) {
            return "电脑端操作结果未知，请刷新运行状态";
        }
        if (statusCode == 409) {
            return "电脑端拒绝了当前操作，请刷新运行状态";
        }
        if (statusCode >= 500) {
            return "电脑端服务异常，请稍后重试";
        }
        return "电脑端请求失败，请检查配置后重试";
    }

    private static String businessMessage(String value) {
        if (value == null || value.isEmpty()) {
            return "";
        }
        try {
            String message = new JSONObject(value).optString("message", "")
                    .replace('\r', ' ')
                    .replace('\n', ' ')
                    .replace('\t', ' ')
                    .trim();
            if (message.length() > 160) {
                return message.substring(0, 160) + "…";
            }
            return message;
        } catch (JSONException ignored) {
            return "";
        }
    }

    private static String businessCode(String value) {
        if (value == null || value.isEmpty()) {
            return "";
        }
        try {
            return new JSONObject(value).optString("code", "");
        } catch (JSONException ignored) {
            return "";
        }
    }

    public static final class WpeHttpException extends IOException {
        public final int statusCode;
        public final String code;

        WpeHttpException(int statusCode, String code, String message) {
            super(message);
            this.statusCode = statusCode;
            this.code = code;
        }
    }
}
