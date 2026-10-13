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
    private final Runnable refreshStatus = new Runnable() {
        @Override public void run() {
            if (status == null) return;
            File moduleDir = getFilesDir();
            String gate = read(new File(moduleDir, "gate-status"));
            String raw = gate.isEmpty() ? read(new File(moduleDir, "status")) : gate;
            boolean cleanStopped = raw.isEmpty() || raw.contains("state=stopped")
                    && raw.contains("hooksRemoved=true") && raw.contains("bindingCleared=true");
            status.setText(!TargetGame.supportsModule(TargetGame.BATTLE_SKIP,
                    TargetGame.getSelectedPackage(MainActivity.this)) && cleanStopped
                    ? "此功能仅支持 DHXY" : formatStatus(raw));
            handler.postDelayed(this, 1000);
        }
    };

    @Override public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setFitsSystemWindows(true);
        int padding = Math.round(24 * getResources().getDisplayMetrics().density);
        layout.setPadding(padding, padding, padding, padding);

        status = new TextView(this);
        status.setTextColor(android.graphics.Color.rgb(224, 239, 243));
        status.setPadding(0, Math.round(12 * getResources().getDisplayMetrics().density), 0, 0);
        layout.addView(status);

        Button start = new Button(this);
        start.setText("启动快进");
        start.setOnClickListener(view -> {
            if (!TargetGame.supportsModule(TargetGame.BATTLE_SKIP, TargetGame.getSelectedPackage(this))) {
                status.setText("此功能仅支持 DHXY");
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
        layout.addView(start);

        Button stop = new Button(this);
        stop.setText("停止快进");
        stop.setOnClickListener(view -> {
            try {
                startActivity(new Intent().setClassName(this, "com.xnas.wpe.suite.BattleSkipStop"));
                status.setText("已发送停止请求；请保持游戏在前台并等待 Hook 与绑定清理确认。");
            } catch (RuntimeException error) {
                status.setText("停止入口不可用；战斗功能保护位保持锁定。");
            }
        });
        layout.addView(stop);
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

    private static String formatStatus(String raw) {
        if (raw.isEmpty()) return "状态：未启动";
        if (raw.contains("state=conflict_wait")) {
            for (String line : raw.split("\\r?\\n")) {
                if (line.startsWith("message=")) return "启动受阻：" + line.substring(8);
            }
            return "启动受阻：等待另一战斗功能退出或完成旧任务检查";
        }
        for (String line : raw.split("\\r?\\n")) {
            if (line.startsWith("message=")) return "状态：" + line.substring("message=".length());
            if (line.startsWith("state=")) {
                String state = line.substring("state=".length());
                if ("starting".equals(state)) return "状态：已预留启动位，等待 worker";
                if ("stopping".equals(state)) return "状态：正在清理；未确认前不会释放战斗功能启动位";
                if ("stopped".equals(state)) return "状态：" + (raw.contains("hooksRemoved=true")
                        && raw.contains("bindingCleared=true") ? "已停止，Hook 与绑定清理已确认" : "停止状态未确认");
                return "状态：" + state;
            }
        }
        return "状态：" + raw.trim();
    }

    @Override public ModuleStatus getModuleStatus() {
        File dir = getFilesDir();
        File gateFile = new File(dir, "gate-status");
        File statusFile = new File(dir, "status");
        String gate = read(gateFile);
        String raw = !gate.isEmpty() ? gate : read(statusFile);
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
            boolean clean = raw.contains("hooksRemoved=true") && raw.contains("bindingCleared=true");
            mapped = clean ? ModuleStatus.State.STOPPED : ModuleStatus.State.UNKNOWN;
            label = clean ? "已停止" : "停止未确认";
        } else if ("stopping".equals(state)) { mapped = ModuleStatus.State.STOPPING; label = "正在清理"; }
        else if ("conflict_wait".equals(state)) {
            mapped = ModuleStatus.State.WAITING; label = "等待旧任务检查";
        }
        else if ("starting".equals(state) || "waiting_game".equals(state)
                || "waiting_role".equals(state)) {
            mapped = ModuleStatus.State.WAITING; label = "等待游戏绑定";
        } else if ("running".equals(state)) { mapped = ModuleStatus.State.RUNNING; label = "worker 运行中"; }
        else if ("error".equals(state)) { mapped = ModuleStatus.State.ERROR; label = "运行失败"; }
        else { mapped = ModuleStatus.State.UNKNOWN; label = "状态未确认"; }
        if (!TargetGame.supportsModule(TargetGame.BATTLE_SKIP, selected)
                && mapped == ModuleStatus.State.STOPPED) {
            return new ModuleStatus(ModuleStatus.State.STOPPED, "不支持", selected,
                    "此功能仅支持 DHXY。", "");
        }
        return new ModuleStatus(mapped, label, TargetGame.DHXY_PACKAGE,
                formatStatus(raw) + "；worker/游戏绑定以状态文件报告为准。",
                mapped == ModuleStatus.State.STOPPING || mapped == ModuleStatus.State.UNKNOWN
                        ? "保持游戏在前台，等待 Hook 与绑定清理结果明确后再启动其他战斗功能。"
                        : "本功能仅支持 DHXY；请在本页面明确启动或停止，状态不代表有效战斗帧。");
    }
}
