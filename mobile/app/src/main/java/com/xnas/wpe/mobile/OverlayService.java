package com.xnas.wpe.mobile;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.res.ColorStateList;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.drawable.Drawable;
import android.graphics.Paint;
import android.graphics.PixelFormat;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.RippleDrawable;
import android.graphics.drawable.StateListDrawable;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.provider.Settings;
import android.view.Gravity;
import android.view.HapticFeedbackConstants;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.ViewConfiguration;
import android.view.WindowManager;
import android.view.animation.PathInterpolator;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import java.util.HashMap;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;
import java.util.Map;
import java.util.UUID;

/** Hosts the floating window and routes commands through the shared coordinator. */
public final class OverlayService extends Service {
    public static final String ACTION_START = "com.xnas.wpe.mobile.OVERLAY_START";
    public static final String ACTION_PAUSE = "com.xnas.wpe.mobile.OVERLAY_PAUSE";
    public static final String ACTION_STOP = "com.xnas.wpe.mobile.OVERLAY_STOP";
    public static final String ACTION_SELECT_MODULE = "com.xnas.wpe.mobile.OVERLAY_SELECT_MODULE";
    public static final String ACTION_SELECT_PRESET = "com.xnas.wpe.mobile.OVERLAY_SELECT_PRESET";
    public static final String ACTION_REFRESH = "com.xnas.wpe.mobile.OVERLAY_REFRESH";
    public static final String ACTION_OPEN_APP = "com.xnas.wpe.mobile.OVERLAY_OPEN_APP";
    public static final String INTERNAL_COMMAND_PERMISSION =
            "com.xnas.wpe.mobile.permission.INTERNAL_OVERLAY_COMMAND";
    public static final String EXTRA_MODULE = "module";
    public static final String EXTRA_PRESET_ID = "presetId";
    public static final String EXTRA_COMMAND_ID = "commandId";

    private static final String CHANNEL_ID = "wpe_overlay";
    private static final int NOTIFICATION_ID = 4101;
    private static final long RUNTIME_POLL_INTERVAL_MS = 2500L;
    private static final PathInterpolator UI_EASE_OUT =
            new PathInterpolator(0.23f, 1f, 0.32f, 1f);
    private static volatile OverlayService activeService;
    private static volatile String syncStatus = "";
    private static volatile SyncModels.Snapshot snapshot;
    private static volatile SyncModels.Module activeModule = SyncModels.Module.SEND;
    private static volatile SyncModels.Preset selectedPreset;
    private static volatile SyncModels.RuntimeStatus runtime = SyncModels.RuntimeStatus.parse(null);
    private static volatile String errorStatus = "";
    private static volatile boolean actionPending;
    private static volatile boolean controlsAvailable = true;
    private static volatile BallState ballState = BallState.NORMAL;
    private static final Object COMMAND_LOCK = new Object();
    private static final ArrayDeque<Intent> PENDING_COMMANDS = new ArrayDeque<>();
    private static final ArrayDeque<String> HANDLED_COMMAND_IDS = new ArrayDeque<>();

    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final Runnable runtimePollingTask = new Runnable() {
        @Override
        public void run() {
            if (activeService != OverlayService.this) {
                return;
            }
            if (!Settings.canDrawOverlays(OverlayService.this)) {
                stopSelf();
                return;
            }
            SyncCoordinator coordinator = ensureCoordinator();
            if (coordinator != null) {
                coordinator.refreshRuntime();
            }
            mainHandler.postDelayed(this, RUNTIME_POLL_INTERVAL_MS);
        }
    };
    private WindowManager windowManager;
    private FloatingBallView collapsedView;
    private View panelView;
    private LinearLayout groupsContainer;
    private ScrollView groupsScroll;
    private TextView panelStatusView;
    private Button startButton;
    private Button pauseButton;
    private Button stopButton;
    private final Map<String, Button> moduleButtons = new HashMap<>();
    private final Map<String, Boolean> expandedGroups = new HashMap<>();
    private boolean panelUpdating;
    private WindowManager.LayoutParams collapsedParams;
    private float dragStartRawX;
    private float dragStartRawY;
    private int dragStartX;
    private int dragStartY;
    private boolean dragging;

    private enum BallState {
        NORMAL, RUNNING, PAUSED
    }

    public static boolean isRunning() {
        return activeService != null;
    }

    public static SyncModels.Module getActiveModule() {
        return activeModule;
    }

    public static SyncModels.Preset getSelectedPreset() {
        return selectedPreset;
    }

    public static List<Intent> drainPendingCommands() {
        synchronized (COMMAND_LOCK) {
            List<Intent> result = new ArrayList<>(PENDING_COMMANDS);
            PENDING_COMMANDS.clear();
            return result;
        }
    }

    public static boolean claimCommand(String commandId) {
        if (commandId == null || commandId.isEmpty()) {
            return true;
        }
        synchronized (COMMAND_LOCK) {
            if (HANDLED_COMMAND_IDS.contains(commandId)) {
                return false;
            }
            HANDLED_COMMAND_IDS.addLast(commandId);
            while (HANDLED_COMMAND_IDS.size() > 32) {
                HANDLED_COMMAND_IDS.removeFirst();
            }
            Iterator<Intent> iterator = PENDING_COMMANDS.iterator();
            while (iterator.hasNext()) {
                if (commandId.equals(iterator.next().getStringExtra(EXTRA_COMMAND_ID))) {
                    iterator.remove();
                    break;
                }
            }
            return true;
        }
    }

    public static void updateSyncStatus(String value) {
        syncStatus = value == null ? "" : value;
        errorStatus = "";
        OverlayService service = activeService;
        if (service != null) {
            service.mainHandler.post(service::renderPanel);
        }
    }

    public static void updateErrorStatus(String value) {
        errorStatus = value == null ? "" : value;
        syncStatus = value == null ? "" : value;
        OverlayService service = activeService;
        if (service != null) {
            service.mainHandler.post(service::renderPanel);
        }
    }

    public static void clearErrorStatus() {
        errorStatus = "";
        syncStatus = "";
        OverlayService service = activeService;
        if (service != null) {
            service.mainHandler.post(service::renderPanel);
        }
    }

    public static void updateActionPending(boolean pending) {
        actionPending = pending;
        OverlayService service = activeService;
        if (service != null) {
            service.mainHandler.post(service::renderPanel);
        }
    }

    public static void updateControlAvailability(boolean available) {
        controlsAvailable = available;
        OverlayService service = activeService;
        if (service != null) {
            service.mainHandler.post(service::renderPanel);
        }
    }

    public static void updatePanelData(SyncModels.Snapshot value, SyncModels.Module module,
                                       SyncModels.Preset preset, SyncModels.RuntimeStatus valueRuntime,
                                       String error) {
        snapshot = value;
        activeModule = module == null ? SyncModels.Module.SEND : module;
        selectedPreset = preset;
        runtime = valueRuntime == null ? SyncModels.RuntimeStatus.parse(null) : valueRuntime;
        updateBallState(runtime);
        if (error != null && !error.isEmpty()) {
            errorStatus = error;
        } else {
            // A successful runtime/snapshot replay must not leave an older
            // action error covering the current panel state.
            errorStatus = "";
        }
        OverlayService service = activeService;
        if (service != null) {
            service.mainHandler.post(service::renderPanel);
        }
    }

    public static void resetProfileState() {
        snapshot = null;
        selectedPreset = null;
        runtime = SyncModels.RuntimeStatus.parse(null);
        errorStatus = "";
        syncStatus = "";
        actionPending = false;
        controlsAvailable = false;
        ballState = BallState.NORMAL;
        OverlayService service = activeService;
        if (service != null) {
            service.expandedGroups.clear();
            service.mainHandler.post(service::renderPanel);
        }
    }

    public static void updateSnapshot(SyncModels.Snapshot value) {
        snapshot = value;
        if (value == null) {
            selectedPreset = null;
        } else {
            UUID preferred = selectedPreset == null ? null : selectedPreset.id;
            SyncCoordinator coordinator = SyncCoordinator.current();
            if (preferred == null && coordinator != null) {
                preferred = coordinator.getSelected(activeModule);
            }
            selectedPreset = findPreset(value, activeModule, preferred);
            if (selectedPreset == null && !value.presets(activeModule).isEmpty()) {
                selectedPreset = value.presets(activeModule).get(0);
                if (coordinator != null) {
                    coordinator.saveSelection(activeModule, selectedPreset.id);
                }
            }
        }
        OverlayService service = activeService;
        if (service != null) {
            service.mainHandler.post(service::renderPanel);
        }
    }

    public static void updateRuntime(SyncModels.Module module, SyncModels.RuntimeStatus value) {
        if (module != null && module == activeModule && value != null) {
            runtime = value;
            if (value.state == SyncModels.RuntimeState.FAULTED) {
                errorStatus = value.detail;
            }
            updateBallState(value);
            if (value.isBusy() && snapshot != null && value.presetId != null) {
                SyncModels.Preset runningPreset = findPreset(snapshot, module, value.presetId);
                if (runningPreset != null) {
                    selectedPreset = runningPreset;
                }
            }
        }
        OverlayService service = activeService;
        if (service != null) {
            service.mainHandler.post(service::renderPanel);
        }
    }

    public static void updateRuntimeBundle(SyncModels.RuntimeBundle value) {
        runtime = value == null ? SyncModels.RuntimeStatus.parse(null)
                : value.forModule(activeModule);
        if (runtime.state == SyncModels.RuntimeState.FAULTED) {
            errorStatus = runtime.detail;
        }
        updateBallState(runtime);
        if (runtime.isBusy() && snapshot != null && runtime.presetId != null) {
            SyncModels.Preset runningPreset = findPreset(snapshot, activeModule, runtime.presetId);
            if (runningPreset != null) {
                selectedPreset = runningPreset;
            }
        }
        OverlayService service = activeService;
        if (service != null) {
            service.mainHandler.post(service::renderPanel);
        }
    }

    @Override
    public void onCreate() {
        super.onCreate();
        windowManager = (WindowManager) getSystemService(WINDOW_SERVICE);
        createNotificationChannel();
        startAsForeground();
        activeService = this;
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (!Settings.canDrawOverlays(this)) {
            stopSelf();
            return START_NOT_STICKY;
        }
        SyncCoordinator coordinator = ensureCoordinator();
        restorePanelState(coordinator);
        controlsAvailable = coordinator != null && coordinator.canControl();
        showCollapsed();
        renderPanel();
        scheduleRuntimePolling();
        return START_STICKY;
    }

    @Override
    public void onDestroy() {
        mainHandler.removeCallbacks(runtimePollingTask);
        removeOverlayView(panelView);
        removeOverlayView(collapsedView);
        panelView = null;
        collapsedView = null;
        groupsContainer = null;
        groupsScroll = null;
        moduleButtons.clear();
        if (activeService == this) {
            activeService = null;
            // A service recreation can briefly overlap the old instance's
            // onDestroy callback.  Only the owner may clear the process-wide
            // panel state; an old instance must not wipe the new one.
            resetProfileState();
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
            stopForeground(STOP_FOREGROUND_REMOVE);
        } else {
            stopForeground(true);
        }
        super.onDestroy();
    }

    private void scheduleRuntimePolling() {
        mainHandler.removeCallbacks(runtimePollingTask);
        mainHandler.postDelayed(runtimePollingTask, RUNTIME_POLL_INTERVAL_MS);
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    private void showCollapsed() {
        if (collapsedView != null) {
            return;
        }
        removeOverlayView(panelView);
        panelView = null;
        collapsedView = new FloatingBallView(this);
        collapsedView.setContentDescription("展开 " + getString(R.string.app_name) + " 悬浮控制");
        collapsedView.setOnClickListener(v -> expandPanel());
        installDragFeedback(collapsedView);
        try {
            collapsedParams = ballLayoutParams();
            windowManager.addView(collapsedView, collapsedParams);
        } catch (RuntimeException ex) {
            collapsedView = null;
            collapsedParams = null;
            stopSelf();
        }
    }

    /**
     * Lets the collapsed ball be dragged to any screen position.  A simple
     * tap still expands the panel; a drag past the touch slop moves the ball
     * and remembers the new position.
     */
    private void installDragFeedback(View view) {
        view.setOnTouchListener((dragged, event) -> {
            int action = event.getActionMasked();
            switch (action) {
                case MotionEvent.ACTION_DOWN:
                    dragged.performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY);
                    dragged.animate().cancel();
                    dragged.animate().scaleX(0.97f).scaleY(0.97f)
                            .setDuration(90L)
                            .setInterpolator(UI_EASE_OUT)
                            .start();
                    dragStartRawX = event.getRawX();
                    dragStartRawY = event.getRawY();
                    dragStartX = collapsedParams == null ? 0 : collapsedParams.x;
                    dragStartY = collapsedParams == null ? 0 : collapsedParams.y;
                    dragging = false;
                    return true;
                case MotionEvent.ACTION_MOVE:
                    float dx = event.getRawX() - dragStartRawX;
                    float dy = event.getRawY() - dragStartRawY;
                    if (!dragging) {
                        int slop = ViewConfiguration.get(this).getScaledTouchSlop();
                        if (Math.abs(dx) > slop || Math.abs(dy) > slop) {
                            dragging = true;
                        }
                    }
                    if (dragging && collapsedParams != null) {
                        int width = collapsedParams.width;
                        int height = collapsedParams.height;
                        collapsedParams.x = clamp(dragStartX + Math.round(dx),
                                0, Math.max(0, displayWidth() - width));
                        collapsedParams.y = clamp(dragStartY + Math.round(dy),
                                0, Math.max(0, displayHeight() - height));
                        try {
                            windowManager.updateViewLayout(dragged, collapsedParams);
                        } catch (RuntimeException ignored) {
                            // Permission can be revoked mid-gesture.
                        }
                    }
                    return true;
                case MotionEvent.ACTION_UP:
                    dragged.animate().cancel();
                    dragged.animate().scaleX(1f).scaleY(1f)
                            .setDuration(140L)
                            .setInterpolator(UI_EASE_OUT)
                            .start();
                    if (!dragging) {
                        dragged.performClick();
                    } else if (collapsedParams != null) {
                        new SyncStore(this).saveBallPosition(collapsedParams.x, collapsedParams.y);
                    }
                    dragging = false;
                    return true;
                case MotionEvent.ACTION_CANCEL:
                    dragged.animate().cancel();
                    dragged.animate().scaleX(1f).scaleY(1f)
                            .setDuration(140L)
                            .setInterpolator(UI_EASE_OUT)
                            .start();
                    dragging = false;
                    return true;
                default:
                    return false;
            }
        });
    }

    private void expandPanel() {
        if (panelView != null) {
            return;
        }
        removeOverlayView(collapsedView);
        collapsedView = null;
        panelView = buildPanelView();
        try {
            windowManager.addView(panelView, panelLayoutParams(dp(338), overlayPanelHeight()));
            panelView.setAlpha(0f);
            panelView.setScaleX(0.97f);
            panelView.setScaleY(0.97f);
            panelView.setPivotX(dp(338));
            panelView.setPivotY(overlayPanelHeight() / 2f);
            renderPanel();
            panelView.animate().alpha(1f).scaleX(1f).scaleY(1f)
                    .setDuration(180L)
                    .setInterpolator(UI_EASE_OUT)
                    .start();
        } catch (RuntimeException ex) {
            panelView = null;
            showCollapsed();
        }
    }

    private void collapsePanel() {
        removeOverlayView(panelView);
        panelView = null;
        groupsContainer = null;
        groupsScroll = null;
        panelStatusView = null;
        startButton = null;
        pauseButton = null;
        stopButton = null;
        moduleButtons.clear();
        showCollapsed();
    }

    private View buildPanelView() {
        OverlayPanelLayout panel = new OverlayPanelLayout(this);
        panel.setOrientation(LinearLayout.VERTICAL);
        panel.setPadding(dp(14), dp(14), dp(14), dp(12));
        panel.setBackground(roundBackground(Color.argb(232, 15, 15, 18),
                Color.argb(190, 148, 163, 184), dp(18), dp(1)));
        panel.setElevation(dp(12));
        panel.setOnTouchListener(new View.OnTouchListener() {
            @Override
            public boolean onTouch(View view, MotionEvent event) {
                if (event.getAction() == MotionEvent.ACTION_OUTSIDE) {
                    collapsePanel();
                    return true;
                }
                if (event.getAction() == MotionEvent.ACTION_UP) {
                    view.performClick();
                }
                return false;
            }
        });

        LinearLayout mainControls = new LinearLayout(this);
        mainControls.setOrientation(LinearLayout.HORIZONTAL);
        startButton = primaryButton("开始", ACTION_START, Color.rgb(120, 75, 12));
        pauseButton = primaryButton("暂停", ACTION_PAUSE, Color.rgb(36, 36, 40));
        stopButton = primaryButton("停止", ACTION_STOP, Color.rgb(36, 36, 40));
        mainControls.addView(startButton, equalButtonParams(true));
        mainControls.addView(pauseButton, equalButtonParams(false));
        mainControls.addView(stopButton, equalButtonParams(false));

        groupsScroll = new ScrollView(this);
        groupsScroll.setFillViewport(false);
        groupsContainer = new LinearLayout(this);
        groupsContainer.setOrientation(LinearLayout.VERTICAL);
        groupsScroll.addView(groupsContainer, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        LinearLayout bottom = new LinearLayout(this);
        bottom.setOrientation(LinearLayout.HORIZONTAL);
        bottom.setPadding(0, dp(8), 0, 0);
        moduleButtons.clear();
        bottom.addView(bottomButton("预设", "send"), bottomButtonParams(true));
        bottom.addView(bottomButton("递进", "progression"), bottomButtonParams(false));
        bottom.addView(bottomButton("助手", "assistant"), bottomButtonParams(false));
        Button refresh = compactButton("更新", Color.rgb(37, 37, 42));
        refresh.setContentDescription("更新完整预设快照");
        refresh.setOnClickListener(v -> dispatch(ACTION_REFRESH, null, null));
        bottom.addView(refresh, bottomButtonParams(false));

        panelStatusView = overlayText("", 11, Color.rgb(251, 191, 36));
        panelStatusView.setMaxLines(2);

        panel.addView(bottom, verticalParams(0, 10));
        panel.addView(groupsScroll, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        panel.addView(mainControls, verticalParams(10, 0));
        panel.addView(panelStatusView, verticalParams(4, 0));
        return panel;
    }

    private Button primaryButton(String label, String action, int color) {
        Button button = compactButton(label, color);
        button.setTextSize(18);
        button.setMinHeight(dp(64));
        button.setMinimumHeight(dp(64));
        button.setContentDescription(label + "当前" + moduleTitle(activeModule));
        // Carry the preset selected at the moment the user presses the
        // button.  The desktop runtime keeps the last completed preset id,
        // so deriving START from runtime alone can accidentally restart an
        // older preset (for example, 回家).
        button.setOnClickListener(v -> dispatch(action, activeModule.wireName,
                selectedPresetIdForModule(activeModule)));
        return button;
    }

    private String selectedPresetIdForModule(SyncModels.Module module) {
        if (module == null || snapshot == null || selectedPreset == null) {
            return null;
        }
        SyncModels.Preset current = findPreset(snapshot, module, selectedPreset.id);
        return current == null ? null : current.id.toString();
    }

    private Button bottomButton(String label, String module) {
        Button button = compactButton(label, Color.rgb(27, 27, 31));
        button.setContentDescription("切换到" + label + "模块");
        button.setOnClickListener(v -> dispatch(ACTION_SELECT_MODULE, module, null));
        moduleButtons.put(module, button);
        return button;
    }

    private void renderPanel() {
        if (collapsedView != null) {
            collapsedView.invalidate();
        }
        if (panelView == null || panelUpdating) {
            return;
        }
        panelUpdating = true;
        try {
            renderGroups();
            if (panelStatusView != null) {
                String message = errorStatus.isEmpty() ? syncStatus : errorStatus;
                if ((message == null || message.isEmpty()) && actionPending) {
                    message = "正在提交操作…";
                }
                panelStatusView.setText(message == null ? "" : message);
                panelStatusView.setVisibility(shouldShowPanelStatus(message)
                        ? View.VISIBLE : View.GONE);
            }
            updatePrimaryButtons();
        } finally {
            panelUpdating = false;
        }
    }

    private void renderGroups() {
        if (groupsContainer == null) {
            return;
        }
        int scrollY = groupsScroll == null ? 0 : groupsScroll.getScrollY();
        groupsContainer.removeAllViews();
        SyncModels.Snapshot currentSnapshot = snapshot;
        if (currentSnapshot == null) {
            groupsContainer.addView(overlayText("暂无已同步预设", 14, Color.rgb(203, 213, 225)),
                    verticalParams(8, 8));
            restoreGroupScroll(scrollY);
            return;
        }
        List<SyncModels.GroupInfo> groups = currentSnapshot.groups(activeModule);
        List<SyncModels.Preset> presets = currentSnapshot.presets(activeModule);
        Map<UUID, LinearLayout> children = new HashMap<>();
        Map<UUID, LinearLayout> presetRows = new HashMap<>();
        Map<UUID, Integer> presetColumns = new HashMap<>();
        boolean firstGroup = true;
        for (SyncModels.GroupInfo group : groups) {
            LinearLayout childRows = new LinearLayout(this);
            childRows.setOrientation(LinearLayout.VERTICAL);
            childRows.setPadding(dp(8), 0, 0, dp(3));
            children.put(group.id, childRows);
            String groupKey = activeModule.wireName + ":" + group.id;
            Boolean rememberedExpansion = expandedGroups.get(groupKey);
            boolean selectedGroup = selectedPreset != null && group.id.equals(selectedPreset.groupId);
            boolean expanded = rememberedExpansion != null
                    ? rememberedExpansion
                    : selectedGroup || (selectedPreset == null && firstGroup);
            firstGroup = false;
            FrameLayout groupRow = new FrameLayout(this);
            groupRow.setFocusable(true);
            groupRow.setClickable(true);
            groupRow.setMinimumHeight(dp(44));
            groupRow.setPadding(dp(12), 0, dp(12), 0);
            groupRow.setBackground(statefulBackground(Color.rgb(29, 29, 33),
                    pressedColor(Color.rgb(29, 29, 33)), Color.rgb(48, 48, 52),
                    Color.argb(45, 255, 255, 255)));

            TextView groupLabel = overlayText(group.name, 14, Color.WHITE);
            groupLabel.setGravity(Gravity.CENTER_VERTICAL | Gravity.START);
            groupLabel.setSingleLine(true);
            groupLabel.setEllipsize(android.text.TextUtils.TruncateAt.END);
            FrameLayout.LayoutParams groupLabelParams = new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT,
                    Gravity.START | Gravity.CENTER_VERTICAL);
            groupLabelParams.rightMargin = dp(32);
            groupRow.addView(groupLabel, groupLabelParams);

            TextView groupChevron = overlayText(expanded ? "▼" : "▶", 16, Color.rgb(226, 232, 240));
            groupChevron.setGravity(Gravity.CENTER);
            FrameLayout.LayoutParams groupChevronParams = new FrameLayout.LayoutParams(
                    dp(24), ViewGroup.LayoutParams.MATCH_PARENT, Gravity.END | Gravity.CENTER_VERTICAL);
            groupRow.addView(groupChevron, groupChevronParams);

            groupRow.setTag(expanded);
            groupRow.setContentDescription(groupAccessibilityLabel(group.name, expanded));
            childRows.setVisibility(expanded ? View.VISIBLE : View.GONE);
            installPressFeedback(groupRow);
            groupRow.setOnClickListener(v -> {
                boolean wasExpanded = Boolean.TRUE.equals(v.getTag());
                boolean nowExpanded = !wasExpanded;
                v.setTag(nowExpanded);
                groupRow.setContentDescription(groupAccessibilityLabel(group.name, nowExpanded));
                expandedGroups.put(groupKey, nowExpanded);
                groupChevron.setText(nowExpanded ? "▼" : "▶");
                childRows.setVisibility(nowExpanded ? View.VISIBLE : View.GONE);
            });
            groupsContainer.addView(groupRow, verticalParams(0, 4));
            groupsContainer.addView(childRows, verticalParams(0, 3));
        }
        boolean selectionLocked = actionPending || (runtime != null && runtime.isBusy());
        for (SyncModels.Preset preset : presets) {
            LinearLayout childRows = children.get(preset.groupId);
            if (childRows == null) {
                continue;
            }
            LinearLayout presetRow = presetRows.get(preset.groupId);
            int presetColumn = presetColumns.containsKey(preset.groupId)
                    ? presetColumns.get(preset.groupId) : 3;
            if (presetRow == null || presetColumn >= 3) {
                presetRow = new LinearLayout(this);
                presetRow.setOrientation(LinearLayout.HORIZONTAL);
                presetRow.setWeightSum(3f);
                presetRow.setBaselineAligned(false);
                childRows.addView(presetRow, verticalParams(0, 4));
                presetRows.put(preset.groupId, presetRow);
                presetColumn = 0;
            }
            boolean selected = selectedPreset != null && preset.id.equals(selectedPreset.id);
            int normalColor = selected ? Color.rgb(89, 57, 13) : Color.rgb(24, 24, 28);
            int disabledColor = selected ? normalColor : Color.rgb(48, 48, 52);
            Button presetButton = compactButton(preset.displayName(), normalColor);
            setButtonAppearance(presetButton, normalColor, pressedColor(normalColor),
                    disabledColor, !selectionLocked);
            presetButton.setGravity(Gravity.CENTER);
            presetButton.setSingleLine(false);
            presetButton.setMaxLines(2);
            presetButton.setEllipsize(android.text.TextUtils.TruncateAt.END);
            presetButton.setTextSize(12);
            presetButton.setMinHeight(dp(52));
            presetButton.setMinimumHeight(dp(52));
            presetButton.setMinWidth(0);
            presetButton.setMinimumWidth(0);
            presetButton.setPadding(dp(6), 0, dp(6), 0);
            presetButton.setText(presetLabel(preset));
            presetButton.setContentDescription("选择预设" + preset.displayName());
            presetButton.setOnClickListener(v -> {
                selectedPreset = preset;
                dispatch(ACTION_SELECT_PRESET, activeModule.wireName, preset.id.toString());
                renderPanel();
            });
            presetRow.addView(presetButton, presetCellParams());
            presetColumns.put(preset.groupId, presetColumn + 1);
        }
        for (Map.Entry<UUID, LinearLayout> entry : presetRows.entrySet()) {
            int columns = presetColumns.get(entry.getKey());
            while (columns < 3) {
                View spacer = new View(this);
                entry.getValue().addView(spacer, presetCellParams());
                columns++;
            }
        }
        if (groups.isEmpty() && presets.isEmpty()) {
            groupsContainer.addView(overlayText("暂无已同步预设", 14, Color.rgb(203, 213, 225)),
                    verticalParams(8, 8));
        }
        restoreGroupScroll(scrollY);
    }

    private boolean shouldShowPanelStatus(String message) {
        if (message == null || message.isEmpty()) {
            return false;
        }
        if (actionPending || !errorStatus.isEmpty()) {
            return true;
        }
        return message.contains("正在")
                || message.contains("失败")
                || message.contains("异常")
                || message.contains("有更新")
                || message.contains("已更新")
                || message.contains("已是最新")
                || message.contains("请");
    }

    private void updatePrimaryButtons() {
        if (startButton == null || pauseButton == null || stopButton == null) {
            return;
        }
        boolean busy = runtime != null && runtime.isBusy();
        boolean running = runtime != null && (runtime.running || runtime.starting);
        boolean paused = runtime != null && runtime.paused;
        SyncCoordinator coordinator = SyncCoordinator.current();
        boolean revisionCurrent = coordinator != null
                && coordinator.isRuntimeRevisionCurrent(activeModule);
        boolean resumeSelectionMatches = !paused || (selectedPreset != null
                && runtime.presetId != null && runtime.presetId.equals(selectedPreset.id));
        boolean canStart = controlsAvailable && !actionPending && selectedPreset != null
                && revisionCurrent && resumeSelectionMatches && (!busy || paused);
        boolean canPause = controlsAvailable && !actionPending && revisionCurrent && running;
        boolean canStop = controlsAvailable && !actionPending && busy
                && runtime.state != SyncModels.RuntimeState.STOPPING;
        setButtonAppearance(startButton, canStart ? Color.rgb(120, 75, 12) : Color.rgb(48, 48, 52),
                Color.rgb(83, 52, 9), Color.rgb(48, 48, 52), canStart);
        setButtonAppearance(pauseButton, canPause ? Color.rgb(36, 84, 48) : Color.rgb(36, 36, 40),
                Color.rgb(23, 57, 32), Color.rgb(36, 36, 40), canPause);
        setButtonAppearance(stopButton, canStop ? Color.rgb(74, 42, 35) : Color.rgb(36, 36, 40),
                Color.rgb(48, 25, 21), Color.rgb(36, 36, 40), canStop);
        startButton.setContentDescription("开始" + moduleTitle(activeModule) + "预设，暂停后恢复当前任务");
        pauseButton.setContentDescription("暂停当前" + moduleTitle(activeModule) + "任务");
        stopButton.setContentDescription("停止当前" + moduleTitle(activeModule) + "任务");
        updateModuleButtons();
    }

    private String presetLabel(SyncModels.Preset preset) {
        String value = preset.displayName();
        int[] codePoints = toCodePoints(value);
        if (codePoints.length <= 3) {
            return value;
        }
        String firstLine = codePointString(codePoints, 0, 3);
        if (codePoints.length <= 6) {
            return firstLine + "\n" + codePointString(codePoints, 3, codePoints.length - 3);
        }
        return firstLine + "\n" + codePointString(codePoints, 3, 3) + "…";
    }

    private int[] toCodePoints(String value) {
        int count = 0;
        for (int i = 0; i < value.length(); i++) {
            if (isHighSurrogate(value.charAt(i)) && i + 1 < value.length()
                    && isLowSurrogate(value.charAt(i + 1))) {
                i++;
            }
            count++;
        }
        int[] result = new int[count];
        int output = 0;
        for (int i = 0; i < value.length(); i++) {
            char first = value.charAt(i);
            if (isHighSurrogate(first) && i + 1 < value.length()
                    && isLowSurrogate(value.charAt(i + 1))) {
                char second = value.charAt(++i);
                result[output++] = ((first - 0xD800) << 10)
                        + (second - 0xDC00) + 0x10000;
            } else {
                result[output++] = first;
            }
        }
        return result;
    }

    private String codePointString(int[] codePoints, int offset, int count) {
        StringBuilder result = new StringBuilder();
        for (int i = offset; i < offset + count; i++) {
            int codePoint = codePoints[i];
            if (codePoint > 0xFFFF) {
                int value = codePoint - 0x10000;
                result.append((char) ((value >> 10) + 0xD800));
                result.append((char) ((value & 0x3FF) + 0xDC00));
            } else {
                result.append((char) codePoint);
            }
        }
        return result.toString();
    }

    private boolean isHighSurrogate(char value) {
        return value >= 0xD800 && value <= 0xDBFF;
    }

    private boolean isLowSurrogate(char value) {
        return value >= 0xDC00 && value <= 0xDFFF;
    }

    private String groupAccessibilityLabel(String name, boolean expanded) {
        return (expanded ? "收起分组" : "展开分组") + name;
    }

    private void dispatch(String action, String module, String presetId) {
        Intent intent = new Intent(action).setPackage(getPackageName());
        intent.putExtra(EXTRA_COMMAND_ID, UUID.randomUUID().toString());
        if (module != null) {
            intent.putExtra(EXTRA_MODULE, module);
        }
        if (presetId != null) {
            intent.putExtra(EXTRA_PRESET_ID, presetId);
        }
        if (activeService == this) {
            mainHandler.post(() -> {
                if (activeService == this) {
                    handleCommand(intent);
                } else {
                    enqueuePendingCommand(intent);
                    sendBroadcast(intent, INTERNAL_COMMAND_PERMISSION);
                }
            });
            return;
        }
        enqueuePendingCommand(intent);
        sendBroadcast(intent, INTERNAL_COMMAND_PERMISSION);
    }

    private static void enqueuePendingCommand(Intent intent) {
        if (intent == null) {
            return;
        }
        synchronized (COMMAND_LOCK) {
            while (PENDING_COMMANDS.size() >= 8) {
                PENDING_COMMANDS.removeFirst();
            }
            PENDING_COMMANDS.addLast(new Intent(intent));
        }
    }

    private void handleCommand(Intent intent) {
        if (intent == null) {
            return;
        }
        if (activeService != this) {
            enqueuePendingCommand(intent);
            sendBroadcast(intent, INTERNAL_COMMAND_PERMISSION);
            return;
        }
        if (!claimCommand(intent.getStringExtra(EXTRA_COMMAND_ID))) {
            return;
        }
        String action = intent.getAction();
        SyncCoordinator coordinator = ensureCoordinator();
        if (coordinator == null) {
            updateSyncStatus("请先连接电脑端");
            return;
        }
        if (ACTION_SELECT_MODULE.equals(action)) {
            activeModule = moduleFrom(intent.getStringExtra(EXTRA_MODULE));
            selectedPreset = findPreset(snapshot, activeModule, coordinator.getSelected(activeModule));
            if (selectedPreset == null && snapshot != null && !snapshot.presets(activeModule).isEmpty()) {
                selectedPreset = snapshot.presets(activeModule).get(0);
                coordinator.saveSelection(activeModule, selectedPreset.id);
            }
            runtime = coordinator.getRuntime().forModule(activeModule);
            clearErrorStatus();
            if (runtime.state == SyncModels.RuntimeState.FAULTED) {
                errorStatus = runtime.detail;
            }
            updateBallState(runtime);
            renderPanel();
            return;
        }
        if (ACTION_SELECT_PRESET.equals(action)) {
            SyncModels.Module targetModule = moduleFrom(intent.getStringExtra(EXTRA_MODULE));
            SyncModels.RuntimeStatus targetRuntime = coordinator.getRuntime().forModule(targetModule);
            if (actionPending || (targetRuntime != null && targetRuntime.isBusy())) {
                return;
            }
            activeModule = targetModule;
            selectedPreset = findPreset(snapshot, activeModule,
                    parseUuid(intent.getStringExtra(EXTRA_PRESET_ID)));
            if (selectedPreset != null) {
                coordinator.saveSelection(activeModule, selectedPreset.id);
            }
            renderPanel();
            return;
        }
        if (ACTION_REFRESH.equals(action)) {
            coordinator.manualUpdate();
            return;
        }
        if (ACTION_OPEN_APP.equals(action)) {
            Intent launch = new Intent(this, MainActivity.class)
                    .addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP);
            launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            startActivity(launch);
            return;
        }
        SyncModels.Module commandModule = moduleFrom(intent.getStringExtra(EXTRA_MODULE));
        SyncModels.RuntimeStatus current = coordinator.getRuntime().forModule(commandModule);
        UUID requestedPresetId = parseUuid(intent.getStringExtra(EXTRA_PRESET_ID));
        if (ACTION_START.equals(action) || ACTION_PAUSE.equals(action)
                || ACTION_STOP.equals(action)) {
            // Bind an action to the module visible when the user pressed it;
            // a delayed command must not follow a later module switch.
            activeModule = commandModule;
            if (ACTION_START.equals(action)) {
                // START is a new selection unless this is an explicit resume.
                // Never replace it with the runtime's last completed preset.
                SyncModels.Preset requestedPreset = findPreset(snapshot, commandModule,
                        requestedPresetId);
                if (requestedPreset != null) {
                    selectedPreset = requestedPreset;
                } else {
                    selectedPreset = findPreset(snapshot, commandModule,
                            coordinator.getSelected(commandModule));
                }
            } else if (current.isBusy() && current.presetId != null) {
                // PAUSE/STOP remain bound to the task that is actually
                // running; this is deliberately different from START.
                selectedPreset = findPreset(snapshot, commandModule, current.presetId);
            }
            if (selectedPreset == null) {
                selectedPreset = findPreset(snapshot, commandModule,
                        coordinator.getSelected(commandModule));
            }
        }
        UUID id;
        if (ACTION_START.equals(action)) {
            SyncModels.Preset requestedPreset = findPreset(snapshot, commandModule,
                    requestedPresetId);
            id = requestedPreset == null
                    ? selectedPreset == null ? null : selectedPreset.id
                    : requestedPreset.id;
        } else {
            id = current.isBusy() && current.presetId != null ? current.presetId
                    : selectedPreset == null ? null : selectedPreset.id;
        }
        if (ACTION_START.equals(action)) {
            coordinator.action(commandModule, id, SyncCoordinator.Action.START);
        } else if (ACTION_PAUSE.equals(action)) {
            coordinator.action(commandModule, id, SyncCoordinator.Action.PAUSE);
        } else if (ACTION_STOP.equals(action)) {
            coordinator.action(commandModule, id, SyncCoordinator.Action.STOP);
        }
    }

    private SyncCoordinator ensureCoordinator() {
        SyncCoordinator coordinator = SyncCoordinator.current();
        if (coordinator == null) {
            coordinator = SyncCoordinator.obtain(this, null);
            // A sticky service may be recreated without an Activity. Restore
            // the saved endpoint and reconnect without exposing the hidden
            // setup Activity.
            coordinator.loadSavedConnection();
        }
        return coordinator;
    }

    private void restorePanelState(SyncCoordinator coordinator) {
        if (coordinator == null) {
            updatePanelData(null, activeModule, null,
                    SyncModels.RuntimeStatus.parse(null), "");
            return;
        }
        SyncModels.Snapshot currentSnapshot = coordinator.getSnapshot();
        SyncModels.Preset currentSelection = findPreset(currentSnapshot, activeModule,
                coordinator.getSelected(activeModule));
        if (currentSelection == null && currentSnapshot != null
                && !currentSnapshot.presets(activeModule).isEmpty()) {
            currentSelection = currentSnapshot.presets(activeModule).get(0);
            coordinator.saveSelection(activeModule, currentSelection.id);
        }
        updatePanelData(currentSnapshot, activeModule, currentSelection,
                coordinator.getRuntime().forModule(activeModule), "");
        if (coordinator.getState() != SyncModels.SyncState.ERROR) {
            errorStatus = "";
        }
    }

    private static SyncModels.Module moduleFrom(String value) {
        if (SyncModels.Module.PROGRESSION.wireName.equals(value)) {
            return SyncModels.Module.PROGRESSION;
        }
        if (SyncModels.Module.ASSISTANT.wireName.equals(value)) {
            return SyncModels.Module.ASSISTANT;
        }
        return SyncModels.Module.SEND;
    }

    private static SyncModels.Preset findPreset(SyncModels.Snapshot value,
                                                 SyncModels.Module module, UUID id) {
        if (value == null || module == null || id == null) {
            return null;
        }
        for (SyncModels.Preset preset : value.presets(module)) {
            if (id.equals(preset.id)) {
                return preset;
            }
        }
        return null;
    }

    private static UUID parseUuid(String value) {
        try {
            return value == null || value.isEmpty() ? null : UUID.fromString(value);
        } catch (IllegalArgumentException ex) {
            return null;
        }
    }

    private WindowManager.LayoutParams baseOverlayParams(int width, int height) {
        int type = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY : WindowManager.LayoutParams.TYPE_PHONE;
        int flags = WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL
                | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN
                | WindowManager.LayoutParams.FLAG_WATCH_OUTSIDE_TOUCH;
        WindowManager.LayoutParams params = new WindowManager.LayoutParams(
                width, height, type, flags, PixelFormat.TRANSLUCENT);
        return params;
    }

    private WindowManager.LayoutParams ballLayoutParams() {
        int width = dp(58);
        int height = dp(58);
        WindowManager.LayoutParams params = baseOverlayParams(width, height);
        params.gravity = Gravity.TOP | Gravity.START;
        // Preserve the historical right-edge, vertically centred default so an
        // unchanged install still looks the same on first launch.
        params.x = Math.max(dp(8), displayWidth() - width - dp(9));
        params.y = overlayBallY();
        SyncStore store = new SyncStore(this);
        int savedX = store.ballPosX();
        int savedY = store.ballPosY();
        if (savedX >= 0 && savedY >= 0) {
            params.x = clamp(savedX, 0, Math.max(0, displayWidth() - width));
            params.y = clamp(savedY, 0, Math.max(0, displayHeight() - height));
        }
        return params;
    }

    private WindowManager.LayoutParams panelLayoutParams(int width, int height) {
        WindowManager.LayoutParams params = baseOverlayParams(width, height);
        params.gravity = Gravity.TOP | Gravity.START;
        int screenWidth = displayWidth();
        int screenHeight = displayHeight();
        // Anchor the panel to the ball's current position, keeping it within
        // the safe screen area so a dragged ball never results in an
        // unreachable panel.
        int ballX = collapsedParams == null ? screenWidth - width - dp(9) : collapsedParams.x;
        int ballY = collapsedParams == null ? overlayBallY() : collapsedParams.y;
        params.x = clamp(ballX - dp(4), dp(8), Math.max(dp(8), screenWidth - width - dp(8)));
        params.y = clamp(ballY - dp(4), dp(8), Math.max(dp(8), screenHeight - height - dp(8)));
        return params;
    }

    private int overlayBallY() {
        return Math.max(dp(8), (displayHeight() - dp(58)) / 2);
    }

    private int displayWidth() {
        return getResources().getDisplayMetrics().widthPixels;
    }

    private int displayHeight() {
        return getResources().getDisplayMetrics().heightPixels;
    }

    private static int clamp(int value, int min, int max) {
        return Math.max(min, Math.min(max, value));
    }

    private int overlayPanelHeight() {
        int screenHeight = getResources().getDisplayMetrics().heightPixels;
        int maximum = Math.max(dp(260), screenHeight - dp(32));
        return Math.min(dp(360), maximum);
    }

    private Button compactButton(String label, int color) {
        Button button = new Button(this);
        button.setAllCaps(false);
        button.setText(label);
        button.setTextColor(Color.WHITE);
        button.setTextSize(14);
        button.setGravity(Gravity.CENTER);
        button.setMinHeight(dp(44));
        button.setMinimumHeight(dp(44));
        button.setPadding(dp(7), 0, dp(7), 0);
        button.setBackground(statefulBackground(color, pressedColor(color), Color.rgb(48, 48, 52),
                Color.argb(45, 255, 255, 255)));
        installPressFeedback(button);
        return button;
    }

    private void installPressFeedback(View value) {
        value.setOnTouchListener((view, event) -> {
            int action = event.getActionMasked();
            if (action == MotionEvent.ACTION_DOWN) {
                // Respect the device haptic setting while giving touch input
                // an immediate physical acknowledgement.
                view.performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY);
                view.animate().cancel();
                view.animate().scaleX(0.97f).scaleY(0.97f)
                        .setDuration(90L)
                        .setInterpolator(UI_EASE_OUT)
                        .start();
            } else if (action == MotionEvent.ACTION_UP
                    || action == MotionEvent.ACTION_CANCEL) {
                view.animate().cancel();
                view.animate().scaleX(1f).scaleY(1f)
                        .setDuration(140L)
                        .setInterpolator(UI_EASE_OUT)
                        .start();
            }
            return false;
        });
    }

    private void setButtonAppearance(Button button, int normalColor, int pressedColor,
                                     int disabledColor, boolean enabled) {
        if (button == null) {
            return;
        }
        button.setEnabled(enabled);
        button.setTextColor(enabled ? Color.WHITE : Color.rgb(148, 163, 184));
        button.setBackground(statefulBackground(normalColor, pressedColor, disabledColor,
                Color.argb(70, 255, 255, 255)));
    }

    private Drawable statefulBackground(int normalColor, int pressedColor,
                                        int disabledColor, int strokeColor) {
        StateListDrawable content = new StateListDrawable();
        content.addState(new int[]{-android.R.attr.state_enabled},
                roundBackground(disabledColor, Color.argb(40, 148, 163, 184), dp(9), dp(1)));
        content.addState(new int[]{android.R.attr.state_pressed},
                roundBackground(pressedColor, strokeColor, dp(9), dp(1)));
        content.addState(new int[]{android.R.attr.state_focused},
                roundBackground(pressedColor, strokeColor, dp(9), dp(1)));
        content.addState(new int[]{},
                roundBackground(normalColor, strokeColor, dp(9), dp(1)));
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP) {
            return new RippleDrawable(
                    ColorStateList.valueOf(Color.argb(52, 255, 255, 255)),
                    content,
                    roundBackground(Color.WHITE, Color.TRANSPARENT, dp(9), 0));
        }
        return content;
    }

    private int pressedColor(int color) {
        return Color.rgb(Math.max(0, (int) (Color.red(color) * 0.72f)),
                Math.max(0, (int) (Color.green(color) * 0.72f)),
                Math.max(0, (int) (Color.blue(color) * 0.72f)));
    }

    private void updateModuleButtons() {
        for (Map.Entry<String, Button> entry : moduleButtons.entrySet()) {
            boolean active = activeModule != null && entry.getKey().equals(activeModule.wireName);
            int normal = active ? Color.rgb(89, 57, 13) : Color.rgb(27, 27, 31);
            setButtonAppearance(entry.getValue(), normal, pressedColor(normal),
                    active ? normal : Color.rgb(48, 48, 52), true);
        }
    }

    private static void updateBallState(SyncModels.RuntimeStatus value) {
        if (value == null) {
            return;
        }
        switch (value.state) {
            case RUNNING:
            case STARTING:
            case PAUSING:
                ballState = BallState.RUNNING;
                break;
            case PAUSED:
                ballState = BallState.PAUSED;
                break;
            case STOPPING:
                // Keep the last confirmed running/paused visual until stop is confirmed.
                break;
            default:
                ballState = BallState.NORMAL;
                break;
        }
    }

    private void restoreGroupScroll(int scrollY) {
        if (groupsScroll == null) {
            return;
        }
        groupsScroll.post(() -> groupsScroll.scrollTo(0, Math.max(0, scrollY)));
    }

    private TextView overlayText(String text, int size, int color) {
        TextView value = new TextView(this);
        value.setText(text);
        value.setTextSize(size);
        value.setTextColor(color);
        return value;
    }

    private LinearLayout.LayoutParams equalButtonParams(boolean first) {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(0, dp(66), 1f);
        params.setMargins(first ? 0 : dp(5), 0, dp(5), 0);
        return params;
    }

    private LinearLayout.LayoutParams bottomButtonParams(boolean first) {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(0, dp(48), 1f);
        params.setMargins(first ? 0 : dp(2), 0, dp(2), 0);
        return params;
    }

    private LinearLayout.LayoutParams presetCellParams() {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(0, dp(52), 1f);
        // Every preset, including an empty spacer cell, uses the same fixed
        // cell height and symmetric gap.  The label length must not change
        // the measured button size.
        params.setMargins(dp(2), 0, dp(2), 0);
        return params;
    }

    private LinearLayout.LayoutParams verticalParams(int top, int bottom) {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        params.setMargins(0, dp(top), 0, dp(bottom));
        return params;
    }

    private GradientDrawable roundBackground(int fill, int stroke, int radius, int width) {
        GradientDrawable background = new GradientDrawable();
        background.setColor(fill);
        background.setCornerRadius(radius);
        background.setStroke(width, stroke);
        return background;
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density + 0.5f);
    }

    private static String moduleTitle(SyncModels.Module module) {
        switch (module) {
            case PROGRESSION:
                return "递进";
            case ASSISTANT:
                return "助手";
            default:
                return "预设";
        }
    }

    private final class FloatingBallView extends View {
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);

        FloatingBallView(Context context) {
            super(context);
            setLayerType(View.LAYER_TYPE_SOFTWARE, null);
            setElevation(dp(8));
        }

        @Override
        protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);
            float center = getWidth() / 2f;
            float radius = Math.min(getWidth(), getHeight()) / 2f - dp(3);
            int fill = Color.rgb(37, 37, 40);
            if (ballState == BallState.RUNNING) {
                fill = Color.rgb(32, 145, 77);
            } else if (ballState == BallState.PAUSED) {
                fill = Color.rgb(217, 139, 20);
            }
            paint.setStyle(Paint.Style.FILL);
            paint.setColor(fill);
            canvas.drawCircle(center, center, radius, paint);
            paint.setStyle(Paint.Style.STROKE);
            paint.setStrokeWidth(dp(1));
            paint.setColor(Color.argb(200, 203, 213, 225));
            canvas.drawCircle(center, center, radius, paint);
            paint.setStyle(Paint.Style.FILL);
            if (ballState == BallState.RUNNING) {
                paint.setColor(Color.WHITE);
                canvas.drawRect(center - dp(7), center - dp(9), center - dp(3), center + dp(9), paint);
                canvas.drawRect(center + dp(3), center - dp(9), center + dp(7), center + dp(9), paint);
            } else if (ballState == BallState.PAUSED) {
                paint.setColor(Color.WHITE);
                PathTriangle.draw(canvas, center, center, dp(17), paint);
            } else {
                paint.setColor(Color.WHITE);
                float dot = dp(3);
                for (int row = -1; row <= 1; row++) {
                    for (int column = -1; column <= 1; column += 2) {
                        canvas.drawCircle(center + column * dp(9), center + row * dp(9), dot, paint);
                    }
                }
            }
        }
    }

    private static final class PathTriangle {
        static void draw(Canvas canvas, float x, float y, float size, Paint paint) {
            android.graphics.Path path = new android.graphics.Path();
            path.moveTo(x - size / 2f, y - size / 2f);
            path.lineTo(x + size / 2f, y);
            path.lineTo(x - size / 2f, y + size / 2f);
            path.close();
            canvas.drawPath(path, paint);
        }
    }

    private static final class OverlayPanelLayout extends LinearLayout {
        OverlayPanelLayout(Context context) {
            super(context);
        }

        @Override
        public boolean performClick() {
            super.performClick();
            return true;
        }
    }

    private void createNotificationChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) {
            return;
        }
        NotificationChannel channel = new NotificationChannel(
                CHANNEL_ID, getString(R.string.app_name) + " 悬浮控制",
                NotificationManager.IMPORTANCE_LOW);
        channel.setDescription("保持 " + getString(R.string.app_name) + " 悬浮按钮可用");
        NotificationManager manager = getSystemService(NotificationManager.class);
        if (manager != null) {
            manager.createNotificationChannel(channel);
        }
    }

    private void startAsForeground() {
        Intent launchIntent = new Intent(this, MainActivity.class)
                .addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        PendingIntent pendingIntent = PendingIntent.getActivity(this, 0, launchIntent,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Notification.Builder builder = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                ? new Notification.Builder(this, CHANNEL_ID) : new Notification.Builder(this);
        builder.setSmallIcon(android.R.drawable.ic_menu_manage)
                .setContentTitle(getString(R.string.app_name) + " 悬浮控制已开启")
                .setContentText("点击悬浮按钮展开控制面板")
                .setContentIntent(pendingIntent)
                .setOngoing(true)
                .setShowWhen(false)
                .setPriority(Notification.PRIORITY_LOW);
        Notification notification = builder.build();
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            startForeground(NOTIFICATION_ID, notification,
                    android.content.pm.ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
        } else {
            startForeground(NOTIFICATION_ID, notification);
        }
    }

    private void removeOverlayView(View view) {
        if (view == null || windowManager == null) {
            return;
        }
        try {
            windowManager.removeView(view);
        } catch (RuntimeException ignored) {
            // Permission revocation can remove the window before the service callback.
        }
    }
}
