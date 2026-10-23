package com.xnas.wpe.anlei;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.net.Uri;
import android.provider.Settings;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import java.io.File;
import java.io.*;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import com.xnas.wpe.suite.ModuleStatus;
import com.xnas.wpe.suite.ModuleStatusProvider;
import com.xnas.wpe.suite.TargetGame;

public final class MainActivity extends com.xnas.wpe.suite.ModuleActivity implements ModuleStatusProvider {
    private final Handler handler = new Handler();
    private TextView status;
    private final Runnable refresh = new Runnable() {
        @Override public void run() {
            if (status != null) {
                File f = new File(getFilesDir(), "status");
                try { status.setText("状态：" + (!TargetGame.supportsModule(TargetGame.ANLEI, TargetGame.getSelectedPackage(MainActivity.this)) ? "此功能仅支持 DHXY" : android.os.Build.VERSION.SDK_INT>=23&&!Settings.canDrawOverlays(MainActivity.this) ? "悬浮窗权限未开启" : (f.isFile() ? display(read(f)) : "未启动"))); }
                catch (Exception e) { status.setText("运行状态：读取状态失败"); }
                handler.postDelayed(this, 1000);
            }
        }
    };
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        ScrollView scroll = new ScrollView(this);
        LinearLayout root = new LinearLayout(this); root.setOrientation(LinearLayout.VERTICAL);
        int pad = Math.round(24 * getResources().getDisplayMetrics().density); root.setPadding(pad,pad,pad,pad);
        status = new TextView(this); root.addView(status);
        Button overlayPermission = new Button(this); overlayPermission.setText("开启悬浮窗权限"); overlayPermission.setOnClickListener(v -> startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:"+getPackageName())))); root.addView(overlayPermission);
        Button start = new Button(this); start.setText("启动"); start.setOnClickListener(v -> { if (!TargetGame.supportsModule(TargetGame.ANLEI, TargetGame.getSelectedPackage(this))) { status.setText("此功能仅支持 DHXY"); return; } Intent i=new Intent(this,AnleiService.class).setAction(AnleiService.START);if(android.os.Build.VERSION.SDK_INT>=26)startForegroundService(i);else startService(i); }); root.addView(start);
        Button stop = new Button(this); stop.setText("停止"); stop.setOnClickListener(v -> startService(new Intent(this, AnleiService.class).setAction(AnleiService.STOP))); root.addView(stop);
        scroll.addView(root, new ScrollView.LayoutParams(-1, -2));
        setContentView(scroll);
    }
    @Override protected void onResume() { super.onResume(); if(TargetGame.supportsModule(TargetGame.ANLEI, TargetGame.getSelectedPackage(this))&&(android.os.Build.VERSION.SDK_INT<23||Settings.canDrawOverlays(this)))startService(new Intent(this,AnleiService.class).setAction(AnleiService.OVERLAY));handler.post(refresh); }
    @Override protected void onPause() { handler.removeCallbacks(refresh); super.onPause(); }
    private static String read(File f) throws Exception { try(FileInputStream in=new FileInputStream(f);ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] b=new byte[4096];int n;while((n=in.read(b))>0)out.write(b,0,n);return out.toString("UTF-8");} }
    private static String display(String s) {
        if (s.contains("waiting_game")) return "等待游戏启动";
        if (s.contains("waiting_manager_or_local_role")) return "等待角色登录";
        Matcher mapMatch = Pattern.compile("(?:^|\\s)map=([0-9]+)(?:\\s|$)").matcher(s);
        String mapSuffix = mapMatch.find() && !mapMatch.group(1).equals("0")
                ? "（当前地图 " + mapMatch.group(1) + "）" : "";
        if (s.contains("running calls=")) {
            String sent = s.replaceAll("(?s).*calls=([0-9]+).*", "$1");
            return "持续运行：位置同步调用 " + sent + " 次" + mapSuffix;
        }
        if (s.contains("battle")) return "战斗中，暂停位置同步" + mapSuffix;
        if (s.contains("waiting_map")) return "等待地图准备" + mapSuffix;
        if (s.contains("waiting_role")) return "等待角色登录" + mapSuffix;
        if (s.contains("overlay_permission_required")) return "请开启悬浮窗权限后启动";
        if (s.contains("identity_unconfirmed")) return "游戏进程身份未确认，等待旧会话清理";
        if (s.contains("game_gone") || s.contains("identity_changed")) return "游戏进程变化，正在重新连接";
        if (WorkerStatus.isStopped(s)) return "已停止";
        if (s.contains("stopping") || s.contains("停止待确认")) return "正在停止";
        if (s.contains("app lease expired")) return "运行租约过期，已停止";
        if (s.contains("error") || s.contains("fatal")) return "运行失败，请查看诊断日志";
        if (s.contains("waiting") || s.contains("stopping")) return "等待游戏状态" + mapSuffix;
        return "正在监控游戏";
    }

    @Override public ModuleStatus getModuleStatus() {
        String target = TargetGame.DHXY_PACKAGE;
        if (!TargetGame.supportsModule(TargetGame.ANLEI, TargetGame.getSelectedPackage(this)))
            return new ModuleStatus(AnleiService.isActive() ? ModuleStatus.State.UNKNOWN : ModuleStatus.State.STOPPED,
                    AnleiService.isActive() ? "状态未确认" : "不支持", target,
                    AnleiService.isActive() ? "暗雷遇敌服务状态未确认。" : "此功能仅支持 DHXY。", "");
        if (android.os.Build.VERSION.SDK_INT >= 23 && !Settings.canDrawOverlays(this))
            return new ModuleStatus(ModuleStatus.State.WAITING, "等待权限", target,
                    "暗雷助手需要悬浮窗权限；当前运行状态未确认。", "授予悬浮窗权限后手动启动。\n权限就绪不代表游戏已绑定。");
        File file = new File(getFilesDir(), "status");
        if (!file.isFile()) return new ModuleStatus(AnleiService.isActive() ? ModuleStatus.State.UNKNOWN : ModuleStatus.State.STOPPED, AnleiService.isActive() ? "状态未确认" : "未启动", target,
                "尚无本地 worker 状态文件。", "点击本功能页的启动按钮；目标游戏绑定状态未确认。");
        try {
            String raw = read(file);
            String text = display(raw);
            ModuleStatus.State state = WorkerStatus.isStopped(raw)
                    ? ModuleStatus.State.STOPPED
                    : raw.contains("error") || raw.contains("fatal") || raw.contains("启动失败")
                    ? ModuleStatus.State.ERROR
                    : raw.contains("stopping") || raw.contains("停止待确认")
                    ? ModuleStatus.State.STOPPING
                    : raw.contains("identity_unconfirmed") || raw.contains("waiting") || raw.contains("等待") || raw.contains("重新连接")
                    ? ModuleStatus.State.WAITING
                    : raw.contains("running calls=") ? ModuleStatus.State.RUNNING : ModuleStatus.State.UNKNOWN;
            if (AnleiService.isActive() && state == ModuleStatus.State.STOPPED) state = ModuleStatus.State.UNKNOWN;
            if (!AnleiService.isActive() && state != ModuleStatus.State.STOPPED && state != ModuleStatus.State.ERROR) {
                state = ModuleStatus.State.UNKNOWN;
                text = "状态未确认";
            }
            return new ModuleStatus(state, text, target,
                    "本地状态：" + text + "。进程/角色绑定未单独确认。",
                    state == ModuleStatus.State.STOPPED ? "点击启动以运行；运行期间仍需查看页面状态。"
                            : "暗雷遇敌仅针对 DHXY；此状态不证明角色或地图数据已绑定。");
        } catch (Exception e) {
            return ModuleStatus.unknown(target, "读取本地 worker 状态失败；运行状态未确认。");
        }
    }
}
