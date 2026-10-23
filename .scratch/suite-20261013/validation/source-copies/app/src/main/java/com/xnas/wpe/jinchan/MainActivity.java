package com.xnas.wpe.jinchan;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.content.ServiceConnection;
import android.graphics.Color;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.AdapterView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.nio.ByteBuffer;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.Charset;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.regex.Pattern;
import com.xnas.wpe.suite.ModuleStatus;
import com.xnas.wpe.suite.ModuleStatusProvider;

/** Standalone confirmation UI; all game work runs in the native worker. */
public final class MainActivity extends com.xnas.wpe.suite.ModuleActivity implements ModuleStatusProvider {
    private static final String PREFS = "jinchan";
    private static final String LAST_QUERY = "last_query";
    private static final String LAST_RESULT = "last_result";
    private static final int MAX_RESULT_BYTES = 256 * 1024;
    private static final Pattern DECIMAL = Pattern.compile("[0-9]{1,20}");
    private static final Pattern HEX = Pattern.compile("[0-9a-fA-F]{1,64}");
    private static final ExecutorService WORKER = Executors.newSingleThreadExecutor(r -> {
        Thread thread = new Thread(r, "jinchan-worker");
        thread.setDaemon(true);
        return thread;
    });
    private static final AtomicBoolean BUSY = new AtomicBoolean();
    private static final Handler MAIN = new Handler(Looper.getMainLooper());
    private static java.lang.ref.WeakReference<MainActivity> ACTIVE = new java.lang.ref.WeakReference<>(null);
    private TextView status;
    private TextView preview;
    private Spinner position;
    private Spinner base;
    private Spinner refine;
    private Button query;
    private Button create;
    private Button giveBack;
    private Button connect;
    private Button disconnect;
    private TextView connectionStatus;
    private ConnectionService service;
    private boolean bound;
    private boolean assetsReady;
    private final ServiceConnection serviceConnection = new ServiceConnection() { public void onServiceConnected(android.content.ComponentName n, android.os.IBinder b) { service=((ConnectionService.LocalBinder)b).get(); showPendingResult(); updateConnectionUi(); } public void onServiceDisconnected(android.content.ComponentName n) { service=null; updateConnectionUi(); } };
    private final Runnable connectionRefresh = new Runnable() { @Override public void run() { updateConnectionUi(); MAIN.postDelayed(this, 1000); } };
    private JSONObject snapshot;
    private String snapshotToken="";
    private boolean loading;
    private int lastPosition;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().setStatusBarColor(Color.rgb(13, 20, 29));
        getWindow().setNavigationBarColor(Color.rgb(13, 20, 29));
        buildUi();
        try { copyAsset("jinchan_agent.js"); copyAsset("jinchan.lua.in"); assetsReady=true; }
        catch (Exception e) { setStatus("资源准备失败：" + message(e)); }
        bindService(new Intent(this, ConnectionService.class), serviceConnection, BIND_AUTO_CREATE); bound=true;
        showPendingResult();
        updateButtons();
    }

    @Override protected void onResume() { super.onResume(); ACTIVE = new java.lang.ref.WeakReference<>(this); showPendingResult(); updateButtons(); updateConnectionUi(); MAIN.post(connectionRefresh); }
    @Override protected void onPause() { MAIN.removeCallbacks(connectionRefresh); if (ACTIVE.get() == this) ACTIVE = new java.lang.ref.WeakReference<>(null); super.onPause(); }
    @Override protected void onDestroy(){ if(bound) { unbindService(serviceConnection); bound=false; } service=null; super.onDestroy(); }
    private void connectGame(){ String p=selectedGamePackage(); Intent i=new Intent(this,ConnectionService.class).setAction(ConnectionService.ACTION_CONNECT).putExtra(ConnectionService.EXTRA_PACKAGE,p); if(android.os.Build.VERSION.SDK_INT>=26)startForegroundService(i);else startService(i); Intent game=getPackageManager().getLaunchIntentForPackage(p);if(game!=null)startActivity(game); }
    private void stopGameConnection(){startService(new Intent(this,ConnectionService.class).setAction(ConnectionService.ACTION_STOP));snapshot=null;clearOptions();getSharedPreferences(PREFS,MODE_PRIVATE).edit().remove(LAST_QUERY).apply();updateButtons();}
    private String lastConnectionState="";
    private void updateConnectionUi(){
        if(connectionStatus==null)return;
        String st=service==null?"disconnected":service.getState();
        boolean targetMatches=service!=null&&selectedGamePackage().equals(service.getPackage());
        connectionStatus.setText("连接："+("connected".equals(st)&&!targetMatches?"目标已切换":"connected".equals(st)?"已连接":"connecting".equals(st)?"连接中":"error".equals(st)||"blocked".equals(st)?"失败":"disconnected".equals(st)?"未连接":"未确认"));
        boolean busyState="busy".equals(st), connected=service!=null&&"connected".equals(st)&&targetMatches;
        if(connected){
            String liveToken=service.getToken();String cached=getSharedPreferences(PREFS,MODE_PRIVATE).getString(LAST_QUERY,"");
            if(!cached.isEmpty())try{JSONObject q=new JSONObject(cached);if(!liveToken.equals(q.optString("connectionToken",""))||!selectedGamePackage().equals(q.optString("gamePackage",""))){getSharedPreferences(PREFS,MODE_PRIVATE).edit().remove(LAST_QUERY).apply();snapshot=null;snapshotToken="";clearOptions();}}catch(Exception e){getSharedPreferences(PREFS,MODE_PRIVATE).edit().remove(LAST_QUERY).apply();}
            if(snapshot!=null&&!liveToken.equals(snapshotToken)){snapshot=null;snapshotToken="";clearOptions();}
            if(snapshot==null&&!"connected".equals(lastConnectionState))showPendingResult();
        }else if(!busyState&&snapshot!=null){snapshot=null;snapshotToken="";clearOptions();getSharedPreferences(PREFS,MODE_PRIVATE).edit().remove(LAST_QUERY).apply();}
        lastConnectionState=st;updateButtons();
    }

    private void buildUi() {
        ScrollView scroll = new ScrollView(this);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(20), dp(24), dp(20), dp(28));
        root.setBackgroundColor(Color.rgb(13, 20, 29));
        position = spinner(new String[]{"武器", "项链", "衣服", "帽子", "鞋子"});
        root.addView(label("部位", 14)); root.addView(position, params(-1, -2, 0, 2));
        position.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(AdapterView<?> p, View v, int i, long id) {
                if (!shouldInvalidateSnapshot(lastPosition, i, snapshot != null)) { lastPosition = i; return; }
                lastPosition = i; snapshot = null; clearOptions(); getSharedPreferences(PREFS, MODE_PRIVATE).edit().remove(LAST_QUERY).apply(); setStatus("部位已更改，请重新查询服务器候选。此次更改不触发操作。"); updateButtons();
            }
            @Override public void onNothingSelected(AdapterView<?> p) { }
        });
        root.addView(label("基础属性候选", 14)); base = spinner(new String[0]); root.addView(base, params(-1, -2, 0, 2));
        root.addView(label("炼化属性候选", 14)); refine = spinner(new String[0]); root.addView(refine, params(-1, -2, 0, 2));
        preview = text("", 13, 0xffc1d0d8);
        preview.setPadding(dp(12), dp(12), dp(12), dp(12));
        root.addView(preview, params(-1, -2, 0, 10));
        status = text("尚未查询", 13, 0xffc7dbe0);
        root.addView(status, params(-1, -2, 0, 10));
        connectionStatus=text("连接：连接中",13,0xffc7dbe0); root.addView(connectionStatus,params(-1,-2,0,8));
        connect=button("连接游戏"); root.addView(connect,params(-1,dp(44),0,6)); connect.setOnClickListener(v->connectGame());
        disconnect=button("断开连接"); root.addView(disconnect,params(-1,dp(44),0,4)); disconnect.setOnClickListener(v->stopGameConnection());
        query = button("查询 / 刷新"); root.addView(query, params(-1, dp(50), 0, 14)); query.setOnClickListener(v -> runAction(snapshot == null ? "query" : "refresh", -1, -1));
        create = button("生成金蟾"); root.addView(create, params(-1, dp(50), 0, 8)); create.setOnClickListener(v -> confirm("create"));
        giveBack = button("归还金蟾"); root.addView(giveBack, params(-1, dp(50), 0, 8)); giveBack.setOnClickListener(v -> confirm("return"));
        Button launch = button("返回游戏"); root.addView(launch, params(-1, dp(48), 0, 8)); launch.setOnClickListener(v -> {
            Intent game = getPackageManager().getLaunchIntentForPackage(selectedGamePackage());
            if (game != null) startActivity(game); else setStatus("未找到游戏启动入口。");
        });
        scroll.addView(root); setContentView(scroll);
    }

    private void confirm(String action) {
        if (snapshot == null) { setStatus("请先查询有效服务器候选。"); return; }
        if ("return".equals(action) && !canReturn()) { setStatus("当前角色 UID 无法绑定，已禁用归还。"); return; }
        int b = base.getSelectedItemPosition(), r = refine.getSelectedItemPosition();
        if ("create".equals(action) && (!canCreate() || b < 0 || r < 0)) { setStatus("生成需要有效 token 及已选基础、炼化候选。"); return; }
        String msg = "create".equals(action)
                ? "部位：" + position.getSelectedItem() + "\n基础属性：" + optionText("baseOptions", b)
                    + "\n炼化属性：" + optionText("refineOptions", r)
                    + "\n普通仙玉实测消耗：10 万\n其他奖励：未确认\n是否执行生成？"
                : "当前金蟾装备：" + decodedName(snapshot, "currentNameHex", "currentName") + "（UID " + snapshot.optString("currentUid")
                    + "）\n归还将删除所选部位金蟾，且不返还。其他奖励：未确认。\n是否执行归还？";
        new AlertDialog.Builder(dialogContext()).setTitle("请确认操作").setMessage(msg)
                .setNegativeButton("取消", (d, w) -> { }).setPositiveButton("确认", (d, w) -> runAction(action, b, r)).show();
    }

    private void runAction(String action, int baseIndex, int refineIndex) {
        if (!BUSY.compareAndSet(false, true)) return;
        if (service == null || !"connected".equals(service.getState()) || !selectedGamePackage().equals(service.getPackage())) { BUSY.set(false); setStatus("目标游戏未连接，请手动连接后重试。"); updateButtons(); return; }
        loading = true; setStatus("正在执行，请返回游戏；最长等待 30 秒。不会自动重发。"); updateButtons();
        int pos=position.getSelectedItemPosition();
        String role=snapshot==null?"0":snapshot.optString("role","0"), uid=snapshot==null?"0":snapshot.optString("currentUid","0"), token=snapshot==null?"0":snapshot.optString("previewToken","0");
        String gamePackage=selectedGamePackage(), nonce=UUID.randomUUID().toString().replace("-","");
        boolean invalidatesSnapshot=!("query".equals(action)||"refresh".equals(action)); int selectedBase=0, selectedRefine=0;
        try { checkedDecimal(role,"role"); checkedDecimal(uid,"uid"); checkedHex(token,"preview token"); if(baseIndex>=0)selectedBase=Integer.parseInt(selectedIndex("baseOptions",baseIndex));if(refineIndex>=0)selectedRefine=Integer.parseInt(selectedIndex("refineOptions",refineIndex)); }
        catch(Exception e){BUSY.set(false);loading=false;setStatus(message(e));updateButtons();return;}
        final int bi=selectedBase,ri=selectedRefine; final String expectedUid=uid;
        ConnectionService conn=service;
        String connectionToken=conn.getToken();
        if(!conn.submit(action,pos,bi,ri,role,uid,token,nonce,gamePackage,connectionToken)){BUSY.set(false);loading=false;setStatus("连接状态过期或已有请求未完成，请重新连接并查询；未重发操作。");updateButtons();return;}
        Intent game=getPackageManager().getLaunchIntentForPackage(gamePackage);if(game!=null)startActivity(game);
        WORKER.execute(() -> {
            JSONObject output=null;String error=null;File result=new File(getFilesDir(),"result-"+nonce+".json");long deadline=System.currentTimeMillis()+30000;
            try {
                while(System.currentTimeMillis()<deadline){if(result.isFile())break;String st=conn.getState();if(!connectionToken.equals(conn.getToken())||!("connected".equals(st)||"busy".equals(st))){if(result.isFile())break;throw new IllegalStateException("游戏连接已断开；请重新查询，不要重发");}Thread.sleep(200);}
                if(!result.isFile()||result.length()>MAX_RESULT_BYTES)throw new IllegalStateException("结果文件缺失或超过 256 KiB");
                output=new JSONObject(new String(read(result),StandardCharsets.UTF_8));
                if(!nonce.equals(output.optString("nonce")))throw new IllegalStateException("nonce 校验失败");
                if(!output.optBoolean("restored",false))throw new IllegalStateException(output.optString("message","操作未成功")+"；Lua 未确认清理恢复，已阻止后续操作");
                if(!output.optBoolean("ok",false))throw new IllegalStateException(output.optString("message","操作未成功")); output.put("gamePackage",gamePackage).put("connectionToken",connectionToken).put("pos",pos);
                if("return".equals(action)){JSONArray removed=output.optJSONArray("removed");if(removed==null||removed.length()!=1||!expectedUid.equals(removed.optString(0,"")))throw new IllegalStateException("归还结果未确认删除所选 UID");}
                getSharedPreferences(PREFS,MODE_PRIVATE).edit().putString(LAST_RESULT,output.toString()).apply();if(!invalidatesSnapshot)getSharedPreferences(PREFS,MODE_PRIVATE).edit().putString(LAST_QUERY,output.toString()).apply();
            }catch(Exception e){error=message(e);}
            finally{if(invalidatesSnapshot||("refresh".equals(action)&&error!=null))getSharedPreferences(PREFS,MODE_PRIVATE).edit().remove(LAST_QUERY).apply();result.delete();}
            final JSONObject resultOutput=output;final String resultError=error;
            if(resultError!=null)try{JSONObject failed=resultOutput==null?new JSONObject():new JSONObject(resultOutput.toString());failed.put("ok",false).put("message",resultError);getSharedPreferences(PREFS,MODE_PRIVATE).edit().putString(LAST_RESULT,failed.toString()).apply();}catch(Exception ignored){}
            MAIN.post(()->{try{MainActivity current=ACTIVE.get();if(current==null||current.isFinishing()||current.isDestroyed())return;current.loading=false;
                if(!gamePackage.equals(current.selectedGamePackage())||current.service==null||!connectionToken.equals(current.service.getToken())){current.snapshot=null;current.snapshotToken="";current.clearOptions();
                    getSharedPreferences(PREFS,MODE_PRIVATE).edit().remove(LAST_QUERY).apply();
                    if(invalidatesSnapshot){String notice=resultError==null?"操作期间连接已变化；以下为本次操作结果，请重新连接并查询当前状态。":"操作期间连接已变化；本次操作结果未能确认："+resultError+"。请重新连接并查询。";current.showResult(resultOutput==null?new JSONObject():resultOutput,notice);}
                    else current.setStatus("操作期间连接已变化，本次查询结果已丢弃，请重新查询。");
                    getSharedPreferences(PREFS,MODE_PRIVATE).edit().remove(LAST_RESULT).apply();return;}
                if(invalidatesSnapshot||("refresh".equals(action)&&resultError!=null)){current.snapshot=null;current.clearOptions();}
                if(resultError!=null&&resultOutput!=null)current.showResult(resultOutput,resultError);else if(resultError!=null)current.setStatus(resultError);else if("query".equals(action)||"refresh".equals(action))current.applySnapshot(resultOutput,true);else{current.preview.setText("操作已完成，请重新查询当前状态。");current.showResult(resultOutput);}
                getSharedPreferences(PREFS,MODE_PRIVATE).edit().remove(LAST_RESULT).apply();
            }finally{BUSY.set(false);MainActivity current=ACTIVE.get();if(current!=null&&!current.isFinishing()&&!current.isDestroyed())current.updateButtons();}});
        });
    }
    private void applySnapshot(JSONObject data, boolean show) {
        if (data == null) return;
        int savedPosition = data.optInt("pos", -1);
        if (savedPosition < 0 || savedPosition >= position.getCount()) {
            snapshot = null;
            snapshotToken = "";
            clearOptions();
            getSharedPreferences(PREFS, MODE_PRIVATE).edit().remove(LAST_QUERY).apply();
            setStatus("查询结果缺少有效部位，请重新查询。");
            updateButtons();
            return;
        }
        snapshot = data;
        snapshotToken=data.optString("connectionToken",service==null?"":service.getToken());
        lastPosition = savedPosition;
        position.setSelection(savedPosition);
        setOptions(base, data.optJSONArray("baseOptions")); setOptions(refine, data.optJSONArray("refineOptions"));
        renderPreview(data);
        if (show) setStatus("服务器查询完成，可选择候选。");
        updateButtons();
    }

    private void renderPreview(JSONObject data) {
        StringBuilder out = new StringBuilder("当前金蟾装备：").append(decodedName(data, "currentNameHex", "currentName"))
                .append("（UID ").append(data.optString("currentUid", "0")).append("）\n普通仙玉：")
                .append(data.optString("gold", "未提供")).append("  银两：").append(data.optString("silver", "未提供"))
                .append("  普通仙玉变化：").append(data.optString("deltaGold", "未提供")).append("\n基础候选：\n");
        appendOptions(out, data.optJSONArray("baseOptions")); out.append("\n炼化候选：\n"); appendOptions(out, data.optJSONArray("refineOptions"));
        appendNotifications(out, data.optJSONArray("notifications"));
        preview.setText(out.toString());
    }
    private void appendOptions(StringBuilder out, JSONArray options) {
        if (options == null || options.length() == 0) { out.append("无\n"); return; }
        for (int i = 0; i < options.length(); i++) try {
            JSONObject option = options.getJSONObject(i); out.append("[").append(optionIndex(option)).append("] ").append(decodedName(option, "tagHex", "tag"));
            JSONArray attrs = option.optJSONArray("attrs");
            if (attrs != null) for (int j = 0; j < attrs.length(); j++) {
                JSONObject attr = attrs.getJSONObject(j); out.append(" · ").append(attr.optString("name", attr.optString("key"))).append(" ").append(attr.optString("value", ""));
            }
            out.append('\n');
        } catch (Exception ignored) { }
    }

    private void showResult(JSONObject data) { showResult(data, null); }
    private void showResult(JSONObject data, String error) {
        StringBuilder detail = new StringBuilder(error == null ? data.optString("message", "操作完成") : error);
        JSONArray added = data.optJSONArray("added"), removed = data.optJSONArray("removed");
        if (added != null) for (int i = 0; i < added.length(); i++) { JSONObject item = added.optJSONObject(i); if (item != null) detail.append("\n生成 UID ").append(item.optString("uid", "未提供")).append("：").append(decodedName(item, "nameHex", "name")).append("（类型 ").append(item.optString("typeId", "未提供")).append("，基础 ").append(item.optString("baseMixed", "未提供")).append("，炼化 ").append(item.optString("refineMixed", "未提供")).append("）"); }
        if (removed != null) for (int i = 0; i < removed.length(); i++) detail.append("\n删除 UID：").append(removed.optString(i));
        appendNotifications(detail, data.optJSONArray("notifications"));
        setStatus(detail.toString());
        new AlertDialog.Builder(dialogContext()).setTitle("执行结果").setMessage(detail.toString()).setPositiveButton("知道了", null).show();
    }

    private Activity dialogContext() {
        Activity parent = getParent();
        return parent == null ? this : parent;
    }
    private void showPendingResult() {
        android.content.SharedPreferences prefs = getSharedPreferences(PREFS, MODE_PRIVATE);
        String cached = prefs.getString(LAST_QUERY, "");
        if (!cached.isEmpty()) {
            try {
                JSONObject data = new JSONObject(cached);
                if (service != null && "connected".equals(service.getState())) {
                    if (selectedGamePackage().equals(data.optString("gamePackage", "")) && service.getToken().equals(data.optString("connectionToken", ""))) applySnapshot(data, false);
                    else { prefs.edit().remove(LAST_QUERY).apply(); snapshot = null; snapshotToken=""; clearOptions(); }
                }
            }
            catch (Exception invalidCache) { prefs.edit().remove(LAST_QUERY).apply(); snapshot = null; snapshotToken=""; clearOptions(); }
        } else if (snapshot != null) {
            snapshot = null;
            snapshotToken="";
            clearOptions();
        }
        String result = prefs.getString(LAST_RESULT, "");
        if (result.isEmpty()) return;
        prefs.edit().remove(LAST_RESULT).apply();
        try { showResult(new JSONObject(result)); } catch (Exception ignored) { }
    }
    private void appendNotifications(StringBuilder detail, JSONArray notifications) {
        if (notifications == null) return;
        for (int i = 0; i < Math.min(8, notifications.length()); i++) {
            String message = decodeHexText(notifications.optString(i, ""));
            if (message.isEmpty()) continue;
            if (message.length() > 512) message = message.substring(0, 512) + "…";
            detail.append("\n游戏提示：").append(message);
        }
    }
    private static String decodeHexText(String hex) {
        byte[] bytes = hexBytes(hex);
        if (bytes == null || bytes.length == 0) return "";
        try { return decodeStrict(bytes, StandardCharsets.UTF_8); }
        catch (CharacterCodingException invalidUtf8) {
            try { return decodeStrict(bytes, Charset.forName("GB18030")); }
            catch (Exception invalidLegacyText) { return ""; }
        }
    }
    private boolean canReturn() { return snapshot != null && eligibleToReturn(snapshot.optString("currentUid", ""), decodedName(snapshot, "currentNameHex", "currentName")); }
    private boolean canCreate() { return snapshot != null && eligibleToCreate(snapshot.optString("currentUid", ""), snapshot.optString("role", ""), snapshot.optString("previewToken", ""), base.getCount(), refine.getCount()); }
    private static boolean nonZeroHex(String value) { return HEX.matcher(value).matches() && !value.replace("0", "").isEmpty(); }
    static boolean eligibleToCreate(String uid, String role, String token, int baseCount, int refineCount) { return DECIMAL.matcher(uid).matches() && DECIMAL.matcher(role).matches() && nonZeroHex(token) && baseCount > 0 && refineCount > 0; }
    static boolean eligibleToReturn(String uid, String name) { try { return DECIMAL.matcher(uid).matches() && Long.parseLong(uid) > 0 && name != null && !name.trim().isEmpty(); } catch (NumberFormatException invalidUid) { return false; } }
    static boolean queryEnabled(boolean busy) { return !busy; }
    static boolean shouldInvalidateSnapshot(int previousPosition, int selectedPosition, boolean hasSnapshot) {
        return hasSnapshot && previousPosition != selectedPosition;
    }
    private String selectedIndex(String key, int selected) throws Exception {
        JSONArray array = snapshot.optJSONArray(key);
        if (array == null || selected < 0 || selected >= array.length()) return "0";
        JSONObject option = array.optJSONObject(selected); if (option == null || !option.has("index")) throw new IllegalArgumentException("候选缺少服务器 index");
        int index = optionIndex(option);
        return Integer.toString(index);
    }
    private static byte[] hexBytes(String hex) {
        if (hex.isEmpty() || (hex.length() & 1) != 0 || hex.length() > 8192) return null;
        byte[] bytes = new byte[hex.length() / 2];
        for (int i = 0; i < bytes.length; i++) {
            int high = Character.digit(hex.charAt(i * 2), 16), low = Character.digit(hex.charAt(i * 2 + 1), 16);
            if (high < 0 || low < 0) return null;
            bytes[i] = (byte) ((high << 4) | low);
        }
        return bytes;
    }
    private static String decodedName(JSONObject object, String hexKey, String fallbackKey) {
        String fallback = object.optString(fallbackKey, "");
        byte[] bytes = hexBytes(object.optString(hexKey, ""));
        if (bytes == null) return fallback;
        try { return decodeStrict(bytes, StandardCharsets.UTF_8); }
        catch (CharacterCodingException invalidUtf8) {
            try { return decodeStrict(bytes, Charset.forName("GB18030")); }
            catch (Exception invalidLegacyText) { return fallback; }
        }
    }
    private static String decodeStrict(byte[] bytes, Charset charset) throws CharacterCodingException {
        return charset.newDecoder().onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes)).toString();
    }
    private int optionIndex(JSONObject option) throws Exception {
        Object raw = option.get("index");
        if (!(raw instanceof Number)) throw new IllegalArgumentException("服务器候选 index 非数字");
        double value = ((Number) raw).doubleValue();
        if ((Double.isNaN(value) || Double.isInfinite(value)) || value < 0 || value > 100 || value != Math.rint(value)) throw new IllegalArgumentException("服务器候选 index 无效");
        return (int) value;
    }
    private String optionText(String key, int selected) {
        Spinner spinner = "baseOptions".equals(key) ? base : refine;
        return spinner.getSelectedItem() == null ? "" : spinner.getSelectedItem().toString();
    }
    private void setOptions(Spinner spinner, JSONArray options) {
        java.util.ArrayList<String> labels = new java.util.ArrayList<>();
        if (options != null) for (int i = 0; i < options.length(); i++) try {
            JSONObject option = options.getJSONObject(i); StringBuilder label = new StringBuilder("[").append(optionIndex(option)).append("] ").append(decodedName(option, "tagHex", "tag"));
            JSONArray attrs = option.optJSONArray("attrs"); if (attrs != null) for (int j = 0; j < attrs.length(); j++) { JSONObject a = attrs.getJSONObject(j); label.append(" · ").append(a.optString("name", a.optString("key"))).append(' ').append(a.optString("value", "")); }
            labels.add(label.toString());
        } catch (Exception ignored) { labels.add("候选数据无效"); }
        spinner.setAdapter(new ArrayAdapter<>(this, android.R.layout.simple_spinner_item, labels));
    }
    private void clearOptions() { if (base != null) setOptions(base, null); if (refine != null) setOptions(refine, null); if (preview != null) preview.setText("部位变化后候选已清空，请重新查询。"); }
    private void updateButtons() {
        boolean active = BUSY.get(); if (query == null) return;
        boolean connected = service != null && "connected".equals(service.getState()) && selectedGamePackage().equals(service.getPackage());
        query.setEnabled(connected && queryEnabled(active)); create.setEnabled(connected && !active && canCreate());
        giveBack.setEnabled(connected && !active && canReturn());
        boolean connectionBusy=service!=null&&("connecting".equals(service.getState())||"busy".equals(service.getState()));
        if(connect!=null) connect.setEnabled(!active && !connectionBusy && (service==null || !selectedGamePackage().equals(service.getPackage()) || !"connected".equals(service.getState())));
        if(disconnect!=null) disconnect.setEnabled(!active && service!=null && !"disconnected".equals(service.getState()));
        position.setEnabled(!active);
    }
    private void copyAsset(String name) throws Exception {
        File target = new File(getFilesDir(), name);
        try (InputStream in = getAssets().open(name); FileOutputStream out = new FileOutputStream(target)) { byte[] b = new byte[8192]; int n; while ((n = in.read(b)) >= 0) out.write(b, 0, n); out.getFD().sync(); }
        target.setReadable(true, true);
    }
    private static byte[] read(File file) throws Exception { try (InputStream in = new FileInputStream(file); ByteArrayOutputStream out = new ByteArrayOutputStream()) { byte[] b = new byte[8192]; int n; while ((n = in.read(b)) >= 0) out.write(b, 0, n); return out.toByteArray(); } }
    private static String shellQuote(String value) { return "'" + value.replace("'", "'\\''") + "'"; }
    private static String checkedDecimal(String v, String label) { if (!DECIMAL.matcher(v).matches()) throw new IllegalArgumentException("无效 " + label); return v; }
    private static String checkedHex(String v, String label) { if (!HEX.matcher(v).matches()) throw new IllegalArgumentException("无效 " + label); return v; }
    private void setStatus(String value) { if (status != null) status.setText(value); }
    @Override public ModuleStatus getModuleStatus() {
        String target = selectedGamePackage();
        String state = service == null ? "disconnected" : service.getState();
        String detail = service == null ? "连接服务未连接。" : service.getMessage();
        if (detail == null || detail.trim().isEmpty()) detail = "连接状态未提供详情。";
        ModuleStatus.State mapped;
        String label;
        if (BUSY.get() || "busy".equals(state)) { mapped = ModuleStatus.State.RUNNING; label = "操作处理中"; }
        else if ("connected".equals(state)) { mapped = ModuleStatus.State.RUNNING; label = "worker 已连接"; }
        else if ("connecting".equals(state)) { mapped = ModuleStatus.State.WAITING; label = "连接中"; }
        else if ("error".equals(state) || "blocked".equals(state)) { mapped = ModuleStatus.State.ERROR; label = "连接失败"; }
        else if ("disconnected".equals(state)) { mapped = ModuleStatus.State.STOPPED; label = "未连接"; }
        else { mapped = ModuleStatus.State.UNKNOWN; label = "状态未确认"; }
        return new ModuleStatus(mapped, label, target,
                detail + "；连接 worker 状态=" + state + "。角色/候选数据仅在本页查询后确认。",
                "连接状态只表示所选目标的本地 worker 连接情况，不代表已查询角色或候选；查看本页结果与提示后再操作。");
    }
    private String message(Exception e) { return e.getMessage() == null ? "操作失败" : e.getMessage(); }
    private String selectedGamePackage() { return com.xnas.wpe.suite.TargetGame.getSelectedPackage(this); }
    private Spinner spinner(String[] entries) { Spinner s = new Spinner(this); s.setAdapter(new ArrayAdapter<>(this, android.R.layout.simple_spinner_item, entries)); return s; }
    private TextView label(String value, int size) { TextView t = text(value, size, Color.WHITE); t.setPadding(0, dp(14), 0, dp(2)); return t; }
    private Button button(String title) { Button b = new Button(this); b.setText(title); b.setAllCaps(false); return b; }
    private TextView text(String value, int size, int color) { TextView t = new TextView(this); t.setText(value); t.setTextSize(size); t.setTextColor(color); return t; }
    private LinearLayout.LayoutParams params(int width, int height, int left, int top) { LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(width, height); p.setMargins(dp(left), dp(top), dp(left), 0); return p; }
    private int dp(int n) { return (int) (n * getResources().getDisplayMetrics().density + 0.5f); }
}





















