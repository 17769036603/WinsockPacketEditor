package com.xnas.wpe.suite;

import android.content.Context;
import android.content.SharedPreferences;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.os.SystemClock;
import org.json.JSONObject;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.SecureRandom;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;
import java.util.regex.Pattern;

public final class LicenseManager {
    private static final String PRODUCT = "piaomiao-suite";
    /** 朋友免激活构建开关：仅显式构建参数开启，默认关闭；游戏版本门禁另行执行。 */
    public static final boolean BYPASS = BuildConfig.PIAOMIAO_LICENSE_BYPASS;
    private static final String PREFS = "suite_license_private_v1";
    private static final String INSTALL_KEY = "installation_id";
    private static final String TOKEN_KEY = "session_token";
    private static final int MAX_RESPONSE = 8192;
    private static final ExecutorService requests = Executors.newSingleThreadExecutor();
    private static final AtomicBoolean requestActive = new AtomicBoolean();
    private static final AtomicBoolean versionRestrictionStopsRequested = new AtomicBoolean();
    private static final Pattern CODE = Pattern.compile("[A-Z0-9]{32}");
    private static volatile long deadlineElapsed;
    private static volatile String expiresAt = "";
    private static volatile String bootId;
    private static volatile Context applicationContext;
    private LicenseManager() {}

    public static void initialize(Context context) {
        Context app = context.getApplicationContext();
        applicationContext = app == null ? context : app;
    }

    public static String endpoint() { return BuildConfig.PIAOMIAO_LICENSE_URL; }
    public static synchronized String installationId(Context c) {
        SharedPreferences p = c.getApplicationContext().getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        String id = p.getString(INSTALL_KEY, null);
        if (id != null) return id;
        byte[] bytes = new byte[32]; new SecureRandom().nextBytes(bytes);
        id = android.util.Base64.encodeToString(bytes, android.util.Base64.URL_SAFE | android.util.Base64.NO_WRAP | android.util.Base64.NO_PADDING);
        p.edit().putString(INSTALL_KEY, id).commit();
        return id;
    }
    public static String shortInstallationId(Context c) {
        return InstallationHint.fromInstallationId(installationId(c));
    }
    public static boolean isActive() {
        if (BYPASS) return bypassGameVersionAllowed(applicationContext);
        if (deadlineElapsed <= SystemClock.elapsedRealtime()) return false;
        String current = readBootId();
        return current != null && current.equals(bootId);
    }
    public static long remainingSeconds() { return isActive()?Math.max(0,(deadlineElapsed-SystemClock.elapsedRealtime())/1000):0; }
    public static String expiresAt() { return expiresAt; }
    public static String displayStatus(Context c) {
        if (BYPASS) return bypassGameVersionAllowed(c)
                ? "免激活（限指定游戏版本）" : "游戏版本不匹配，功能已停用";
        if (isActive()) {
            String date = formattedExpiry();
            return date.isEmpty() ? "授权有效" : "授权有效，到期：" + date + "（设备本地时间）";
        }
        return hasSession(c) ? "授权待联网验证" : "尚未激活";
    }
    public static String formattedExpiry() {
        try {
            long epoch = Long.parseLong(expiresAt);
            return new SimpleDateFormat("yyyy-MM-dd HH:mm", Locale.SIMPLIFIED_CHINESE).format(new Date(epoch));
        } catch (Exception ignored) { return ""; }
    }
    public static boolean configured() { return validEndpoint(endpoint()); }

    /** Stop active features once when a version-restricted friend build detects an unsupported game. */
    public static void enforceGameVersionRestriction(Context context) {
        if (!BYPASS || context == null) return;
        Context app = context.getApplicationContext();
        if (app == null) app = context;
        if (bypassGameVersionAllowed(app)) {
            versionRestrictionStopsRequested.set(false);
            return;
        }
        if (!versionRestrictionStopsRequested.compareAndSet(false, true)) return;
        requestSafeStops(app);
    }

    private static boolean bypassGameVersionAllowed(Context context) {
        if (!BYPASS || context == null) return false;
        String gamePackage = context.getApplicationContext()
                .getSharedPreferences("suite_target", Context.MODE_PRIVATE)
                .getString("game_package", TargetGame.DHXY_PACKAGE);
        boolean allowedPackage = false;
        for (String allowed : BuildConfig.PIAOMIAO_BYPASS_GAME_PACKAGES.split(",")) {
            if (allowed.equals(gamePackage)) { allowedPackage = true; break; }
        }
        if (!allowedPackage) return false;
        try {
            PackageInfo info = context.getPackageManager().getPackageInfo(gamePackage, 0);
            return info.versionCode == BuildConfig.PIAOMIAO_BYPASS_GAME_VERSION_CODE
                    && BuildConfig.PIAOMIAO_BYPASS_GAME_VERSION_NAME.equals(info.versionName);
        } catch (PackageManager.NameNotFoundException | RuntimeException missingOrUnavailable) {
            return false;
        }
    }
    public static boolean hasSession(Context c) {
        String token=c.getApplicationContext().getSharedPreferences(PREFS,Context.MODE_PRIVATE).getString(TOKEN_KEY,null);
        return token!=null&&!token.isEmpty();
    }
    public static boolean hasStoredLease(Context c) {
        return new File(c.getApplicationContext().getFilesDir(), "license-lease.json").isFile();
    }
    public interface Callback { void complete(String message, boolean success); }
    public static void activate(Context c, String rawCode, Callback cb) {
        if (BYPASS) {
            boolean allowed = bypassGameVersionAllowed(c);
            deliver(c, cb, allowed ? "免激活构建，无需激活码（限指定游戏版本）" : "游戏版本不匹配或未安装，功能已停用", allowed);
            return;
        }
        if (!requestActive.compareAndSet(false, true)) { deliver(c, cb, "授权请求处理中", false); return; }
        final long requestStart=SystemClock.elapsedRealtime();
        final String code=normalizeCode(rawCode);
        requests.execute(() -> { try { doRequest(c, true, code, requestStart, cb); } finally { requestActive.set(false); } });
    }
    public static void verify(Context c, Callback cb) {
        if (BYPASS) {
            boolean allowed = bypassGameVersionAllowed(c);
            deliver(c, cb, allowed ? "免激活构建，无需联网校验（限指定游戏版本）" : "游戏版本不匹配或未安装，功能已停用", allowed);
            return;
        }
        if (!requestActive.compareAndSet(false, true)) { deliver(c, cb, "授权请求处理中", false); return; }
        final long requestStart=SystemClock.elapsedRealtime();
        requests.execute(() -> { try { doRequest(c, false, null, requestStart, cb); } finally { requestActive.set(false); } });
    }
    public static String normalizeCode(String value) {
        if(value==null)return "";
        return value.replace("-", "").replace(" ", "").trim().toUpperCase(java.util.Locale.ROOT);
    }
    private static void doRequest(Context context, boolean activating, String code, long started, Callback cb) {
        Context c=context.getApplicationContext();
        try {
            if (!configured()) throw new IOException("授权服务未配置");
            if (activating && !CODE.matcher(code).matches()) throw new IOException("激活码格式错误，应为32位字符");
            SharedPreferences p=c.getSharedPreferences(PREFS,Context.MODE_PRIVATE);
            String token=p.getString(TOKEN_KEY,null);
            if(!activating && (token==null || token.isEmpty())) throw new IOException("尚未激活");
            JSONObject body=new JSONObject();
            body.put("installationId",installationId(c)); body.put("product",PRODUCT);
            if(activating)body.put("code",code); else body.put("sessionToken",token);
            JSONObject response=post(endpoint()+(activating?"/v1/activate":"/v1/verify"),body);
            String newToken=activating?response.getString("sessionToken"):token;
            String expiry=response.getString("expiresAt");
            long serverTime=response.getLong("serverTime"), lease=response.getLong("leaseSeconds");
            if(newToken.isEmpty()||newToken.length()>4096) throw new IOException("服务返回的授权凭证无效");
            long newDeadline;
            try { newDeadline=LicenseLeaseWindow.deadline(started,SystemClock.elapsedRealtime(),Long.parseLong(expiry),serverTime,lease); }
            catch (IllegalArgumentException invalid) { throw new IOException("服务返回的授权租约无效",invalid); }
            synchronized(LicenseManager.class) {
                p.edit().putString(TOKEN_KEY,newToken).commit();
                expiresAt=expiry; bootId=readBootId();
                deadlineElapsed=newDeadline;
                writeLease(c,newDeadline,bootId);
            }
            deliver(c,cb,"授权有效，到期："+formattedExpiry()+"（设备本地时间；系统时间改动不影响鉴权）",true);
        } catch(HttpFailure f) {
            if (activating && f.status == 409) {
                deliver(c,cb,"激活码已绑定其他安装，请联系管理员解绑或换码",false);
                return;
            }
            boolean expired = !activating && f.status == 403 && "LICENSE_EXPIRED".equals(f.errorCode);
            boolean unauthorized = f.status == 401;
            if (expired) expire(c);
            else if (unauthorized && !activating) invalidate(c);
            String message = expired ? "授权已到期，等待管理员续期并重新验证"
                    : f.status == 403 ? (activating
                    ? (f.businessError ? "激活请求被拒绝（403），请联系管理员核对授权状态"
                    : "授权服务访问被拒绝（403），激活码尚未验证，请稍后重试或联系管理员")
                    : "授权服务访问被拒绝（403），授权状态未更改；请稍后重试")
                    : unauthorized ? (activating ? "激活码无效或已使用（"+f.status+"），现有授权保持不变"
                    : "授权无效或已撤销（"+f.status+"）")
                    : "授权服务暂不可用（"+f.status+"）";
            deliver(c,cb,message,false);
        } catch(Exception e) {
            deliver(c,cb,e.getMessage()==null?"授权验证失败":e.getMessage(),false);
        }
    }
    private static JSONObject post(String url,JSONObject body)throws Exception {
        HttpURLConnection conn=(HttpURLConnection)new URL(url).openConnection();
        try {
            conn.setInstanceFollowRedirects(false);
            conn.setConnectTimeout(5000);conn.setReadTimeout(5000);conn.setRequestMethod("POST");
            conn.setDoOutput(true);conn.setRequestProperty("Content-Type","application/json; charset=utf-8");
            conn.setRequestProperty("User-Agent", "PiaomiaoHelper/"+BuildConfig.VERSION_NAME+" (Android)");
            byte[] data=body.toString().getBytes(StandardCharsets.UTF_8);
            try(OutputStream out=conn.getOutputStream()){out.write(data);}
            int status=conn.getResponseCode();
            if(status<200||status>=300) {
                ErrorResponse error=readErrorResponse(conn.getErrorStream());
                throw new HttpFailure(status,error.errorCode,error.businessError);
            }
            InputStream in=conn.getInputStream();
            if(in==null)throw new IOException("授权服务响应为空");
            return new JSONObject(new String(readBounded(in),StandardCharsets.UTF_8));
        } finally { conn.disconnect(); }
    }
    private static ErrorResponse readErrorResponse(InputStream in) {
        if(in==null)return new ErrorResponse("",false);
        try {
            String json=new String(readBounded(in),StandardCharsets.UTF_8);
            try {
                JSONObject object=new JSONObject(json);
                String code=object.optString("code","");
                Object error=object.opt("error");
                boolean hasError=error instanceof String && !((String)error).isEmpty();
                boolean hasCode="LICENSE_EXPIRED".equals(code);
                return new ErrorResponse(code,hasError||hasCode);
            } catch (Exception ignored) { return new ErrorResponse("",false); }
        } catch (IOException oversizedOrUnreadable) { return new ErrorResponse("",false); }
    }
    private static byte[] readBounded(InputStream in)throws IOException {
        if(in==null)throw new IOException("授权服务响应为空");
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();byte[] buf=new byte[1024];int n;
        try(InputStream stream=in){while((n=stream.read(buf))!=-1){if(bytes.size()+n>MAX_RESPONSE)throw new IOException("授权服务响应超限");bytes.write(buf,0,n);}}
        return bytes.toByteArray();
    }
    private static boolean validEndpoint(String value) {
        try { URI u=URI.create(value); return "https".equalsIgnoreCase(u.getScheme())&&u.getHost()!=null
          &&u.getUserInfo()==null&&u.getQuery()==null&&u.getFragment()==null; } catch(Exception e){return false;}
    }
    private static void invalidate(Context c) {
        if (BYPASS) return;
        clearLease(c, true);
    }
    private static void clearLease(Context c, boolean removeSession) {
        Context app=c.getApplicationContext();
        SharedPreferences prefs=app.getSharedPreferences(PREFS,Context.MODE_PRIVATE);
        boolean hadLease;
        synchronized(LicenseManager.class) {
            hadLease=deadlineElapsed>0
                    || new File(app.getFilesDir(),"license-lease.json").exists();
            SharedPreferences.Editor edit=prefs.edit();
            if(removeSession) edit.remove(TOKEN_KEY);
            edit.commit();
            deadlineElapsed=0;expiresAt="";bootId=null;
            new File(app.getFilesDir(),"license-lease.json").delete();
        }
        if(hadLease)requestSafeStops(app);
    }
    public static void expire(Context c) { if (BYPASS) return; clearLease(c, false); }
    private static void requestSafeStops(Context c) {
        stop(c, com.xnas.wpe.bosshunt.BossHuntService.class,
                com.xnas.wpe.bosshunt.BossHuntService.ACTION_STOP_READ);
        // 找星停止要同时发“停动作”和“停读取”，只发一个会让 worker 继续读。
        stop(c, com.xnas.wpe.starhunt.StarHuntService.class,
                com.xnas.wpe.starhunt.StarHuntService.ACTION_STOP_RUN);
        stop(c, com.xnas.wpe.starhunt.StarHuntService.class,
                com.xnas.wpe.starhunt.StarHuntService.ACTION_STOP_READ);
        stop(c, com.xnas.wpe.anlei.AnleiService.class, "stop");
        stop(c, com.xnas.wpe.mounthold.MountHoldService.class, com.xnas.wpe.mounthold.MountHoldService.ACTION_STOP);
        stop(c, com.xnas.wpe.battleloop.BattleLoopService.class, com.xnas.wpe.battleloop.BattleLoopService.STOP);
        stop(c, com.xnas.wpe.battlepanel.BattleOverlayService.class, com.xnas.wpe.battlepanel.BattleOverlayService.ACTION_STOP);
        stop(c, com.xnas.wpe.jinchan.ConnectionService.class, "stop");
    }
    private static void stop(Context c, Class<?> service, String action) {
        try {
            android.content.Intent intent=new android.content.Intent(c, service).setAction(action);
            if(android.os.Build.VERSION.SDK_INT>=26)c.startForegroundService(intent);else c.startService(intent);
        }
        catch (RuntimeException ignored) { }
    }
    private static void writeLease(Context c,long deadline,String id)throws Exception {
        if(id==null)throw new IOException("系统启动标识不可用");
        File f=new File(c.getFilesDir(),"license-lease.json"),tmp=new File(c.getFilesDir(),"license-lease.json.tmp");
        JSONObject j=new JSONObject();j.put("v",1);j.put("deadlineElapsedMs",deadline);j.put("bootId",id);
        try(FileOutputStream out=new FileOutputStream(tmp)){out.write(j.toString().getBytes(StandardCharsets.UTF_8));out.getFD().sync();}
        if(!tmp.renameTo(f))throw new IOException("授权租约写入失败");
    }
    private static String readBootId() {
        try(BufferedReader r=new BufferedReader(new FileReader("/proc/sys/kernel/random/boot_id"))){return r.readLine();}
        catch(Exception e){return null;}
    }
    private static void deliver(Context c,Callback cb,String msg,boolean ok) {
        if(cb!=null)new android.os.Handler(c.getMainLooper()).post(()->cb.complete(msg,ok));
    }
    private static final class HttpFailure extends IOException {
        final int status; final String errorCode; final boolean businessError;
        HttpFailure(int status,String errorCode,boolean businessError){this.status=status;this.errorCode=errorCode;this.businessError=businessError;}
    }
    private static final class ErrorResponse {
        final String errorCode; final boolean businessError;
        ErrorResponse(String errorCode,boolean businessError){this.errorCode=errorCode;this.businessError=businessError;}
    }
}
