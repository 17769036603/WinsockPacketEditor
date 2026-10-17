package com.xnas.wpe.battleskip;

import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

import com.xnas.wpe.suite.BattleConflictGate;
import com.xnas.wpe.suite.GlassSkin;
import com.xnas.wpe.suite.ModuleActivity;
import com.xnas.wpe.suite.ModuleStatus;
import com.xnas.wpe.suite.ModuleStatusProvider;
import com.xnas.wpe.suite.TargetGame;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.nio.charset.StandardCharsets;

public final class MainActivity extends ModuleActivity implements ModuleStatusProvider {
    private final Handler handler = new Handler(Looper.getMainLooper());
    private TextView status;
    private long lastRecoveryRequest;
    private final Runnable refreshStatus = new Runnable() {
        @Override public void run() {
            if (status == null) return;
            File moduleDir = getFilesDir();
            String raw = currentStatus(moduleDir);
            boolean cleanStopped = raw.isEmpty() || BattleConflictGate.isClean(moduleDir.getParentFile(), "battleskip");
            status.setText(!TargetGame.supportsModule(TargetGame.BATTLE_SKIP,
                    TargetGame.getSelectedPackage(MainActivity.this)) && cleanStopped
                    ? "此功能仅支持 DHXY" : formatStatus(raw, cleanStopped));
            handler.postDelayed(this, 1000);
        }
    };

    @Override public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setFitsSystemWindows(true);
        int paddingX = Math.round(GlassSkin.PADDING_H_DP * getResources().getDisplayMetrics().density);
        int paddingY = Math.round(GlassSkin.PADDING_V_DP * getResources().getDisplayMetrics().density);
        layout.setPadding(paddingX, paddingY, paddingX, paddingY);
        layout.setBackgroundColor(GlassSkin.PAGE_BG);

        status = new TextView(this);
        status.setTextColor(android.graphics.Color.rgb(224, 239, 243));
        status.setTextSize(GlassSkin.TEXT_BODY_SP);
        status.setPadding(0, Math.round(12 * getResources().getDisplayMetrics().density), 0, 0);
        layout.addView(status);

        Button start = new Button(this);
        start.setText("启动快进");
        start.setTextSize(GlassSkin.TEXT_BUTTON_SP);
        GlassSkin.apply(start, GlassSkin.actionTinted(this, 8, GlassSkin.ACTION_START));
        start.setTextColor(GlassSkin.actionText());
        start.setOnClickListener(view -> {
            if (!TargetGame.supportsModule(TargetGame.BATTLE_SKIP, TargetGame.getSelectedPackage(this))) {
                status.setText("此功能仅支持 DHXY");
                return;
            }
            if (BattleSkipRecoveryService.isChecking()
                    || com.xnas.wpe.battleloop.BattleLoopService.isCheckingOldTask()) {
                status.setText("正在准备，请稍候。");
                return;
            }
            boolean oldLoop = BattleConflictGate.hasUnconfirmedLoopActivity(getFilesDir().getParentFile())
                    && !com.xnas.wpe.battleloop.BattleLoopService.isActive();
            boolean oldSkip = BattleConflictGate.hasUnconfirmedSkipActivity(getFilesDir().getParentFile());
            if (oldLoop || oldSkip) {
                long now = android.os.SystemClock.elapsedRealtime();
                if (lastRecoveryRequest != 0 && now - lastRecoveryRequest < 10000) return;
                lastRecoveryRequest = now;
                Intent check = oldLoop
                        ? new Intent(this, com.xnas.wpe.battleloop.BattleLoopService.class)
                            .setAction(com.xnas.wpe.battleloop.BattleLoopService.CHECK)
                        : new Intent(this, BattleSkipRecoveryService.class);
                try {
                    if (android.os.Build.VERSION.SDK_INT >= 26) startForegroundService(check);
                    else startService(check);
                    status.setText("正在确认上次任务是否退出；完成后请再次点击启动。");
                } catch (RuntimeException error) { status.setText("无法确认上次任务，暂未启动。"); }
                return;
            }
            BattleConflictGate.Decision decision = BattleConflictGate.reserve(
                    getFilesDir().getParentFile(), "battleskip");
            if (!decision.allowed) {
                status.setText("启动已阻止：" + decision.message);
                Toast.makeText(this, decision.message, Toast.LENGTH_LONG).show();
                return;
            }
            try {
                startActivity(new Intent().setClassName(this, "com.xnas.wpe.suite.BattleSkipStart"));
                status.setText("已发送启动请求；等待 worker 绑定游戏并显示运行状态。");
            } catch (RuntimeException error) {
                boolean aborted = BattleConflictGate.abortBeforeDispatch(
                        getFilesDir().getParentFile(), "battleskip");
                status.setText(aborted
                        ? "启动入口不可用；该次请求未派发，启动位已清理。"
                        : "启动入口不可用；状态未确认前不会允许另一战斗功能启动。");
            }
        });
        layout.addView(start, new LinearLayout.LayoutParams(
                -1,
                Math.round(GlassSkin.BUTTON_HEIGHT_DP * getResources().getDisplayMetrics().density)));

        Button stop = new Button(this);
        stop.setText("停止快进");
        stop.setTextSize(GlassSkin.TEXT_BUTTON_COMPACT_SP);
        GlassSkin.apply(stop, GlassSkin.actionTinted(this, 8, GlassSkin.ACTION_STOP));
        stop.setTextColor(GlassSkin.actionText());
        stop.setOnClickListener(view -> {
            try {
                startActivity(new Intent().setClassName(this, "com.xnas.wpe.suite.BattleSkipStop"));
                status.setText("已发送停止请求；请保持游戏在前台并等待 Hook 与绑定清理确认。");
            } catch (RuntimeException error) {
                status.setText("停止入口不可用；战斗功能保护位保持锁定。");
            }
        });
        layout.addView(stop, new LinearLayout.LayoutParams(
                -1,
                Math.round(GlassSkin.BUTTON_HEIGHT_COMPACT_DP * getResources().getDisplayMetrics().density)));
        ScrollView scroll = new ScrollView(this);
        scroll.addView(layout, new ScrollView.LayoutParams(-1, -2));
        setContentView(scroll);
    }

    @Override protected void onResume() {
        super.onResume();
        handler.removeCallbacks(refreshStatus);
        handler.post(refreshStatus);
    }

    @Override protected void onPause() {
        handler.removeCallbacks(refreshStatus);
        super.onPause();
    }

    private static String read(File file) {
        try (FileInputStream input = new FileInputStream(file);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[2048];
            int count;
            while ((count = input.read(buffer)) > 0) output.write(buffer, 0, count);
            return new String(output.toByteArray(), StandardCharsets.UTF_8);
        } catch (Exception ignored) { return ""; }
    }

    private static String currentStatus(File dir) {
        String actual = read(new File(dir, "status"));
        if (BattleSkipRecoveryService.isChecking()) return "state=checking";
        if (BattleConflictGate.isClean(dir.getParentFile(), "battleskip")) return actual;
        String recovery = read(new File(dir, "recovery-status"));
        if (!recovery.isEmpty()) return recovery;
        String gate = read(new File(dir, "gate-status"));
        return gate.isEmpty() ? actual : gate;
    }

    private static String formatStatus(String raw, boolean clean) {
        if (raw.isEmpty()) return "状态：未启动";
        if (raw.contains("state=conflict_wait")) {
            for (String line : raw.split("\\r?\\n")) {
                if (line.startsWith("message=")) return "启动受阻：" + line.substring(8);
            }
            return "启动受阻：战斗快进与战斗跳过的运行或清理状态尚未确认";
        }
        for (String line : raw.split("\\r?\\n")) {
            if (line.startsWith("message=")) return "状态：" + line.substring("message=".length());
            if (line.startsWith("state=")) {
                String state = line.substring("state=".length());
                if ("starting".equals(state)) return "状态：已预留启动位，等待 worker";
                if ("stopping".equals(state)) return "状态：正在清理；未确认前不会释放战斗功能启动位";
                if ("stopped".equals(state)) return clean ? "状态：已停止" : "状态：停止未确认";
                if ("checking".equals(state)) return "正在准备，请稍候。";
                if ("waiting_restart".equals(state)) return "请重启游戏后再点击启动。";
                if ("unconfirmed".equals(state)) return "无法确认上次任务是否退出，暂未启动。";
                return "状态：" + state;
            }
        }
        return "状态：" + raw.trim();
    }

    @Override public ModuleStatus getModuleStatus() {
        File dir = getFilesDir();
        String raw = currentStatus(dir);
        String selected = TargetGame.getSelectedPackage(this);
        if (raw.isEmpty()) return new ModuleStatus(ModuleStatus.State.STOPPED,
                TargetGame.supportsModule(TargetGame.BATTLE_SKIP, selected) ? "未启动" : "不支持",
                selected, TargetGame.supportsModule(TargetGame.BATTLE_SKIP, selected)
                        ? "尚无本地状态文件；游戏绑定未确认。" : "此功能仅支持 DHXY。",
                "");
        String state = null;
        for (String line : raw.split("\\r?\\n")) {
            if (line.startsWith("state=")) { state = line.substring("state=".length()).trim(); break; }
        }
        ModuleStatus.State mapped;
        String label;
        if ("stopped".equals(state)) {
            boolean clean = BattleConflictGate.isClean(dir.getParentFile(), "battleskip");
            mapped = clean ? ModuleStatus.State.STOPPED : ModuleStatus.State.UNKNOWN;
            label = clean ? "已停止" : "停止未确认";
        } else if ("stopping".equals(state)) { mapped = ModuleStatus.State.STOPPING; label = "正在清理"; }
        else if ("conflict_wait".equals(state)) {
            mapped = ModuleStatus.State.WAITING; label = "战斗功能互斥";
        }
        else if ("starting".equals(state) || "waiting_game".equals(state)
                || "waiting_role".equals(state)) {
            mapped = ModuleStatus.State.WAITING; label = "等待游戏绑定";
        } else if ("running".equals(state)) { mapped = ModuleStatus.State.RUNNING; label = "worker 运行中"; }
        else if ("checking".equals(state)) { mapped = ModuleStatus.State.WAITING; label = "正在准备"; }
        else if ("waiting_restart".equals(state)) { mapped = ModuleStatus.State.WAITING; label = "需重启游戏"; }
        else if ("error".equals(state)) { mapped = ModuleStatus.State.ERROR; label = "运行失败"; }
        else { mapped = ModuleStatus.State.UNKNOWN; label = "状态未确认"; }
        if (!TargetGame.supportsModule(TargetGame.BATTLE_SKIP, selected)
                && mapped == ModuleStatus.State.STOPPED) {
            return new ModuleStatus(ModuleStatus.State.STOPPED, "不支持", selected,
                    "此功能仅支持 DHXY。", "");
        }
        String nextStep = "本功能仅支持 DHXY；请在本页面明确启动或停止，状态不代表有效战斗帧。";
        if (mapped == ModuleStatus.State.STOPPING || mapped == ModuleStatus.State.UNKNOWN) {
            nextStep = "保持游戏在前台，等待 Hook 与绑定清理结果明确后再启动其他战斗功能。";
        } else if ("conflict_wait".equals(state)) {
            nextStep = "按上方阻塞说明处理；清理确认前，战斗跳过与战斗快进继续互斥。";
        }
        return new ModuleStatus(mapped, label, TargetGame.DHXY_PACKAGE,
                formatStatus(raw, BattleConflictGate.isClean(dir.getParentFile(), "battleskip"))
                        + "；worker/游戏绑定以状态文件报告为准。", nextStep);
    }
}
