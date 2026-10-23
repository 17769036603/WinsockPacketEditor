package com.xnas.wpe.anlei;

import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import java.io.File;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import com.xnas.wpe.suite.GlassSkin;
import com.xnas.wpe.suite.ModuleStatus;
import com.xnas.wpe.suite.ModuleStatusProvider;
import com.xnas.wpe.suite.TargetGame;
import com.xnas.wpe.suite.UiStatusFiles;

public final class MainActivity extends com.xnas.wpe.suite.ModuleActivity implements ModuleStatusProvider {
    private final Handler handler = new Handler();
    private TextView status;
    private final Runnable refresh = new Runnable() {
        @Override public void run() {
            if (status != null) {
                File f = new File(getFilesDir(), "status");
                String targetKey = TargetGame.getSelectedPackage(MainActivity.this);
                if (!TargetGame.supportsModule(TargetGame.ANLEI, targetKey)) {
                    status.setText("状态：此功能仅支持 DHXY");
                } else {
                    UiStatusFiles.Snapshot snapshot = UiStatusFiles.readForDisplay("anlei", f, targetKey);
                    status.setText("状态：" + (!snapshot.known ? "状态读取中，运行情况未确认"
                            : snapshot.exists ? display(snapshot.text) : "未启动"));
                }
                handler.postDelayed(this, 1000);
            }
        }
    };
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        ScrollView scroll = new ScrollView(this); scroll.setFillViewport(true);
        LinearLayout root = new LinearLayout(this); root.setOrientation(LinearLayout.VERTICAL);
        int padX = Math.round(GlassSkin.PADDING_H_DP * getResources().getDisplayMetrics().density);
        int padY = Math.round(GlassSkin.PADDING_V_DP * getResources().getDisplayMetrics().density);
        root.setPadding(padX, padY, padX, padY); root.setBackground(GlassSkin.page(this));
        status = new TextView(this); status.setTextColor(GlassSkin.TEXT); status.setTextSize(GlassSkin.TEXT_BODY_SP);
        int rowH = Math.round(GlassSkin.BUTTON_HEIGHT_DP * getResources().getDisplayMetrics().density);
        int compactH = Math.round(GlassSkin.BUTTON_HEIGHT_COMPACT_DP * getResources().getDisplayMetrics().density);
        Button start = new Button(this); start.setText("启动"); start.setTextSize(GlassSkin.TEXT_BUTTON_SP); GlassSkin.apply(start, GlassSkin.actionTinted(this, 8, GlassSkin.ACTION_START)); start.setTextColor(GlassSkin.actionText()); start.setOnClickListener(v -> { if (!TargetGame.supportsModule(TargetGame.ANLEI, TargetGame.getSelectedPackage(this))) { status.setText("此功能仅支持 DHXY"); return; } Intent i=new Intent(this,AnleiService.class).setAction(AnleiService.START);if(android.os.Build.VERSION.SDK_INT>=26)startForegroundService(i);else startService(i); }); android.view.View topSpace = new android.view.View(this); root.addView(topSpace, new LinearLayout.LayoutParams(-1, 0, 1f)); LinearLayout.LayoutParams lps = new LinearLayout.LayoutParams(-1, rowH); lps.setMargins(0, 0, 0, Math.round(8 * getResources().getDisplayMetrics().density)); root.addView(start, lps);
        Button stop = new Button(this); stop.setText("停止"); stop.setTextSize(GlassSkin.TEXT_BUTTON_COMPACT_SP); GlassSkin.apply(stop, GlassSkin.actionTinted(this, 8, GlassSkin.ACTION_STOP)); stop.setTextColor(GlassSkin.actionText()); stop.setOnClickListener(v -> startService(new Intent(this, AnleiService.class).setAction(AnleiService.STOP))); root.addView(stop, new LinearLayout.LayoutParams(-1, rowH)); android.view.View bottomSpace = new android.view.View(this); root.addView(bottomSpace, new LinearLayout.LayoutParams(-1, 0, 1f));
        scroll.addView(root, new ScrollView.LayoutParams(-1, -1));
        setContentView(scroll);
    }
    @Override protected void onResume() { super.onResume(); handler.post(refresh); }
    @Override protected void onPause() { handler.removeCallbacks(refresh); super.onPause(); }
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
        String selected = TargetGame.getSelectedPackage(this);
        String target = TargetGame.DHXY_PACKAGE;
        if (!TargetGame.supportsModule(TargetGame.ANLEI, selected))
            return new ModuleStatus(AnleiService.isActive() ? ModuleStatus.State.UNKNOWN : ModuleStatus.State.STOPPED,
                    AnleiService.isActive() ? "状态未确认" : "不支持", target,
                    AnleiService.isActive() ? "暗雷遇敌服务状态未确认。" : "此功能仅支持 DHXY。", "");
        File file = new File(getFilesDir(), "status");
        UiStatusFiles.Snapshot snapshot = UiStatusFiles.readForDisplay("anlei", file, selected);
        if (!snapshot.known || !snapshot.targetKey.equals(selected)) return ModuleStatus.unknown(target,
                "本地 worker 状态读取中或暂不可用；运行状态未确认。");
        if (!snapshot.exists) return new ModuleStatus(AnleiService.isActive() ? ModuleStatus.State.UNKNOWN : ModuleStatus.State.STOPPED, AnleiService.isActive() ? "状态未确认" : "未启动", target,
                "尚无本地 worker 状态文件。", "点击本功能页的启动按钮；目标游戏绑定状态未确认。");
        {
            String raw = snapshot.text;
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
        }
    }
}
