package com.xnas.wpe.battlepanel;

import android.animation.Animator;
import android.animation.AnimatorListenerAdapter;
import android.animation.ValueAnimator;
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
import android.graphics.Paint;
import android.graphics.PixelFormat;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.RippleDrawable;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.SystemClock;
import android.provider.Settings;
import android.view.Gravity;
import android.view.HapticFeedbackConstants;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewConfiguration;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.view.animation.DecelerateInterpolator;
import android.widget.Button;
import android.widget.Spinner;
import android.widget.ArrayAdapter;
import android.widget.AdapterView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import java.io.BufferedReader;
import java.io.File;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;

/** Floating battle panel: local EBP2 reader and bounded resistance query. */
public final class BattleOverlayService extends com.xnas.wpe.suite.ModuleService {
    public static final String ACTION_START = "com.xnas.wpe.battlepanel.action.START";
    public static final String ACTION_STOP = "com.xnas.wpe.battlepanel.action.STOP";
    public static final String EXTRA_PACKAGE = "game_package";

    static final String GAME_PACKAGE_XXXY2026 = "com.gdoo.xxxy2026";
    static final String GAME_PACKAGE_DHXY = "com.gdoo.dhxy";
    private static final String MANAGER_CACHE_FILE = "manager-cache-v2";
    private static final String MANAGER_CACHE_FILE_DHXY = "manager-cache-v2-dhxy";
    private static final String READER_STATUS_PREFIX = "ENEMY_READER_STATUS|";
    private static final String CHANNEL_ID = "battle_panel_overlay";
    private static final int NOTIFICATION_ID = 5201;
    private static final String PREFS_NAME = "battle_panel_overlay";
    private static final String PREF_X = "overlay_x";
    private static final String PREF_Y = "overlay_y";
    private static final long ORIENTATION_POLL_MS = 800L;
    private static final long CLOSE_HOLD_MS = 3_000L;

    private static volatile boolean active;

    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final Runnable orientationCheck = new Runnable() {
        @Override
        public void run() {
            if (!active || activeService != BattleOverlayService.this) {
                return;
            }
            if (!Settings.canDrawOverlays(BattleOverlayService.this)) {
                setPanelState(BattleFrameProtocol.STATUS_READ_ERROR,
                        "悬浮窗权限已撤销，读取器已停止", null);
                stopSelf();
                return;
            }
            boolean landscape = isLandscape();
            if (landscape != lastLandscape) {
                lastLandscape = landscape;
                rebuildVisibleOverlay();
            }
            mainHandler.postDelayed(this, ORIENTATION_POLL_MS);
        }
    };
    private final Runnable freshnessCheck = new Runnable() {
        @Override
        public void run() {
            BattleFrameProtocol.Frame frame = currentFrame;
            if (frame == null || frame.status != BattleFrameProtocol.STATUS_LIVE) {
                return;
            }
            long age = System.currentTimeMillis() - currentFrameTimestampUnixMs;
            if (age > BattleFrameProtocol.MAX_FRAME_AGE_MS) {
                setPanelState(BattleFrameProtocol.STATUS_EXPIRED,
                        "实时数据超过 5 秒未更新，已清除旧数值", null);
            } else {
                long delay = Math.max(250L,
                        BattleFrameProtocol.MAX_FRAME_AGE_MS - Math.max(0L, age) + 25L);
                mainHandler.postDelayed(this, delay);
            }
        }
    };
    private static volatile BattleOverlayService activeService;
    private volatile String targetPackage;

    static String getSelectedGamePackage(Context context) {
        return com.xnas.wpe.suite.TargetGame.getSelectedPackage(context);
    }

    static String getActiveGamePackage(Context context) {
        BattleOverlayService service = activeService;
        return service == null || service.targetPackage == null
                ? getSelectedGamePackage(context) : service.targetPackage;
    }

    private static boolean isAllowedGamePackage(String gamePackage) {
        return GAME_PACKAGE_XXXY2026.equals(gamePackage) || GAME_PACKAGE_DHXY.equals(gamePackage);
    }

    private WindowManager windowManager;
    private View ballView;
    private View panelView;
    private TextView statusText;
    private TextView collapsedBallHint;
    private BallIconView collapsedBallIcon;
    private final SlotRow[] slotRows = new SlotRow[20];
    private final int[] pendingMetricHighlights = new int[20];
    private WindowManager.LayoutParams ballParams;
    private WindowManager.LayoutParams panelParams;
    private boolean stopping;
    private boolean lastLandscape;
    private float dragStartRawX;
    private float dragStartRawY;
    private int dragStartX;
    private int dragStartY;
    private boolean dragging;
    private boolean closeHoldTriggered;
    private long closeHoldStartedAt;
    private int lastCloseCountdown = -1;
    private volatile Process workerProcess;
    private volatile Thread workerThread;
    private volatile String workerStderr = "";
    private boolean resistanceVisible;
    private View attributeBody;
    private View resistanceBody;
    private TextView resistanceDetail;
    private final TextView[] resistancePositions = new TextView[20];
    private final TextView[] resistanceValues = new TextView[20];
    private final TextView[] resistanceHeaders = new TextView[20];
    private int resistanceSelectedMetric;
    private ResistanceQuery resistanceQuery;
    private ResistanceSnapshot resistanceSnapshot;
    private BattleFrameProtocol.Frame resistanceRequestedFrame;
    private String resistanceMessage = "进入战斗后打开抗性页查询";
    private static final String[] RESISTANCE_NAMES = {
            "抗混乱", "抗毒", "抗昏睡", "抗封印", "抗风", "抗雷", "抗水",
            "抗火", "抗震慑", "抗三尸", "抗鬼火", "抗遗忘", "物理吸收"};
    private volatile BattleFrameProtocol.Frame currentFrame;
    private long currentFrameTimestampUnixMs;
    private int currentStatus = BattleFrameProtocol.STATUS_LOCATING;
    private int lastNotifiedStatus = Integer.MIN_VALUE;
    private String currentDetail = "正在启动只读读取器…";
    private final Runnable closeHoldAction = new Runnable() {
        @Override
        public void run() {
            View view = ballView;
            if (view == null || !view.isPressed() || dragging || stopping) {
                return;
            }
            long remainingMs = CLOSE_HOLD_MS - (SystemClock.uptimeMillis() - closeHoldStartedAt);
            if (remainingMs <= 0L) {
                closeHoldTriggered = true;
                view.performHapticFeedback(HapticFeedbackConstants.LONG_PRESS);
                stopSelf();
                return;
            }
            int seconds = (int) ((remainingMs + 999L) / 1000L);
            if (seconds != lastCloseCountdown && collapsedBallHint != null) {
                lastCloseCountdown = seconds;
                collapsedBallHint.setText("松开取消 " + seconds);
            }
            long untilNextSecond = remainingMs - (seconds - 1L) * 1000L;
            mainHandler.postDelayed(this, Math.max(1L, untilNextSecond));
        }
    };

    public static boolean isActive() {
        return active;
    }

    public static boolean isOverlayVisible() {
        BattleOverlayService service = activeService;
        return service != null && (service.panelView != null || service.ballView != null);
    }

    @Override
    public void onCreate() {
        super.onCreate();
        windowManager = (WindowManager) getSystemService(WINDOW_SERVICE);
        resistanceQuery = new ResistanceQuery(this);
        createNotificationChannel();
        activeService = this;
        lastLandscape = isLandscape();
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent != null && ACTION_STOP.equals(intent.getAction())) {
            stopSelf();
            return START_NOT_STICKY;
        }
        String requested = intent == null ? getSelectedGamePackage(this)
                : intent.getStringExtra(EXTRA_PACKAGE);
        if (requested == null || !com.xnas.wpe.suite.TargetGame.supportsModule(
                com.xnas.wpe.suite.TargetGame.BATTLE_PANEL, requested)) {
            startAsForeground();
            setPanelState(BattleFrameProtocol.STATUS_READ_ERROR, "目标游戏不支持", null);
            stopSelf();
            return START_NOT_STICKY;
        }
        if (active && targetPackage != null && !targetPackage.equals(requested)) {
            setPanelState(BattleFrameProtocol.STATUS_READ_ERROR,
                    "当前面板仍绑定另一个目标，请先停止后再切换", null);
            return START_STICKY;
        }
        targetPackage = requested;
        startAsForeground();
        if (!Settings.canDrawOverlays(this)) {
            setPanelState(BattleFrameProtocol.STATUS_READ_ERROR,
                    "缺少悬浮窗权限，请返回应用重新授权", null);
            stopSelf();
            return START_NOT_STICKY;
        }

        stopping = false;
        active = true;
        setPanelState(BattleFrameProtocol.STATUS_LOCATING, "正在启动只读读取器…", null);
        startWorkerIfNeeded(targetPackage);
        mainHandler.removeCallbacks(orientationCheck);
        mainHandler.postDelayed(orientationCheck, ORIENTATION_POLL_MS);
        return START_STICKY;
    }

    @Override
    public void onDestroy() {
        stopping = true;
        resistanceQuery.shutdown();
        mainHandler.removeCallbacks(orientationCheck);
        mainHandler.removeCallbacks(freshnessCheck);
        mainHandler.removeCallbacks(closeHoldAction);
        stopWorker();
        removeOverlay(panelView);
        removeOverlay(ballView);
        panelView = null;
        ballView = null;
        collapsedBallHint = null;
        collapsedBallIcon = null;
        if (activeService == this) {
            active = false;
            activeService = null;
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
            stopForeground(STOP_FOREGROUND_REMOVE);
        } else {
            stopForeground(true);
        }
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    private void showCollapsedBall() {
        if (ballView != null || windowManager == null) {
            return;
        }
        removeOverlay(panelView);
        panelView = null;
        LinearLayout ball = new LinearLayout(this);
        ball.setOrientation(LinearLayout.VERTICAL);
        ball.setGravity(Gravity.CENTER);
        ball.setPadding(dp(4), dp(8), dp(4), dp(8));
        ball.setContentDescription("战斗状态面板；点击展开，长按悬浮球 3 秒关闭");
        BallIconView ballIcon = new BallIconView(this);
        ballIcon.setColor(statusColor(currentStatus));
        TextView closeHint = text("长按3秒关闭", 9.5f, Color.rgb(206, 228, 234));
        closeHint.setGravity(Gravity.CENTER);
        ball.addView(ballIcon, new LinearLayout.LayoutParams(dp(40), dp(40)));
        ball.addView(closeHint, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(17)));
        collapsedBallIcon = ballIcon;
        collapsedBallHint = closeHint;
        GradientDrawable ballMask = new GradientDrawable();
        ballMask.setShape(GradientDrawable.OVAL);
        ballMask.setColor(Color.WHITE);
        ball.setBackground(new RippleDrawable(
                ColorStateList.valueOf(Color.argb(115, 210, 248, 255)),
                ovalBackground(Color.rgb(24, 55, 67), Color.rgb(153, 214, 211), dp(2)),
                ballMask));
        ball.setElevation(dp(8));
        ball.setOnClickListener(view -> expandPanel());
        installDragListener(ball, false);
        ballView = ball;
        ballParams = ballLayoutParams();
        ball.setAlpha(0f);
        ball.setScaleX(0.88f);
        ball.setScaleY(0.88f);
        try {
            windowManager.addView(ballView, ballParams);
            renderPanelState();
            ball.animate().alpha(1f).scaleX(1f).scaleY(1f)
                    .setDuration(160L).setInterpolator(new DecelerateInterpolator(1.5f)).start();
        } catch (RuntimeException error) {
            ballView = null;
            ballParams = null;
            setPanelState(BattleFrameProtocol.STATUS_READ_ERROR,
                    "无法显示悬浮窗，请检查系统悬浮权限", null);
            stopSelf();
        }
    }

    private void expandPanel() {
        if (panelView != null || windowManager == null) {
            return;
        }
        removeOverlay(ballView);
        ballView = null;
        panelView = buildPanelView();
        collapsedBallHint = null;
        collapsedBallIcon = null;
        panelView.setAlpha(0f);
        panelView.setScaleX(0.97f);
        panelView.setScaleY(0.97f);
        panelParams = panelLayoutParams();
        try {
            windowManager.addView(panelView, panelParams);
            renderPanelState();
            panelView.animate().alpha(1f).scaleX(1f).scaleY(1f)
                    .setDuration(180L).setInterpolator(new DecelerateInterpolator(1.5f)).start();
        } catch (RuntimeException error) {
            panelView = null;
            panelParams = null;
            setPanelState(BattleFrameProtocol.STATUS_READ_ERROR,
                    "悬浮面板创建失败，请重新打开面板", null);
            showCollapsedBall();
        }
    }

    private void ensurePanelVisible() {
        if (stopping || activeService != this || panelView != null || ballView != null) {
            return;
        }
        if (!Settings.canDrawOverlays(this)) {
            return;
        }
        expandPanel();
    }

    private void collapsePanel() {
        if (panelParams != null) {
            savePosition(panelParams.x, panelParams.y);
        }
        removeOverlay(panelView);
        panelView = null;
        panelParams = null;
        showCollapsedBall();
    }

    private View buildPanelView() {
        // TYPE_APPLICATION_OVERLAY exposes only whole-screen blur-behind. The local
        // Window background blur API belongs to Activity/Dialog windows, so this
        // Service overlay uses a dark translucent glass surface with a bright edge.
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(12), dp(10), dp(12), dp(11));
        root.setBackground(roundBackground(Color.argb(215, 11, 20, 31),
                Color.argb(245, 151, 204, 220), dp(18), dp(1)));
        root.setElevation(dp(10));

        LinearLayout header = new LinearLayout(this);
        header.setGravity(Gravity.CENTER_VERTICAL);
        header.setOrientation(LinearLayout.HORIZONTAL);
        header.setPadding(dp(2), 0, 0, 0);
        TextView title = text("战斗状态", 19f, Color.WHITE);
        title.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        title.setContentDescription("拖动此处移动战斗状态面板");
        title.setGravity(Gravity.CENTER_VERTICAL | Gravity.RIGHT);

        Button collapse = smallButton("收起");
        collapse.setContentDescription("收起到悬浮球");
        collapse.setOnClickListener(view -> collapsePanel());
        header.addView(collapse, new LinearLayout.LayoutParams(dp(84), dp(48)));
        header.addView(title, new LinearLayout.LayoutParams(0, dp(52), 1f));
        installDragListener(title, true);
        root.addView(header, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(52)));

        statusText = text("正在定位…", 12.7f, Color.rgb(232, 240, 246));
        statusText.setGravity(Gravity.CENTER_VERTICAL);
        statusText.setPadding(dp(10), 0, dp(10), 0);
        statusText.setBackground(roundBackground(Color.argb(228, 27, 45, 63),
                Color.argb(160, 156, 198, 213), dp(9), dp(1)));
        root.addView(statusText, marginParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(34), 0, dp(5), 0, dp(7)));

        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(false);
        boolean landscape = isLandscape();
        LinearLayout body = new LinearLayout(this);
        body.setOrientation(landscape ? LinearLayout.HORIZONTAL : LinearLayout.VERTICAL);
        if (landscape) {
            LinearLayout.LayoutParams ownParams = new LinearLayout.LayoutParams(
                    0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
            ownParams.setMargins(0, 0, dp(5), 0);
            body.addView(buildSideSection("对方", 10), ownParams);
            LinearLayout.LayoutParams opposingParams = new LinearLayout.LayoutParams(
                    0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
            opposingParams.setMargins(dp(5), 0, 0, 0);
            body.addView(buildSideSection("本方", 0), opposingParams);
        } else {
            LinearLayout.LayoutParams ownParams = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
            ownParams.setMargins(0, 0, 0, dp(5));
            body.addView(buildSideSection("对方", 10), ownParams);
            body.addView(buildSideSection("本方", 0),
                    new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,
                            ViewGroup.LayoutParams.WRAP_CONTENT));
        }
        attributeBody = body;
        resistanceBody = buildResistanceBody();
        LinearLayout pages = new LinearLayout(this);
        pages.setOrientation(LinearLayout.VERTICAL);
        pages.addView(attributeBody);
        pages.addView(resistanceBody);
        attributeBody.setVisibility(resistanceVisible ? View.GONE : View.VISIBLE);
        resistanceBody.setVisibility(resistanceVisible ? View.VISIBLE : View.GONE);
        scroll.addView(pages, new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        root.addView(scroll, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        LinearLayout tabs = new LinearLayout(this);
        tabs.setOrientation(LinearLayout.HORIZONTAL);
        Button attributes = smallButton("属性");
        attributes.setContentDescription("显示 HP、MP、SP 属性");
        attributes.setSelected(!resistanceVisible);
        attributes.setOnClickListener(view -> {
            resistanceVisible = false;
            attributeBody.setVisibility(View.VISIBLE);
            resistanceBody.setVisibility(View.GONE);
            renderPanelState();
        });
        Button resistance = smallButton("抗性");
        resistance.setContentDescription("查询并查看本场战斗抗性快照");
        resistance.setSelected(resistanceVisible);
        resistance.setOnClickListener(view -> {
            resistanceVisible = true;
            attributeBody.setVisibility(View.GONE);
            resistanceBody.setVisibility(View.VISIBLE);
            renderPanelState();
        });
        LinearLayout.LayoutParams attributeParams = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.MATCH_PARENT, 1f);
        attributeParams.setMargins(0, 0, dp(4), 0);
        tabs.addView(attributes, attributeParams);
        LinearLayout.LayoutParams resistanceParams = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.MATCH_PARENT, 1f);
        resistanceParams.setMargins(dp(4), 0, 0, 0);
        tabs.addView(resistance, resistanceParams);
        root.addView(tabs, marginParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(48), 0, dp(8), 0, 0));
        return root;
    }

    private View buildSideSection(String name, int firstIndex) {
        return buildSideSection(name, firstIndex, false);
    }

    private View buildSideSection(String name, int firstIndex, boolean resistance) {
        LinearLayout side = new LinearLayout(this);
        side.setOrientation(LinearLayout.VERTICAL);
        side.setPadding(dp(6), dp(5), dp(6), dp(6));
        side.setBackground(roundBackground(Color.argb(154, 17, 31, 47),
                Color.argb(150, 111, 169, 189), dp(13), dp(1)));

        TextView heading = text(name, 15f, Color.WHITE);
        heading.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        heading.setGravity(Gravity.CENTER_VERTICAL);
        side.addView(heading, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(24)));

        LinearLayout formations = new LinearLayout(this);
        formations.setOrientation(LinearLayout.HORIZONTAL);
        LinearLayout.LayoutParams firstFormationParams = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        firstFormationParams.setMargins(0, 0, dp(4), 0);
        boolean ownSide = firstIndex == 0;
        View firstFormation = ownSide
                ? buildFormationSection("宠物", firstIndex + 5, resistance)
                : buildFormationSection("人物", firstIndex, resistance);
        View secondFormation = ownSide
                ? buildFormationSection("人物", firstIndex, resistance)
                : buildFormationSection("宠物", firstIndex + 5, resistance);
        formations.addView(firstFormation, firstFormationParams);
        formations.addView(secondFormation,
                new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        side.addView(formations, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        return side;
    }

    private View buildFormationSection(String name, int firstIndex) {
        return buildFormationSection(name, firstIndex, false);
    }

    private View buildFormationSection(String name, int firstIndex, boolean resistance) {
        LinearLayout section = new LinearLayout(this);
        section.setOrientation(LinearLayout.VERTICAL);
        section.setPadding(dp(7), dp(6), dp(7), dp(7));
        section.setBackground(roundBackground(Color.argb(170, 21, 38, 55),
                Color.argb(132, 130, 180, 198), dp(12), dp(1)));

        TextView heading = text(name, 14f, Color.rgb(226, 240, 247));
        heading.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        heading.setPadding(dp(3), 0, 0, 0);
        section.addView(heading, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(resistance ? 20 : 24)));

        LinearLayout tableHeader = new LinearLayout(this);
        tableHeader.setGravity(Gravity.CENTER_VERTICAL);
        tableHeader.setPadding(dp(2), 0, dp(2), 0);
        tableHeader.addView(metricCell("槽位", resistance ? 1f : 0.9f, true),
                weightedCellParams(resistance ? 1f : 0.9f));
        if (resistance) {
            resistanceHeaders[firstIndex] = metricCell(RESISTANCE_NAMES[resistanceSelectedMetric], 1f, true);
            tableHeader.addView(resistanceHeaders[firstIndex], weightedCellParams(1f));
        } else {
            tableHeader.addView(metricCell("HP", 1f, true), weightedCellParams(1f));
            tableHeader.addView(metricCell("MP", 1f, true), weightedCellParams(1f));
            tableHeader.addView(metricCell("SP", 1f, true), weightedCellParams(1f));
        }
        section.addView(tableHeader, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(resistance ? 20 : 23)));

        for (int i = 0; i < 5; i++) {
            int index = firstIndex + 4 - i;
            LinearLayout row = new LinearLayout(this);
            row.setGravity(Gravity.CENTER_VERTICAL);
            row.setPadding(dp(2), 0, dp(2), 0);
            row.setBackground(roundBackground(Color.argb(155, 31, 48, 64),
                    Color.argb(82, 185, 215, 228), dp(8), dp(1)));
            LinearLayout.LayoutParams rowParams = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, dp(resistance ? 24 : 35));
            rowParams.setMargins(0, dp(2), 0, 0);

            if (resistance) {
                String position = name + (5 - i);
                resistancePositions[index] = metricCell(position, 1f, false);
                resistanceValues[index] = metricCell("未提供", 1f, false);
                resistanceValues[index].setTextColor(Color.rgb(224, 237, 173));
                resistanceValues[index].setSingleLine(true);
                resistanceValues[index].setTextSize(11.2f);
                row.addView(resistancePositions[index], weightedCellParams(1f));
                row.addView(resistanceValues[index], weightedCellParams(1f));
            } else {
                SlotRow dataRow = new SlotRow();
                dataRow.container = row;
                dataRow.position = metricCell("—", 0.9f, false);
                dataRow.hp = metricCell("—", 1f, false);
                dataRow.mp = metricCell("—", 1f, false);
                dataRow.sp = metricCell("—", 1f, false);
                row.addView(dataRow.position, weightedCellParams(0.9f));
                row.addView(dataRow.hp, weightedCellParams(1f));
                row.addView(dataRow.mp, weightedCellParams(1f));
                row.addView(dataRow.sp, weightedCellParams(1f));
                slotRows[index] = dataRow;
            }
            section.addView(row, rowParams);
        }
        return section;
    }

    private TextView metricCell(String value, float weight, boolean header) {
        TextView cell = text(value, header ? 11.5f : 11.2f,
                header ? Color.rgb(197, 219, 232) : Color.WHITE);
        cell.setGravity(Gravity.CENTER);
        if (!header) {
            cell.setTypeface(Typeface.MONOSPACE, Typeface.BOLD);
        }
        return cell;
    }

    private View buildResistanceBody() {
        LinearLayout body = new LinearLayout(this);
        body.setOrientation(LinearLayout.VERTICAL);
        resistanceDetail = text(resistanceMessage, 12f, Color.rgb(193, 218, 229));
        body.addView(resistanceDetail);
        LinearLayout selectorRow = new LinearLayout(this);
        selectorRow.setGravity(Gravity.CENTER_VERTICAL);
        selectorRow.addView(text("抗性项目", 13f, Color.rgb(197, 219, 232)),
                new LinearLayout.LayoutParams(dp(68), dp(32)));
        Spinner selector = new Spinner(this);
        ArrayAdapter<String> adapter = new ArrayAdapter<>(this,
                android.R.layout.simple_spinner_dropdown_item, RESISTANCE_NAMES);
        selector.setAdapter(adapter);
        selector.setSelection(resistanceSelectedMetric);
        selector.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            public void onItemSelected(AdapterView<?> parent, View view, int position, long id) {
                resistanceSelectedMetric = position;
                if (view instanceof TextView) ((TextView) view).setTextColor(Color.WHITE);
                for (TextView header : resistanceHeaders) {
                    if (header != null) header.setText(RESISTANCE_NAMES[position]);
                }
                renderResistance();
            }
            public void onNothingSelected(AdapterView<?> parent) { }
        });
        selectorRow.addView(selector, new LinearLayout.LayoutParams(0, dp(36), 1f));
        body.addView(selectorRow);
        LinearLayout formations = new LinearLayout(this);
        formations.setOrientation(isLandscape() ? LinearLayout.HORIZONTAL : LinearLayout.VERTICAL);
        LinearLayout.LayoutParams sideParams = isLandscape()
                ? new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,
                        ViewGroup.LayoutParams.WRAP_CONTENT);
        if (isLandscape()) sideParams.setMargins(0, 0, dp(5), 0);
        else sideParams.setMargins(0, 0, 0, dp(5));
        formations.addView(buildSideSection("对方", 10, true), sideParams);
        LinearLayout.LayoutParams ownParams = isLandscape()
                ? new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
                : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,
                        ViewGroup.LayoutParams.WRAP_CONTENT);
        if (isLandscape()) ownParams.setMargins(dp(5), 0, 0, 0);
        formations.addView(buildSideSection("本方", 0, true), ownParams);
        body.addView(formations);
        for (TextView header : resistanceHeaders) {
            if (header != null) header.setText(RESISTANCE_NAMES[resistanceSelectedMetric]);
        }
        return body;
    }

    private void renderResistance() {
        BattleFrameProtocol.Frame frame = currentFrame;
        boolean live = frame != null && currentStatus == BattleFrameProtocol.STATUS_LIVE;
        boolean matches = live && resistanceSnapshot != null && resistanceSnapshot.matches(frame);
        if (resistanceSnapshot != null && !matches) {
            resistanceSnapshot = null;
            resistanceMessage = "原快照已清除，等待下一场战斗查询";
        }
        if (resistanceVisible && panelView != null && live && (resistanceRequestedFrame == null
                || !resistanceRequestedFrame.sameProcessAndBattle(frame))) {
            resistanceRequestedFrame = frame;
            resistanceSnapshot = null;
            resistanceMessage = "正在查询本场抗性…";
            resistanceQuery.query(frame, targetPackage == null ? getSelectedGamePackage(this) : targetPackage, (snapshot, error) -> {
                if (stopping) return;
                if (currentFrame == null || !frame.sameProcessAndBattle(currentFrame)) {
                    resistanceMessage = "查询期间战斗状态已变化，结果未采用";
                    renderResistance();
                    return;
                }
                resistanceSnapshot = snapshot != null && snapshot.matches(currentFrame) ? snapshot : null;
                resistanceMessage = resistanceSnapshot == null
                        ? "本场未取得抗性 · " + (error == null ? "返回身份未确认" : error)
                        : "第 " + resistanceSnapshot.sampleRound + " 回合发起的查询快照 · 每场查询一次";
                renderResistance();
            });
        }
        if (resistanceDetail != null) resistanceDetail.setText(live
                ? resistanceMessage : "进入战斗后打开抗性页查询");
        for (int i = 0; i < resistanceValues.length; i++) {
            if (resistanceValues[i] != null) resistanceValues[i].setText(
                    live && resistanceSnapshot != null && resistanceSnapshot.matches(frame)
                            ? resistanceSnapshot.value(frame.slots[i].position,
                                    ResistanceSnapshot.FIRST_CODE + resistanceSelectedMetric)
                            : "未提供");
        }
    }

    private LinearLayout.LayoutParams weightedCellParams(float weight) {
        return new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, weight);
    }

    private void renderPanelState() {
        if (ballView != null) {
            String summary = statusLabel(currentStatus);
            BattleFrameProtocol.Frame collapsedFrame = currentFrame;
            if (collapsedFrame != null && currentStatus == BattleFrameProtocol.STATUS_LIVE) {
                summary = "实时战斗 · 第 " + collapsedFrame.round + " 回合";
            }
            if (currentDetail != null && !currentDetail.isEmpty()) {
                summary += " · " + currentDetail;
            }
            ballView.setContentDescription(summary + "；点击展开，长按悬浮球 3 秒关闭");
            if (collapsedBallIcon != null) {
                collapsedBallIcon.setColor(statusColor(currentStatus));
            }
        }
        if (statusText != null) {
            String label = statusLabel(currentStatus);
            String detail = currentDetail == null || currentDetail.isEmpty()
                    ? "" : " · " + currentDetail;
            BattleFrameProtocol.Frame frame = currentFrame;
            if (frame != null && currentStatus == BattleFrameProtocol.STATUS_LIVE) {
                label = "实时战斗 · 第 " + frame.round + " 回合";
                if (hasUnknownSp(frame)) {
                    label += " · SP — 属性值未提供";
                }
            }
            statusText.setText(label + detail);
            statusText.setTextColor(statusColor(currentStatus));
        }

        BattleFrameProtocol.Frame frame = currentFrame;
        for (int i = 0; i < slotRows.length; i++) {
            SlotRow row = slotRows[i];
            if (row == null) {
                continue;
            }
            int sideIndex = i % BattleFrameProtocol.SIDE_SLOT_COUNT;
            String group = sideIndex < 5 ? "人物" : "宠";
            int number = sideIndex < 5 ? sideIndex + 1 : sideIndex - 4;
            String slotName = group + number;
            if (frame == null || currentStatus != BattleFrameProtocol.STATUS_LIVE) {
                setRow(row, slotName, "—", "—", "—", 0);
                continue;
            }
            BattleFrameProtocol.Slot slot = frame.slots[i];
            switch (slot.state) {
                case BattleFrameProtocol.SLOT_ALIVE:
                case BattleFrameProtocol.SLOT_ALIVE_SP_UNKNOWN:
                    boolean spKnown = slot.state == BattleFrameProtocol.SLOT_ALIVE;
                    setRow(row, slotName, Long.toString(slot.hp),
                            Long.toString(slot.mp), spKnown ? Long.toString(slot.sp) : "—",
                            slot.state);
                    int highlightMask = pendingMetricHighlights[i];
                    if ((highlightMask & 1) != 0) highlightMetric(row.hp, Color.rgb(255, 217, 205));
                    if ((highlightMask & 2) != 0) highlightMetric(row.mp, Color.rgb(205, 226, 255));
                    if (spKnown && (highlightMask & 4) != 0) {
                        highlightMetric(row.sp, Color.rgb(224, 237, 173));
                    }
                    break;
                case BattleFrameProtocol.SLOT_EMPTY:
                    setRow(row, slotName, "—", "—", "—", BattleFrameProtocol.SLOT_EMPTY);
                    break;
                case BattleFrameProtocol.SLOT_DEAD:
                    setRow(row, slotName, "—", "—", "—", BattleFrameProtocol.SLOT_DEAD);
                    break;
                default:
                    setRow(row, slotName + "\n未确认", "—", "—", "—", 0);
                    break;
            }
        }
        java.util.Arrays.fill(pendingMetricHighlights, 0);
        renderResistance();
        updateNotificationText();
    }

    private boolean hasUnknownSp(BattleFrameProtocol.Frame frame) {
        for (BattleFrameProtocol.Slot slot : frame.slots) {
            if (slot.state == BattleFrameProtocol.SLOT_ALIVE_SP_UNKNOWN) {
                return true;
            }
        }
        return false;
    }

    private boolean isAliveState(int state) {
        return state == BattleFrameProtocol.SLOT_ALIVE
                || state == BattleFrameProtocol.SLOT_ALIVE_SP_UNKNOWN;
    }

    private void highlightMetric(TextView view, int color) {
        view.setBackground(roundBackground(Color.argb(72, Color.red(color),
                Color.green(color), Color.blue(color)), Color.TRANSPARENT, dp(5), 0));
        view.postDelayed(() -> view.setBackground(null), 520L);
    }

    private void setRow(SlotRow row, String slot, String hp, String mp, String sp,
                        int state) {
        boolean alive = isAliveState(state);
        row.position.setText(slot);
        animateMetric(row.hp, hp);
        animateMetric(row.mp, mp);
        animateMetric(row.sp, sp);
        int color = alive ? Color.argb(166, 31, 92, 75)
                : state == BattleFrameProtocol.SLOT_EMPTY ? Color.argb(112, 35, 50, 65)
                : state == BattleFrameProtocol.SLOT_DEAD ? Color.argb(174, 67, 49, 57)
                : Color.argb(145, 35, 50, 65);
        row.container.setBackground(roundBackground(color,
                Color.argb(82, 190, 220, 232), dp(8), dp(1)));
        int textColor = alive ? Color.rgb(245, 250, 249)
                : state == BattleFrameProtocol.SLOT_EMPTY ? Color.rgb(161, 179, 190)
                : state == BattleFrameProtocol.SLOT_DEAD ? Color.rgb(225, 188, 186)
                : Color.rgb(221, 231, 238);
        row.position.setTextColor(textColor);
        row.hp.setTextColor(alive ? Color.rgb(255, 217, 205) : textColor);
        row.mp.setTextColor(alive ? Color.rgb(205, 226, 255) : textColor);
        row.sp.setTextColor(alive ? Color.rgb(224, 237, 173) : textColor);
    }

    private void animateMetric(TextView view, String targetText) {
        Object oldAnimation = view.getTag();
        if (oldAnimation instanceof ValueAnimator) {
            ((ValueAnimator) oldAnimation).cancel();
        }

        long from;
        long target;
        try {
            from = Long.parseLong(view.getText().toString());
            target = Long.parseLong(targetText);
        } catch (NumberFormatException ignored) {
            view.setTag(null);
            view.setText(targetText);
            return;
        }
        if (from == target) {
            view.setTag(null);
            view.setText(targetText);
            return;
        }

        ValueAnimator animator = ValueAnimator.ofFloat(0f, 1f);
        animator.setDuration(320L);
        animator.setInterpolator(new DecelerateInterpolator(1.5f));
        animator.addUpdateListener(animation -> {
            float fraction = (float) animation.getAnimatedValue();
            long value = Math.round(from + ((double) target - from) * fraction);
            view.setText(Long.toString(value));
        });
        animator.addListener(new AnimatorListenerAdapter() {
            private boolean cancelled;

            @Override
            public void onAnimationCancel(Animator animation) {
                cancelled = true;
            }

            @Override
            public void onAnimationEnd(Animator animation) {
                if (view.getTag() == animation) {
                    view.setTag(null);
                    if (!cancelled) {
                        view.setText(Long.toString(target));
                    }
                }
            }
        });
        view.setTag(animator);
        animator.start();
    }

    private void setPanelState(int status, String detail, BattleFrameProtocol.Frame frame) {
        if (activeService != null && activeService != this) {
            return;
        }
        currentStatus = status;
        currentDetail = detail;
        if (status != BattleFrameProtocol.STATUS_LIVE) {
            java.util.Arrays.fill(pendingMetricHighlights, 0);
        }
        currentFrame = status == BattleFrameProtocol.STATUS_LIVE ? frame : null;
        currentFrameTimestampUnixMs = currentFrame == null ? 0L : currentFrame.timestampUnixMs;
        mainHandler.removeCallbacks(freshnessCheck);
        if (currentFrame != null) {
            mainHandler.postDelayed(freshnessCheck,
                    Math.max(250L, BattleFrameProtocol.MAX_FRAME_AGE_MS));
        }
        renderPanelState();
    }

    private void acceptFrame(BattleFrameProtocol.Frame frame) {
        if (stopping || activeService != this) {
            return;
        }
        if (frame.status != BattleFrameProtocol.STATUS_LIVE) {
            if (BattleFrameProtocol.shouldRetainLastLiveFrame(currentFrame, frame)) {
                ensurePanelVisible();
                return;
            }
            String detail = statusDetail(frame.status);
            if ((frame.status == BattleFrameProtocol.STATUS_LOCATING
                    || frame.status == BattleFrameProtocol.STATUS_READ_ERROR)
                    && currentStatus != BattleFrameProtocol.STATUS_LIVE
                    && isReaderDiagnosticDetail(currentDetail)) {
                detail = currentDetail;
            }
            setPanelState(frame.status, detail, null);
            ensurePanelVisible();
            return;
        }

        BattleFrameProtocol.Frame old = currentFrame;
        boolean sameBattle = old != null && old.sameProcessAndBattle(frame);
        if (old != null && !sameBattle) {
            setPanelState(BattleFrameProtocol.STATUS_LOCATING,
                    "进程或战斗身份变化，已清除旧数据并重新确认", null);
        }
        if (sameBattle && panelView != null) {
            for (int i = 0; i < pendingMetricHighlights.length; i++) {
                BattleFrameProtocol.Slot previous = old.slots[i];
                BattleFrameProtocol.Slot next = frame.slots[i];
                if (isAliveState(previous.state) && isAliveState(next.state)) {
                    if (previous.hp != next.hp) pendingMetricHighlights[i] |= 1;
                    if (previous.mp != next.mp) pendingMetricHighlights[i] |= 2;
                    if (previous.state == BattleFrameProtocol.SLOT_ALIVE
                            && next.state == BattleFrameProtocol.SLOT_ALIVE
                            && previous.sp != next.sp) {
                        pendingMetricHighlights[i] |= 4;
                    }
                }
            }
        }
        setPanelState(BattleFrameProtocol.STATUS_LIVE, "", frame);
        ensurePanelVisible();
    }

    private void startWorkerIfNeeded(String requestedPackage) {
        Thread current = workerThread;
        if (current != null && current.isAlive()) {
            return;
        }
        if (!isAllowedGamePackage(requestedPackage)
                || !com.xnas.wpe.suite.TargetGame.supportsModule(
                        com.xnas.wpe.suite.TargetGame.BATTLE_PANEL, requestedPackage)) {
            setPanelState(BattleFrameProtocol.STATUS_READ_ERROR, "目标游戏不支持", null);
            ensurePanelVisible();
            return;
        }
        String gamePackage = requestedPackage;
        File worker = new File(getApplicationInfo().nativeLibraryDir, "libenemy_reader.so");
        if (!worker.isFile() || !worker.canExecute()) {
            setPanelState(BattleFrameProtocol.STATUS_READ_ERROR,
                    "读取器不存在或不可执行：libenemy_reader.so", null);
            ensurePanelVisible();
            return;
        }

        stopping = false;
        workerStderr = "";
        setPanelState(BattleFrameProtocol.STATUS_LOCATING, "正在启动只读读取器…", null);
        Thread thread = new Thread(() -> runWorker(worker, gamePackage),
                "battle-panel-reader");
        thread.setDaemon(true);
        workerThread = thread;
        thread.start();
    }

    private void runWorker(File worker, String gamePackage) {
        Thread ownerThread = Thread.currentThread();
        Process process = null;
        Thread stderrThread = null;
        String endReason = "读取器已停止输出";
        boolean malformed = false;
        try {
            String managerCacheFile = GAME_PACKAGE_DHXY.equals(gamePackage)
                    ? MANAGER_CACHE_FILE_DHXY : MANAGER_CACHE_FILE;
            String managerCachePath = new File(getCacheDir(), managerCacheFile)
                    .getAbsolutePath();
            String command = shellQuote(worker.getAbsolutePath())
                    + " --package " + gamePackage
                    + " --cache " + shellQuote(managerCachePath);
            process = new ProcessBuilder("su", "-c", command).start();
            workerProcess = process;
            process.getOutputStream().close();
            Process runningProcess = process;
            stderrThread = new Thread(() -> drainStderr(runningProcess, ownerThread),
                    "battle-panel-reader-stderr");
            stderrThread.setDaemon(true);
            stderrThread.start();

            try (InputStream stdout = process.getInputStream()) {
                while (!stopping) {
                    long length = readFrameLength(stdout);
                    if (length < 0) {
                        break;
                    }
                    if (length > BattleFrameProtocol.MAX_PAYLOAD_BYTES) {
                        malformed = true;
                        endReason = "帧长度超过 1024 字节，已清除旧数据";
                        break;
                    }
                    byte[] payload = readPayload(stdout, (int) length);
                    if (payload.length != BattleFrameProtocol.V2_PAYLOAD_BYTES) {
                        malformed = true;
                        endReason = "帧长度不是版本 2 的 680 字节，已清除旧数据";
                        break;
                    }

                    try {
                        BattleFrameProtocol.Frame frame = BattleFrameProtocol.parse(
                                payload, System.currentTimeMillis());
                        mainHandler.post(() -> {
                            if (stopping || workerThread != ownerThread || activeService != this) {
                                return;
                            }
                            acceptFrame(frame);
                        });
                    } catch (BattleFrameProtocol.FrameException error) {
                        if (error.expired) {
                            mainHandler.post(() -> setPanelState(
                                    BattleFrameProtocol.STATUS_EXPIRED,
                                    "收到过期帧，已清除旧数据", null));
                            continue;
                        }
                        malformed = true;
                        endReason = "收到损坏或不符合协议的数据帧，已清除旧数据";
                        break;
                    }
                }
            }
        } catch (IOException error) {
            endReason = "读取器启动或读取失败";
        } finally {
            if (process != null) {
                process.destroy();
            }
            if (stderrThread != null) {
                try {
                    stderrThread.join(350L);
                } catch (InterruptedException interrupted) {
                    Thread.currentThread().interrupt();
                }
            }
            if (workerThread == ownerThread) {
                workerProcess = null;
            }
            if (!stopping && workerThread == ownerThread) {
                String stderr = workerStderr;
                boolean rootDenied = looksLikeRootDenial(stderr);
                int status = rootDenied
                        ? BattleFrameProtocol.STATUS_ROOT_DENIED
                        : BattleFrameProtocol.STATUS_READ_ERROR;
                String detail = rootDenied
                        ? "su 未授予 Root 权限" : endReason;
                if (malformed) {
                    status = BattleFrameProtocol.STATUS_READ_ERROR;
                }
                String finalDetail = detail;
                int finalStatus = status;
                mainHandler.post(() -> {
                    if (stopping || workerThread != ownerThread) {
                        return;
                    }
                    setPanelState(finalStatus, finalDetail, null);
                    ensurePanelVisible();
                });
            }
        }
    }

    private void drainStderr(Process process, Thread ownerThread) {
        try (BufferedReader reader = new BufferedReader(new InputStreamReader(
                process.getErrorStream(), StandardCharsets.UTF_8))) {
            String line;
            while ((line = reader.readLine()) != null) {
                synchronized (this) {
                    String next = workerStderr + line + "\n";
                    workerStderr = next.length() > 2_048
                            ? next.substring(next.length() - 2_048) : next;
                }
                publishReaderDiagnostic(line, ownerThread);
            }
        } catch (IOException ignored) {
            // Process exit normally closes stderr.
        }
    }

    private void publishReaderDiagnostic(String line, Thread ownerThread) {
        if (line.length() > 160 || !line.startsWith(READER_STATUS_PREFIX)) {
            return;
        }
        String[] fields = line.split("\\|", -1);
        if (fields.length < 4 || !READER_STATUS_PREFIX.substring(0,
                READER_STATUS_PREFIX.length() - 1).equals(fields[0])) {
            return;
        }

        String stage = fields[1];
        String detail;
        int status;
        if (("tables".equals(stage) || "role_nodes".equals(stage))
                && fields.length == 4) {
            long completed;
            long total;
            try {
                completed = Long.parseLong(fields[2]);
                total = Long.parseLong(fields[3]);
            } catch (NumberFormatException ignored) {
                return;
            }
            if (completed < 0 || total <= 0 || completed > total) {
                return;
            }
            int percent = (int) Math.min(100L, Math.round(completed * 100.0 / total));
            detail = "tables".equals(stage)
                    ? "扫描管理表 " + percent + "%"
                    : "扫描角色节点 " + percent + "%";
            status = BattleFrameProtocol.STATUS_LOCATING;
        } else if ("retry".equals(stage) && fields.length == 5
                && "0".equals(fields[2]) && "0".equals(fields[3])) {
            String code = fields[4];
            if ("manager_not_found".equals(code)) {
                detail = "上次扫描未找到管理表，稍后重试";
            } else if ("manager_ambiguous".equals(code)) {
                detail = "上次扫描找到多个候选，稍后重试";
            } else if ("battle_data_invalid".equals(code)) {
                detail = "已找到战斗管理表，但角色队伍数据未通过校验";
            } else if ("read_error".equals(code)) {
                detail = "上次内存读取失败，稍后重试";
            } else if ("process_changed".equals(code)) {
                detail = "游戏进程变化，正在重新定位";
            } else if ("sample_changed".equals(code)) {
                detail = "战斗数据正在变化，稍后重读";
            } else {
                return;
            }
            status = BattleFrameProtocol.STATUS_READ_ERROR;
        } else {
            return;
        }

        String finalDetail = detail;
        int finalStatus = status;
        mainHandler.post(() -> {
            if (stopping || workerThread != ownerThread || activeService != this
                    || currentStatus == BattleFrameProtocol.STATUS_LIVE) {
                return;
            }
            setPanelState(finalStatus, finalDetail, null);
        });
    }

    private boolean isReaderDiagnosticDetail(String detail) {
        return detail != null && (detail.startsWith("扫描管理表 ")
                || detail.startsWith("扫描角色节点 ")
                || detail.startsWith("上次扫描")
                || detail.startsWith("已找到战斗管理表")
                || detail.startsWith("上次内存读取失败")
                || detail.startsWith("游戏进程变化，正在重新定位")
                || detail.startsWith("战斗数据正在变化"));
    }

    private static long readFrameLength(InputStream input) throws IOException {
        int first = input.read();
        if (first < 0) {
            return -1L;
        }
        int second = input.read();
        int third = input.read();
        int fourth = input.read();
        if (second < 0 || third < 0 || fourth < 0) {
            throw new IOException("partial frame length");
        }
        return ((long) first << 24) | ((long) second << 16)
                | ((long) third << 8) | (long) fourth;
    }

    private static byte[] readPayload(InputStream input, int length) throws IOException {
        byte[] payload = new byte[length];
        int offset = 0;
        while (offset < length) {
            int count = input.read(payload, offset, length - offset);
            if (count < 0) {
                throw new IOException("partial frame payload");
            }
            offset += count;
        }
        return payload;
    }

    private void stopWorker() {
        Process process = workerProcess;
        if (process != null) {
            process.destroy();
        }
        Thread thread = workerThread;
        if (thread != null) {
            thread.interrupt();
            try {
                thread.join(500L);
            } catch (InterruptedException error) {
                Thread.currentThread().interrupt();
            }
        }
        workerThread = null;
        workerProcess = null;
        currentFrame = null;
    }

    private void updateNotificationText() {
        if (!active || Build.VERSION.SDK_INT < Build.VERSION_CODES.O) {
            return;
        }
        if (currentStatus == lastNotifiedStatus) {
            return;
        }
        Notification notification = buildNotification();
        NotificationManager manager = getSystemService(NotificationManager.class);
        if (manager != null) {
            manager.notify(NOTIFICATION_ID, notification);
            lastNotifiedStatus = currentStatus;
        }
    }

    private void createNotificationChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) {
            return;
        }
        NotificationChannel channel = new NotificationChannel(CHANNEL_ID,
                "飘渺助手" + " 前台服务", NotificationManager.IMPORTANCE_LOW);
        channel.setDescription("保持战斗状态与抗性查询面板运行");
        NotificationManager manager = getSystemService(NotificationManager.class);
        if (manager != null) {
            manager.createNotificationChannel(channel);
        }
    }

    private void startAsForeground() {
        Notification notification = buildNotification();
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            startForeground(NOTIFICATION_ID, notification,
                    android.content.pm.ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
        } else {
            startForeground(NOTIFICATION_ID, notification);
        }
        lastNotifiedStatus = currentStatus;
    }

    private Notification buildNotification() {
        Intent open = new Intent(this, com.xnas.wpe.suite.MainActivity.class).putExtra(com.xnas.wpe.suite.MainActivity.EXTRA_OPEN_MODULE_ID, "battlepanel")
                .addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        PendingIntent contentIntent = PendingIntent.getActivity(this, 5201, open,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Intent stop = new Intent(this, BattleOverlayService.class).setAction(ACTION_STOP);
        PendingIntent stopIntent = PendingIntent.getService(this, 2, stop,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Notification.Builder builder = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                ? new Notification.Builder(this, CHANNEL_ID) : new Notification.Builder(this);
        builder.setSmallIcon(android.R.drawable.ic_menu_info_details)
                .setContentTitle("飘渺助手")
                .setContentText(statusLabel(currentStatus))
                .setContentIntent(contentIntent)
                .setOngoing(true)
                .setShowWhen(false)
                .setPriority(Notification.PRIORITY_LOW)
                .addAction(android.R.drawable.ic_menu_close_clear_cancel,
                        "停止读取", stopIntent);
        return builder.build();
    }

    private void rebuildVisibleOverlay() {
        if (panelView != null) {
            int oldX = panelParams == null ? dp(8) : panelParams.x;
            int oldY = panelParams == null ? dp(8) : panelParams.y;
            removeOverlay(panelView);
            panelView = buildPanelView();
            panelParams = panelLayoutParams();
            panelParams.x = clamp(oldX, dp(8), Math.max(dp(8), displayWidth() - panelParams.width - dp(8)));
            panelParams.y = clamp(oldY, dp(24),
                    Math.max(dp(24), displayHeight() - panelParams.height - dp(24)));
            try {
                windowManager.addView(panelView, panelParams);
                renderPanelState();
            } catch (RuntimeException error) {
                panelView = null;
                panelParams = null;
                showCollapsedBall();
            }
        } else if (ballView != null && ballParams != null) {
            ballParams.width = dp(88);
            ballParams.height = dp(88);
            ballParams.x = clamp(ballParams.x, 0, Math.max(0, displayWidth() - ballParams.width));
            ballParams.y = clamp(ballParams.y, 0, Math.max(0, displayHeight() - ballParams.height));
            try {
                windowManager.updateViewLayout(ballView, ballParams);
                savePosition(ballParams.x, ballParams.y);
            } catch (RuntimeException ignored) {
                // Permission revocation is handled by the service permission check below.
            }
        }
    }

    private void installDragListener(View view, boolean movingPanel) {
        view.setOnTouchListener((dragged, event) -> {
            int action = event.getActionMasked();
            switch (action) {
                case MotionEvent.ACTION_DOWN:
                    if (!movingPanel) {
                        dragged.setPressed(true);
                        closeHoldTriggered = false;
                        closeHoldStartedAt = SystemClock.uptimeMillis();
                        lastCloseCountdown = 3;
                        if (collapsedBallHint != null) {
                            collapsedBallHint.setText("松开取消 3");
                        }
                        mainHandler.removeCallbacks(closeHoldAction);
                        mainHandler.postDelayed(closeHoldAction, 1_000L);
                    }
                    dragged.performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY);
                    dragStartRawX = event.getRawX();
                    dragStartRawY = event.getRawY();
                    dragStartX = movingPanel
                            ? (panelParams == null ? 0 : panelParams.x)
                            : (ballParams == null ? 0 : ballParams.x);
                    dragStartY = movingPanel
                            ? (panelParams == null ? 0 : panelParams.y)
                            : (ballParams == null ? 0 : ballParams.y);
                    dragging = false;
                    return true;
                case MotionEvent.ACTION_MOVE:
                    float dx = event.getRawX() - dragStartRawX;
                    float dy = event.getRawY() - dragStartRawY;
                    if (!dragging) {
                        int slop = ViewConfiguration.get(BattleOverlayService.this)
                                .getScaledTouchSlop();
                        if (!movingPanel) {
                            slop = Math.round(slop * 1.5f);
                        }
                        if (Math.abs(dx) > slop || Math.abs(dy) > slop) {
                            dragging = true;
                        }
                    }
                    if (dragging) {
                        if (!movingPanel) {
                            resetCloseHoldHint();
                            dragged.setPressed(false);
                        }
                        WindowManager.LayoutParams params = movingPanel ? panelParams : ballParams;
                        View target = movingPanel ? panelView : ballView;
                        if (params != null && target != null) {
                            int minY = movingPanel ? dp(24) : 0;
                            int bottomInset = movingPanel ? dp(24) : 0;
                            params.x = clamp(dragStartX + Math.round(dx), 0,
                                    Math.max(0, displayWidth() - params.width));
                            params.y = clamp(dragStartY + Math.round(dy), minY,
                                    Math.max(minY, displayHeight() - params.height - bottomInset));
                            try {
                                windowManager.updateViewLayout(target, params);
                            } catch (RuntimeException ignored) {
                                // Permission revocation can interrupt a drag.
                            }
                        }
                    }
                    return true;
                case MotionEvent.ACTION_UP:
                    resetCloseHoldHint();
                    dragged.setPressed(false);
                    if (!dragging && !movingPanel && !closeHoldTriggered) {
                        dragged.performClick();
                    } else {
                        WindowManager.LayoutParams params = movingPanel ? panelParams : ballParams;
                        if (params != null) {
                            savePosition(params.x, params.y);
                        }
                    }
                    dragging = false;
                    closeHoldTriggered = false;
                    return true;
                case MotionEvent.ACTION_CANCEL:
                    resetCloseHoldHint();
                    dragged.setPressed(false);
                    dragging = false;
                    closeHoldTriggered = false;
                    return true;
                default:
                    return false;
            }
        });
    }

    private WindowManager.LayoutParams ballLayoutParams() {
        int size = dp(88);
        WindowManager.LayoutParams params = baseOverlayParams(size, size);
        params.gravity = Gravity.TOP | Gravity.START;
        int savedX = getPreferences().getInt(PREF_X, -1);
        int savedY = getPreferences().getInt(PREF_Y, -1);
        params.x = savedX < 0 ? Math.max(dp(8), displayWidth() - size - dp(10))
                : clamp(savedX, 0, Math.max(0, displayWidth() - size));
        params.y = savedY < 0 ? Math.max(dp(8), (displayHeight() - size) / 2)
                : clamp(savedY, 0, Math.max(0, displayHeight() - size));
        return params;
    }

    private void resetCloseHoldHint() {
        mainHandler.removeCallbacks(closeHoldAction);
        closeHoldStartedAt = 0L;
        lastCloseCountdown = -1;
        if (collapsedBallHint != null) {
            collapsedBallHint.setText("长按3秒关闭");
        }
    }

    private WindowManager.LayoutParams panelLayoutParams() {
        int maxWidth = Math.max(dp(180), displayWidth() - dp(20));
        int width = Math.min(dp(isLandscape() ? 820 : 560), maxWidth);
        int maxHeight = Math.max(dp(180), displayHeight() - dp(52));
        int height = Math.min(dp(isLandscape() ? 480 : 690), maxHeight);
        WindowManager.LayoutParams params = baseOverlayParams(width, height);
        params.gravity = Gravity.TOP | Gravity.START;
        int anchorX = ballParams == null ? displayWidth() / 2 : ballParams.x;
        int anchorY = ballParams == null ? displayHeight() / 2 : ballParams.y;
        params.x = clamp(anchorX - width / 2, dp(8), Math.max(dp(8), displayWidth() - width - dp(8)));
        params.y = clamp(anchorY - height / 2, dp(24),
                Math.max(dp(24), displayHeight() - height - dp(24)));
        return params;
    }

    private WindowManager.LayoutParams baseOverlayParams(int width, int height) {
        int type = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
                : WindowManager.LayoutParams.TYPE_PHONE;
        int flags = WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL
                | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN;
        return new WindowManager.LayoutParams(width, height, type, flags, PixelFormat.TRANSLUCENT);
    }

    private void savePosition(int x, int y) {
        getPreferences().edit().putInt(PREF_X, x).putInt(PREF_Y, y).apply();
    }

    private android.content.SharedPreferences getPreferences() {
        return getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
    }

    private void removeOverlay(View view) {
        if (view == null || windowManager == null) {
            return;
        }
        try {
            windowManager.removeView(view);
        } catch (RuntimeException ignored) {
            // Permission revocation can remove the window before service cleanup.
        }
    }

    private GradientDrawable roundBackground(int fill, int stroke, int radius, int strokeWidth) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setColor(fill);
        drawable.setCornerRadius(radius);
        drawable.setStroke(strokeWidth, stroke);
        return drawable;
    }

    private GradientDrawable ovalBackground(int fill, int stroke, int strokeWidth) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setShape(GradientDrawable.OVAL);
        drawable.setColor(fill);
        drawable.setStroke(strokeWidth, stroke);
        return drawable;
    }

    private TextView text(String value, float sizeSp, int color) {
        TextView result = new TextView(this);
        result.setText(value);
        result.setTextSize(sizeSp * 0.95f);
        result.setTextColor(color);
        result.setIncludeFontPadding(false);
        return result;
    }

    private Button smallButton(String label) {
        Button button = new Button(this);
        button.setText(label);
        button.setTextSize(14.5f);
        button.setAllCaps(false);
        button.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        button.setTextColor(Color.WHITE);
        button.setMinHeight(0);
        button.setMinimumHeight(0);
        button.setPadding(dp(8), 0, dp(8), 0);
        button.setBackground(roundBackground(Color.argb(248, 27, 102, 126),
                Color.argb(255, 173, 239, 247), dp(11), dp(2)));
        return button;
    }

    private LinearLayout.LayoutParams marginParams(int width, int height,
                                                    int left, int top, int right, int bottom) {
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(width, height);
        params.setMargins(dp(left), dp(top), dp(right), dp(bottom));
        return params;
    }

    private int statusColor(int status) {
        switch (status) {
            case BattleFrameProtocol.STATUS_LIVE:
                return Color.rgb(180, 244, 212);
            case BattleFrameProtocol.STATUS_ROOT_DENIED:
            case BattleFrameProtocol.STATUS_READ_ERROR:
                return Color.rgb(255, 190, 178);
            case BattleFrameProtocol.STATUS_EXPIRED:
                return Color.rgb(255, 218, 155);
            default:
                return Color.rgb(211, 226, 236);
        }
    }

    private String statusLabel(int status) {
        switch (status) {
            case BattleFrameProtocol.STATUS_NO_GAME:
                return "未检测到游戏";
            case BattleFrameProtocol.STATUS_LOCATING:
                return "正在定位";
            case BattleFrameProtocol.STATUS_NOT_IN_BATTLE:
                return "未进战";
            case BattleFrameProtocol.STATUS_LIVE:
                return "实时战斗 · 双方各十槽";
            case BattleFrameProtocol.STATUS_ROOT_DENIED:
                return "Root 拒绝";
            case BattleFrameProtocol.STATUS_READ_ERROR:
                return "读取错误";
            case BattleFrameProtocol.STATUS_EXPIRED:
                return "数据已过期";
            default:
                return "未知状态";
        }
    }

    private String statusDetail(int status) {
        switch (status) {
            case BattleFrameProtocol.STATUS_NO_GAME:
                return "未检测到目标游戏进程";
            case BattleFrameProtocol.STATUS_LOCATING:
                return "正在定位战斗数据";
            case BattleFrameProtocol.STATUS_NOT_IN_BATTLE:
                return "游戏当前未进入战斗";
            case BattleFrameProtocol.STATUS_ROOT_DENIED:
                return "su 未授予 Root 权限";
            case BattleFrameProtocol.STATUS_READ_ERROR:
                return "读取器报告读取错误";
            case BattleFrameProtocol.STATUS_EXPIRED:
                return "读取器报告数据过期";
            default:
                return "";
        }
    }

    private boolean isLandscape() {
        return getResources().getConfiguration().orientation
                == android.content.res.Configuration.ORIENTATION_LANDSCAPE;
    }

    private int displayWidth() {
        return getResources().getDisplayMetrics().widthPixels;
    }

    private int displayHeight() {
        return getResources().getDisplayMetrics().heightPixels;
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density * 0.9f + 0.5f);
    }

    private static int clamp(int value, int min, int max) {
        return Math.max(min, Math.min(max, value));
    }

    private static String shellQuote(String value) {
        return "'" + value.replace("'", "'\\''") + "'";
    }

    private static boolean looksLikeRootDenial(String stderr) {
        if (stderr == null) {
            return false;
        }
        String lower = stderr.toLowerCase(java.util.Locale.ROOT);
        return lower.contains("su: request rejected")
                || lower.contains("su: permission denied")
                || lower.contains("su: access denied")
                || lower.contains("su: not allowed")
                || lower.contains("sureceiver action deny")
                || lower.contains("root permission denied")
                || lower.contains("root access denied")
                || lower.contains("no root");
    }

    private static final class SlotRow {
        LinearLayout container;
        TextView position;
        TextView hp;
        TextView mp;
        TextView sp;
    }

    private static final class BallIconView extends View {
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF screen = new RectF(-13f, -11f, 13f, 9f);

        BallIconView(Context context) {
            super(context);
            paint.setColor(Color.WHITE);
            paint.setStrokeWidth(2f);
            paint.setStrokeCap(Paint.Cap.ROUND);
            paint.setStrokeJoin(Paint.Join.ROUND);
        }

        void setColor(int color) {
            paint.setColor(color);
            invalidate();
        }

        @Override
        protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);
            float scale = Math.min(getWidth(), getHeight()) / 36f;
            canvas.save();
            canvas.translate(getWidth() / 2f, getHeight() / 2f);
            canvas.scale(scale, scale);
            paint.setStyle(Paint.Style.STROKE);
            canvas.drawRoundRect(screen, 3f, 3f, paint);
            paint.setStyle(Paint.Style.FILL);
            canvas.drawRoundRect(-8f, -6f, 4f, -4f, 1f, 1f, paint);
            canvas.drawRoundRect(-8f, -1f, 8f, 1f, 1f, 1f, paint);
            canvas.drawRoundRect(-8f, 4f, 2f, 6f, 1f, 1f, paint);
            paint.setStyle(Paint.Style.STROKE);
            canvas.drawLine(0f, 9f, 0f, 14f, paint);
            canvas.drawLine(-6f, 14f, 6f, 14f, paint);
            canvas.restore();
        }
    }
}
