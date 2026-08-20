package com.xnas.wpe.mobile;

import android.annotation.SuppressLint;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.text.InputType;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.RadioButton;
import android.widget.RadioGroup;
import android.widget.ScrollView;
import android.widget.TextView;

import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.UUID;

public final class MainActivity extends android.app.Activity implements SyncCoordinator.Listener {
    private SyncCoordinator coordinator;
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final Runnable runtimePollingTask = () -> {
        if (coordinator != null) {
            if (!OverlayService.isRunning()) {
                updateOverlayButtonState();
                maybeAutoStartOverlay();
            }
            coordinator.refreshRuntime();
        }
    };

    private EditText endpointInput;
    private TextView connectionStatus;
    private TextView syncStatus;
    private Button overlayButton;
    private boolean activityStarted;
    private boolean destroyed;
    private boolean overlayReceiverRegistered;
    private boolean awaitingOverlayPermission;
    private boolean actionPending;
    private boolean automaticStartup = true;
    private boolean setupPageHidden = true;
    private boolean overlayUiReady;
    private View setupContent;
    private SyncModels.Module activeOverlayModule = SyncModels.Module.SEND;
    private final List<Intent> deferredOverlayCommands = new ArrayList<>();

    private final ModuleUi sendUi = new ModuleUi(SyncModels.Module.SEND, "发送");
    private final ModuleUi progressionUi = new ModuleUi(SyncModels.Module.PROGRESSION, "递进");
    private final ModuleUi assistantUi = new ModuleUi(SyncModels.Module.ASSISTANT, "助手");

    private final BroadcastReceiver overlayActionReceiver = new BroadcastReceiver() {
        @Override
        public void onReceive(Context context, Intent intent) {
            if (intent == null || intent.getAction() == null || coordinator == null) {
                return;
            }
            if (!overlayUiReady) {
                // Do not claim a command until the Activity can execute it.
                // The service keeps the unclaimed copy in its short-lived
                // queue, so an Activity recreation cannot lose the command.
                while (deferredOverlayCommands.size() >= 8) {
                    deferredOverlayCommands.remove(0);
                }
                deferredOverlayCommands.add(new Intent(intent));
                return;
            }
            if (!OverlayService.claimCommand(
                    intent.getStringExtra(OverlayService.EXTRA_COMMAND_ID))) {
                return;
            }
            handleOverlayCommand(intent);
        }
    };

    private void handleOverlayCommand(Intent intent) {
        if (intent == null || intent.getAction() == null || coordinator == null) {
            return;
        }
        String action = intent.getAction();
        if (OverlayService.ACTION_START.equals(action)) {
            activeOverlayModule = moduleFrom(intent.getStringExtra(OverlayService.EXTRA_MODULE));
            executeAction(moduleUi(activeOverlayModule), SyncCoordinator.Action.START,
                    parseUuid(intent.getStringExtra(OverlayService.EXTRA_PRESET_ID)));
        } else if (OverlayService.ACTION_PAUSE.equals(action)) {
            activeOverlayModule = moduleFrom(intent.getStringExtra(OverlayService.EXTRA_MODULE));
            executeAction(moduleUi(activeOverlayModule), SyncCoordinator.Action.PAUSE, null);
        } else if (OverlayService.ACTION_STOP.equals(action)) {
            activeOverlayModule = moduleFrom(intent.getStringExtra(OverlayService.EXTRA_MODULE));
            executeAction(moduleUi(activeOverlayModule), SyncCoordinator.Action.STOP, null);
        } else if (OverlayService.ACTION_SELECT_MODULE.equals(action)) {
            activeOverlayModule = moduleFrom(intent.getStringExtra(OverlayService.EXTRA_MODULE));
            updateOverlayPanel();
        } else if (OverlayService.ACTION_SELECT_PRESET.equals(action)) {
            SyncModels.Module module = moduleFrom(intent.getStringExtra(OverlayService.EXTRA_MODULE));
            selectPreset(moduleUi(module), parseUuid(intent.getStringExtra(OverlayService.EXTRA_PRESET_ID)));
            activeOverlayModule = module;
            updateOverlayPanel();
        } else if (OverlayService.ACTION_REFRESH.equals(action)) {
            coordinator.manualUpdate();
        } else if (OverlayService.ACTION_OPEN_APP.equals(action)) {
            Intent launch = new Intent(MainActivity.this, MainActivity.class)
                    .addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP);
            startActivity(launch);
        }
    }

    private void drainOverlayCommands() {
        List<Intent> queued = OverlayService.drainPendingCommands();
        for (Intent command : queued) {
            if (command == null || !OverlayService.claimCommand(
                    command.getStringExtra(OverlayService.EXTRA_COMMAND_ID))) {
                continue;
            }
            handleOverlayCommand(command);
        }
        if (!deferredOverlayCommands.isEmpty()) {
            List<Intent> deferred = new ArrayList<>(deferredOverlayCommands);
            deferredOverlayCommands.clear();
            for (Intent command : deferred) {
                if (command == null || !OverlayService.claimCommand(
                        command.getStringExtra(OverlayService.EXTRA_COMMAND_ID))) {
                    continue;
                }
                handleOverlayCommand(command);
            }
        }
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        destroyed = false;
        coordinator = SyncCoordinator.obtain(this, this);
        buildUi();
        hideSetupPage();
        registerOverlayReceiver();
        coordinator.loadSavedConnection();
        // Snapshot/runtime replay is posted by the coordinator. Process
        // queued overlay commands only after that replay has populated the UI.
        mainHandler.post(() -> {
            if (destroyed) {
                return;
            }
            overlayUiReady = true;
            drainOverlayCommands();
        });
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        hideSetupPage();
        maybeAutoStartOverlay();
    }

    @Override
    protected void onStart() {
        super.onStart();
        activityStarted = true;
        hideSetupPage();
        updateOverlayButtonState();
        scheduleRuntimePolling();
        maybeAutoStartOverlay();
    }

    @Override
    protected void onResume() {
        super.onResume();
        hideSetupPage();
        if (awaitingOverlayPermission) {
            awaitingOverlayPermission = false;
            if (Settings.canDrawOverlays(this)) {
                maybeAutoStartOverlay();
                return;
            }
        }
        updateOverlayButtonState();
        maybeAutoStartOverlay();
    }

    @Override
    protected void onStop() {
        activityStarted = false;
        mainHandler.removeCallbacks(runtimePollingTask);
        if (OverlayService.isRunning()) {
            scheduleRuntimePolling();
        }
        super.onStop();
    }

    @Override
    protected void onDestroy() {
        destroyed = true;
        mainHandler.removeCallbacks(runtimePollingTask);
        unregisterOverlayReceiver();
        overlayUiReady = false;
        deferredOverlayCommands.clear();
        if (coordinator != null) {
            if (OverlayService.isRunning()) {
                coordinator.detachListener(this);
            } else {
                coordinator.shutdown();
            }
        }
        super.onDestroy();
    }

    private void buildUi() {
        ScrollView scrollView = new ScrollView(this);
        scrollView.setFillViewport(true);
        scrollView.setBackgroundColor(Color.rgb(248, 250, 252));
        setupContent = scrollView;
        if (setupPageHidden) {
            setupContent.setVisibility(View.GONE);
        }

        LinearLayout content = new LinearLayout(this);
        content.setOrientation(LinearLayout.VERTICAL);
        content.setPadding(dp(16), dp(18), dp(16), dp(24));
        scrollView.addView(content, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        setContentView(scrollView);

        TextView title = textView(getString(R.string.app_name), 24, Color.rgb(15, 23, 42));
        title.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        content.addView(title, marginParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 0, 0, 8));
        content.addView(textView("电脑端是唯一预设源；这里仅同步、选择和控制。", 13,
                Color.rgb(71, 85, 105)), marginParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 0, 0, 14));

        LinearLayout connectionCard = card();
        TextView connectionTitle = textView("电脑端连接", 17, Color.rgb(15, 23, 42));
        connectionTitle.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        connectionCard.addView(connectionTitle, marginParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 0, 0, 8));

        endpointInput = editText("地址，例如 https://192.168.0.100:89/",
                InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        endpointInput.setText(coordinator == null ? SyncCoordinator.DEFAULT_ENDPOINT : coordinatorEndpoint());
        connectionCard.addView(endpointInput, marginParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 0, 0, 6));

        LinearLayout connectionActions = new LinearLayout(this);
        connectionActions.setOrientation(LinearLayout.HORIZONTAL);
        Button connect = actionButton("连接并同步", Color.rgb(37, 99, 235));
        connect.setContentDescription("连接电脑端并同步预设");
        connect.setOnClickListener(v -> connectAndSync());
        connectionActions.addView(connect, weightParams(0, 1, 0, 0, 6));
        Button refresh = actionButton("更新预设", Color.rgb(51, 65, 85));
        refresh.setContentDescription("手动更新完整预设快照");
        refresh.setOnClickListener(v -> coordinator.manualUpdate());
        connectionActions.addView(refresh, weightParams(0, 1, 0, 0, 0));
        connectionCard.addView(connectionActions, marginParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 0, 0, 8));

        overlayButton = actionButton("开启悬浮按钮", Color.rgb(15, 118, 110));
        overlayButton.setContentDescription("开启或关闭悬浮按钮");
        overlayButton.setOnClickListener(v -> toggleOverlay());
        connectionCard.addView(overlayButton, marginParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 0, 0, 5));
        connectionStatus = textView("未连接", 13, Color.rgb(100, 116, 139));
        connectionCard.addView(connectionStatus, marginParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 0, 0, 3));
        syncStatus = textView("预设尚未同步", 12, Color.rgb(100, 116, 139));
        connectionCard.addView(syncStatus, marginParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 0, 0, 0));
        content.addView(connectionCard, marginParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 0, 0, 12));

        addModule(content, sendUi);
        addModule(content, progressionUi);
        addModule(content, assistantUi);
    }

    private void addModule(LinearLayout parent, ModuleUi ui) {
        LinearLayout moduleCard = card();
        TextView heading = textView(ui.title, 18, Color.rgb(15, 23, 42));
        heading.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        moduleCard.addView(heading, marginParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 0, 0, 5));

        ui.group = new RadioGroup(this);
        ui.group.setOrientation(RadioGroup.VERTICAL);
        ui.group.setOnCheckedChangeListener((group, checkedId) -> {
            if (ui.applying) {
                return;
            }
            View checked = checkedId == -1 ? null : group.findViewById(checkedId);
            ui.selected = checked == null ? null : (SyncModels.Preset) checked.getTag();
            if (ui.selected != null) {
                coordinator.saveSelection(ui.module, ui.selected.id);
            }
            updateModule(ui);
            updateOverlayPanel();
        });
        moduleCard.addView(ui.group, marginParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 0, 0, 4));

        ui.detail = textView("暂无选中预设", 12, Color.rgb(71, 85, 105));
        ui.detail.setMaxLines(2);
        ui.detail.setVisibility(View.GONE);
        moduleCard.addView(ui.detail, marginParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 0, 0, 4));
        ui.runtimeView = textView("运行状态：未知", 12, Color.rgb(71, 85, 105));
        moduleCard.addView(ui.runtimeView, marginParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 0, 0, 6));

        LinearLayout actions = new LinearLayout(this);
        actions.setOrientation(LinearLayout.HORIZONTAL);
        ui.startButton = actionButton("开始", Color.rgb(37, 99, 235));
        ui.startButton.setContentDescription(ui.title + "开始或恢复");
        ui.startButton.setOnClickListener(v -> executeAction(ui, SyncCoordinator.Action.START));
        actions.addView(ui.startButton, weightParams(0, 1, 0, 0, 5));
        ui.pauseButton = actionButton("暂停", Color.rgb(180, 83, 9));
        ui.pauseButton.setContentDescription(ui.title + "暂停");
        ui.pauseButton.setOnClickListener(v -> executeAction(ui, SyncCoordinator.Action.PAUSE));
        actions.addView(ui.pauseButton, weightParams(0, 1, 0, 0, 5));
        ui.stopButton = actionButton("停止", Color.rgb(71, 85, 105));
        ui.stopButton.setContentDescription(ui.title + "停止");
        ui.stopButton.setOnClickListener(v -> executeAction(ui, SyncCoordinator.Action.STOP));
        actions.addView(ui.stopButton, weightParams(0, 1, 0, 0, 0));
        moduleCard.addView(actions, marginParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 0, 0, 0));
        parent.addView(moduleCard, marginParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 0, 0, 12));
    }

    private void connectAndSync() {
        String endpoint = endpointInput.getText().toString().trim();
        if (endpoint.isEmpty()) {
            setConnectionError("请填写电脑端地址");
            return;
        }
        coordinator.connectFromSavedFields(endpoint);
        automaticStartup = true;
        maybeAutoStartOverlay();
    }

    private void executeAction(ModuleUi ui, SyncCoordinator.Action action) {
        executeAction(ui, action, null);
    }

    private void executeAction(ModuleUi ui, SyncCoordinator.Action action, UUID requestedPresetId) {
        if (ui == null || coordinator == null) {
            return;
        }
        SyncModels.RuntimeStatus current = coordinator.getRuntime().forModule(ui.module);
        UUID id = requestedPresetId != null ? requestedPresetId
                : ui.selected == null ? null : ui.selected.id;
        if (action != SyncCoordinator.Action.START
                && current.isBusy() && current.presetId != null) {
            id = current.presetId;
        }
        coordinator.action(ui.module, id, action);
    }

    @SuppressLint("UnspecifiedRegisterReceiverFlag")
    private void registerOverlayReceiver() {
        if (overlayReceiverRegistered) {
            return;
        }
        IntentFilter filter = new IntentFilter();
        filter.addAction(OverlayService.ACTION_START);
        filter.addAction(OverlayService.ACTION_PAUSE);
        filter.addAction(OverlayService.ACTION_STOP);
        filter.addAction(OverlayService.ACTION_SELECT_MODULE);
        filter.addAction(OverlayService.ACTION_SELECT_PRESET);
        filter.addAction(OverlayService.ACTION_REFRESH);
        filter.addAction(OverlayService.ACTION_OPEN_APP);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            registerReceiver(overlayActionReceiver, filter, Context.RECEIVER_NOT_EXPORTED);
        } else {
            registerReceiver(overlayActionReceiver, filter,
                    OverlayService.INTERNAL_COMMAND_PERMISSION, mainHandler);
        }
        overlayReceiverRegistered = true;
    }

    private void unregisterOverlayReceiver() {
        if (!overlayReceiverRegistered) {
            return;
        }
        unregisterReceiver(overlayActionReceiver);
        overlayReceiverRegistered = false;
    }

    private void toggleOverlay() {
        if (OverlayService.isRunning()) {
            // An explicit close wins over the automatic startup requested by
            // the last successful connection for the rest of this Activity.
            automaticStartup = false;
            stopService(new Intent(this, OverlayService.class));
            setSyncStatus("", false);
            mainHandler.postDelayed(this::updateOverlayButtonState, 350L);
            return;
        }
        if (!Settings.canDrawOverlays(this)) {
            setSyncStatus("请先允许 " + getString(R.string.app_name)
                    + " 显示在其他应用上层", true);
            awaitingOverlayPermission = true;
            try {
                startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                        Uri.parse("package:" + getPackageName())));
            } catch (RuntimeException ex) {
                awaitingOverlayPermission = false;
                setSyncStatus("无法打开悬浮窗权限设置，请手动开启", true);
            }
            return;
        }
        startOverlayService();
    }

    private void maybeAutoStartOverlay() {
        if (!automaticStartup || destroyed) {
            return;
        }
        if (!canDrawOverlays()) {
            if (!awaitingOverlayPermission) {
                awaitingOverlayPermission = true;
                try {
                    startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                            Uri.parse("package:" + getPackageName())));
                } catch (RuntimeException ex) {
                    awaitingOverlayPermission = false;
                    setSyncStatus("无法打开悬浮窗权限设置，请手动开启", true);
                }
            }
            return;
        }
        if (!OverlayService.isRunning()) {
            startOverlayService();
        }
        moveTaskToBack(true);
    }

    private void startOverlayService() {
        try {
            Intent serviceIntent = new Intent(this, OverlayService.class);
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                startForegroundService(serviceIntent);
            } else {
                startService(serviceIntent);
            }
            setSyncStatus("悬浮按钮正在启动", false);
            mainHandler.postDelayed(this::updateOverlayButtonState, 350L);
        } catch (RuntimeException ex) {
            setSyncStatus("悬浮按钮启动失败", true);
        }
    }

    private boolean canDrawOverlays() {
        return Settings.canDrawOverlays(this);
    }

    private void hideSetupPage() {
        setupPageHidden = true;
        if (setupContent != null) {
            setupContent.setVisibility(View.GONE);
        }
    }

    private void updateOverlayButtonState() {
        if (overlayButton == null) {
            return;
        }
        if (OverlayService.isRunning()) {
            if (syncStatus != null && "悬浮按钮正在启动".contentEquals(syncStatus.getText())) {
                setSyncStatus("", false);
            }
            overlayButton.setText("关闭悬浮按钮");
            overlayButton.setBackground(buttonBackground(Color.rgb(71, 85, 105)));
        } else {
            overlayButton.setText(!Settings.canDrawOverlays(this)
                    ? "授权并开启悬浮按钮" : "开启悬浮按钮");
            overlayButton.setBackground(buttonBackground(Color.rgb(15, 118, 110)));
        }
    }

    @Override
    public void onSnapshotChanged(SyncModels.Snapshot snapshot, SyncModels.SyncState state) {
        setModuleItems(sendUi, snapshot == null ? null : snapshot.send);
        setModuleItems(progressionUi, snapshot == null ? null : snapshot.progression);
        setModuleItems(assistantUi, snapshot == null ? null : snapshot.assistant);
        updateOverlayPanel();
    }

    @Override
    public void onRuntimeChanged(SyncModels.RuntimeBundle runtime) {
        sendUi.runtime = runtime.send;
        progressionUi.runtime = runtime.progression;
        assistantUi.runtime = runtime.assistant;
        updateModule(sendUi);
        updateModule(progressionUi);
        updateModule(assistantUi);
        updateOverlayPanel();
        scheduleRuntimePolling();
    }

    @Override
    public void onStateChanged(SyncModels.SyncState state, String message) {
        boolean error = state == SyncModels.SyncState.ERROR;
        setSyncStatus(message, error);
        connectionStatus.setText(state == SyncModels.SyncState.ERROR ? "连接/同步异常"
                : String.format(Locale.ROOT, "状态：%s", state.name()));
        connectionStatus.setTextColor(error ? Color.rgb(185, 28, 28) : Color.rgb(71, 85, 105));
        updateOverlayPanel();
        maybeAutoStartOverlay();
    }

    @Override
    public void onActionPending(boolean pending) {
        actionPending = pending;
        OverlayService.updateActionPending(pending);
        updateModule(sendUi);
        updateModule(progressionUi);
        updateModule(assistantUi);
        updateOverlayPanel();
    }

    private void setModuleItems(ModuleUi ui, List<SyncModels.Preset> values) {
        ui.applying = true;
        ui.items = values == null ? new ArrayList<>() : new ArrayList<>(values);
        UUID preferred = coordinator.getProfile().selected(ui.module);
        ui.selected = null;
        ui.group.removeAllViews();
        if (ui.items.isEmpty()) {
            TextView empty = textView("暂无预设", 13, Color.rgb(100, 116, 139));
            ui.group.addView(empty, new RadioGroup.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, dp(38)));
            // A null list is a transient no-snapshot state during profile
            // switching or a failed refresh. Preserve the profile's saved
            // selection so a later valid snapshot can restore it. Only an
            // actual empty category in a valid snapshot clears the selection.
            if (values != null) {
                coordinator.saveSelection(ui.module, null);
            }
        } else {
            RadioButton preferredButton = null;
            for (SyncModels.Preset item : ui.items) {
                RadioButton option = new RadioButton(this);
                option.setId(View.generateViewId());
                option.setTag(item);
                option.setText(String.format(Locale.ROOT, "%s  ·  %s",
                        item.displayName(), item.groupName));
                option.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
                option.setTextColor(Color.rgb(30, 41, 59));
                option.setPadding(0, dp(5), 0, dp(5));
                ui.group.addView(option, new RadioGroup.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
                if (preferred != null && preferred.equals(item.id)) {
                    preferredButton = option;
                }
            }
            RadioButton first = (RadioButton) ui.group.getChildAt(0);
            RadioButton chosen = preferredButton == null ? first : preferredButton;
            chosen.setChecked(true);
            ui.selected = (SyncModels.Preset) chosen.getTag();
            if (preferred == null || preferredButton == null) {
                coordinator.saveSelection(ui.module, ui.selected.id);
            }
        }
        ui.applying = false;
        updateModule(ui);
    }

    private void selectPreset(ModuleUi ui, UUID id) {
        if (ui == null || id == null || ui.group == null || actionPending || ui.runtime.isBusy()) {
            return;
        }
        for (int i = 0; i < ui.group.getChildCount(); i++) {
            View child = ui.group.getChildAt(i);
            if (child instanceof RadioButton && id.equals(((SyncModels.Preset) child.getTag()).id)) {
                ((RadioButton) child).setChecked(true);
                ui.selected = (SyncModels.Preset) child.getTag();
                coordinator.saveSelection(ui.module, id);
                updateModule(ui);
                return;
            }
        }
    }

    private void updateModule(ModuleUi ui) {
        if (ui == null || ui.runtimeView == null) {
            return;
        }
        SyncModels.RuntimeStatus status = ui.runtime;
        alignSelectionToRuntime(ui, status);
        ui.runtimeView.setText(String.format(Locale.ROOT, "运行状态：%s", runtimeText(status)));
        ui.runtimeView.setTextColor(status.paused ? Color.rgb(180, 83, 9)
                : status.running ? Color.rgb(22, 101, 52) : Color.rgb(71, 85, 105));
        boolean locked = actionPending || status.isBusy();
        boolean controlReady = coordinator != null && coordinator.canControl();
        boolean revisionCurrent = coordinator != null
                && coordinator.isRuntimeRevisionCurrent(ui.module);
        ui.group.setEnabled(!locked);
        boolean hasSelection = ui.selected != null;
        boolean resumeSelectionMatches = status.state != SyncModels.RuntimeState.PAUSED
                || (hasSelection && status.presetId != null
                && status.presetId.equals(ui.selected.id));
        ui.startButton.setEnabled(controlReady && !actionPending && hasSelection &&
                revisionCurrent && resumeSelectionMatches
                && (!status.isBusy() || status.state == SyncModels.RuntimeState.PAUSED));
        ui.pauseButton.setEnabled(controlReady && !actionPending &&
                revisionCurrent && (status.running || status.starting));
        ui.stopButton.setEnabled(controlReady && !actionPending && status.isBusy()
                && status.state != SyncModels.RuntimeState.STOPPING);
        setButtonEnabledColor(ui.startButton, ui.startButton.isEnabled(), Color.rgb(37, 99, 235));
        setButtonEnabledColor(ui.pauseButton, ui.pauseButton.isEnabled(), Color.rgb(180, 83, 9));
        setButtonEnabledColor(ui.stopButton, ui.stopButton.isEnabled(), Color.rgb(71, 85, 105));
    }

    private void alignSelectionToRuntime(ModuleUi ui, SyncModels.RuntimeStatus status) {
        if (ui == null || status == null || !status.isBusy() || status.presetId == null
                || ui.group == null) {
            return;
        }
        if (ui.selected != null && status.presetId.equals(ui.selected.id)) {
            return;
        }
        for (int i = 0; i < ui.group.getChildCount(); i++) {
            View child = ui.group.getChildAt(i);
            if (!(child instanceof RadioButton) || child.getTag() == null) {
                continue;
            }
            SyncModels.Preset candidate = (SyncModels.Preset) child.getTag();
            if (status.presetId.equals(candidate.id)) {
                ui.applying = true;
                ((RadioButton) child).setChecked(true);
                ui.applying = false;
                ui.selected = candidate;
                coordinator.saveSelection(ui.module, candidate.id);
                return;
            }
        }
    }

    private void updateOverlayPanel() {
        if (!OverlayService.isRunning()) {
            return;
        }
        SyncModels.Module overlayModule = OverlayService.getActiveModule();
        SyncModels.Preset overlayPreset = OverlayService.getSelectedPreset();
        SyncModels.Snapshot snapshot = coordinator.getSnapshot();
        OverlayService.updatePanelData(snapshot, overlayModule, overlayPreset,
                coordinator.getRuntime().forModule(overlayModule),
                coordinator.getState() == SyncModels.SyncState.ERROR ? syncStatus.getText().toString() : "");
        OverlayService.updateControlAvailability(coordinator.canControl());
        OverlayService.updateRuntime(overlayModule,
                coordinator.getRuntime().forModule(overlayModule));
    }

    private void scheduleRuntimePolling() {
        mainHandler.removeCallbacks(runtimePollingTask);
        if (!destroyed && (activityStarted || OverlayService.isRunning()) && coordinator != null) {
            mainHandler.postDelayed(runtimePollingTask, 2500L);
        }
    }

    private static String runtimeText(SyncModels.RuntimeStatus status) {
        if (status == null || status.state == SyncModels.RuntimeState.UNKNOWN) {
            return "未知";
        }
        switch (status.state) {
            case STARTING:
                return "启动中";
            case RUNNING:
                return "运行中";
            case PAUSING:
                return "暂停中";
            case PAUSED:
                return "已暂停";
            case STOPPING:
                return "停止中";
            case COMPLETED:
                return "已完成";
            case CANCELLED:
                return "已取消";
            case FAULTED:
                return "异常";
            default:
                return "已停止";
        }
    }

    private void setConnectionError(String text) {
        connectionStatus.setText(text);
        connectionStatus.setTextColor(Color.rgb(185, 28, 28));
        setSyncStatus(text, true);
    }

    private void setSyncStatus(String text, boolean error) {
        if (syncStatus != null) {
            syncStatus.setText(text == null ? "" : text);
            syncStatus.setTextColor(error ? Color.rgb(185, 28, 28) : Color.rgb(71, 85, 105));
        }
        if (error) {
            OverlayService.updateErrorStatus(text);
        } else {
            OverlayService.updateSyncStatus(text);
        }
    }

    private ModuleUi moduleUi(SyncModels.Module module) {
        switch (module) {
            case PROGRESSION:
                return progressionUi;
            case ASSISTANT:
                return assistantUi;
            default:
                return sendUi;
        }
    }

    private static SyncModels.Module moduleFrom(String value) {
        if ("progression".equalsIgnoreCase(value)) {
            return SyncModels.Module.PROGRESSION;
        }
        if ("assistant".equalsIgnoreCase(value)) {
            return SyncModels.Module.ASSISTANT;
        }
        return SyncModels.Module.SEND;
    }

    private static UUID parseUuid(String value) {
        try {
            return value == null || value.isEmpty() ? null : UUID.fromString(value);
        } catch (IllegalArgumentException ignored) {
            return null;
        }
    }

    private String coordinatorEndpoint() {
        String value = coordinator == null ? "" : new SyncStore(this).getEndpoint();
        return value.isEmpty() ? SyncCoordinator.DEFAULT_ENDPOINT : value;
    }

    private void setButtonEnabledColor(Button button, boolean enabled, int color) {
        button.setBackground(buttonBackground(enabled ? color : Color.rgb(148, 163, 184)));
        button.setTextColor(enabled ? Color.WHITE : Color.rgb(226, 232, 240));
    }

    private LinearLayout card() {
        LinearLayout value = new LinearLayout(this);
        value.setOrientation(LinearLayout.VERTICAL);
        value.setPadding(dp(14), dp(13), dp(14), dp(13));
        GradientDrawable background = new GradientDrawable();
        background.setColor(Color.WHITE);
        background.setCornerRadius(dp(12));
        value.setBackground(background);
        return value;
    }

    private EditText editText(String hint, int inputType) {
        EditText value = new EditText(this);
        value.setSingleLine(true);
        value.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        value.setHint(hint);
        value.setInputType(inputType);
        value.setPadding(dp(10), 0, dp(10), 0);
        return value;
    }

    private Button actionButton(String text, int color) {
        Button value = new Button(this);
        value.setAllCaps(false);
        value.setText(text);
        value.setTextColor(Color.WHITE);
        value.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14);
        value.setMinHeight(dp(44));
        value.setMinimumHeight(dp(44));
        value.setBackground(buttonBackground(color));
        installPressFeedback(value);
        return value;
    }

    private void installPressFeedback(View value) {
        value.setOnTouchListener((view, event) -> {
            int action = event.getActionMasked();
            if (action == MotionEvent.ACTION_DOWN) {
                view.animate().cancel();
                view.animate().scaleX(0.97f).scaleY(0.97f).setDuration(80L).start();
            } else if (action == MotionEvent.ACTION_UP
                    || action == MotionEvent.ACTION_CANCEL) {
                view.animate().scaleX(1f).scaleY(1f).setDuration(140L).start();
            }
            return false;
        });
    }

    private GradientDrawable buttonBackground(int color) {
        GradientDrawable background = new GradientDrawable();
        background.setColor(color);
        background.setCornerRadius(dp(8));
        return background;
    }

    private TextView textView(String text, int size, int color) {
        TextView value = new TextView(this);
        value.setText(text);
        value.setTextSize(TypedValue.COMPLEX_UNIT_SP, size);
        value.setTextColor(color);
        return value;
    }

    private LinearLayout.LayoutParams marginParams(int width, int left, int top, int right, int bottom) {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(width,
                ViewGroup.LayoutParams.WRAP_CONTENT);
        params.setMargins(dp(left), dp(top), dp(right), dp(bottom));
        return params;
    }

    private LinearLayout.LayoutParams weightParams(int width, float weight, int left, int top, int right) {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(width,
                ViewGroup.LayoutParams.WRAP_CONTENT, weight);
        params.setMargins(dp(left), dp(top), dp(right), 0);
        return params;
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density + 0.5f);
    }

    private static final class ModuleUi {
        final SyncModels.Module module;
        final String title;
        RadioGroup group;
        TextView detail;
        TextView runtimeView;
        Button startButton;
        Button pauseButton;
        Button stopButton;
        List<SyncModels.Preset> items = new ArrayList<>();
        SyncModels.Preset selected;
        SyncModels.RuntimeStatus runtime = SyncModels.RuntimeStatus.parse(null);
        boolean applying;

        ModuleUi(SyncModels.Module module, String title) {
            this.module = module;
            this.title = title;
        }
    }
}
