package com.xnas.wpe.anlei;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.net.Uri;
import android.provider.Settings;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.io.File;
import java.io.*;

public final class MainActivity extends Activity {
    private final Handler handler = new Handler();
    private TextView status;
    private final Runnable refresh = new Runnable() {
        @Override public void run() {
            if (status != null) {
                File f = new File(getFilesDir(), "status");
                try { status.setText("状态：" + (android.os.Build.VERSION.SDK_INT>=23&&!Settings.canDrawOverlays(MainActivity.this) ? "悬浮窗权限未开启" : (f.isFile() ? display(read(f)) : "未启动"))); }
                catch (Exception e) { status.setText("运行状态：读取状态失败"); }
                handler.postDelayed(this, 1000);
            }
        }
    };
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        LinearLayout root = new LinearLayout(this); root.setOrientation(LinearLayout.VERTICAL);
        int pad = Math.round(24 * getResources().getDisplayMetrics().density); root.setPadding(pad,pad,pad,pad);
        TextView title = new TextView(this); title.setText("暗雷遇敌助手"); title.setTextSize(20); root.addView(title);
        status = new TextView(this); root.addView(status);
        Button overlayPermission = new Button(this); overlayPermission.setText("开启悬浮窗权限"); overlayPermission.setOnClickListener(v -> startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:"+getPackageName())))); root.addView(overlayPermission);
        Button start = new Button(this); start.setText("启动"); start.setOnClickListener(v -> { Intent i=new Intent(this,AnleiService.class).setAction(AnleiService.START);if(android.os.Build.VERSION.SDK_INT>=26)startForegroundService(i);else startService(i); }); root.addView(start);
        Button stop = new Button(this); stop.setText("停止"); stop.setOnClickListener(v -> startService(new Intent(this, AnleiService.class).setAction(AnleiService.STOP))); root.addView(stop);
        setContentView(root);
    }
    @Override protected void onResume() { super.onResume(); if(android.os.Build.VERSION.SDK_INT<23||Settings.canDrawOverlays(this))startService(new Intent(this,AnleiService.class).setAction(AnleiService.OVERLAY));handler.post(refresh); }
    @Override protected void onPause() { handler.removeCallbacks(refresh); super.onPause(); }
    private static String read(File f) throws Exception { try(FileInputStream in=new FileInputStream(f);ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] b=new byte[4096];int n;while((n=in.read(b))>0)out.write(b,0,n);return out.toString("UTF-8");} }
    private static String display(String s) {
        if (s.contains("waiting_game")) return "等待游戏启动";
        if (s.contains("waiting_manager_or_local_role")) return "等待角色登录";
        String map = s.replaceAll("(?s).*map=([0-9]+).*", "$1");
        String mapSuffix = map.equals(s.trim()) || map.equals("0") ? "" : "（当前地图 " + map + "）";
        if (s.contains("running calls=")) {
            String sent = s.replaceAll("(?s).*calls=([0-9]+).*", "$1");
            return "持续运行：位置同步调用 " + sent + " 次" + mapSuffix;
        }
        if (s.contains("battle")) return "战斗中，暂停位置同步" + mapSuffix;
        if (s.contains("waiting_map")) return "等待地图准备" + mapSuffix;
        if (s.contains("waiting_role")) return "等待角色登录" + mapSuffix;
        if (s.contains("overlay_permission_required")) return "请开启悬浮窗权限后启动";
        if (s.contains("game_gone") || s.contains("identity_changed")) return "游戏进程变化，正在重新连接";
        if (s.contains("stopped") || s.contains("已停止")) return "已停止";
        if (s.contains("app lease expired")) return "运行租约过期，已停止";
        if (s.contains("error") || s.contains("fatal")) return "运行失败，请查看诊断日志";
        if (s.contains("waiting") || s.contains("stopping")) return "等待游戏状态" + mapSuffix;
        return "正在监控游戏";
    }
}
