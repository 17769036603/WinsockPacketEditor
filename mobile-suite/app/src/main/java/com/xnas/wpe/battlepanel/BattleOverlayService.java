package com.xnas.wpe.battlepanel;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.os.Binder;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.graphics.PixelFormat;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.StateListDrawable;
import android.provider.Settings;
import android.util.DisplayMetrics;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewConfiguration;
import android.view.WindowManager;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.Toast;
import android.widget.TextView;

import java.io.BufferedReader;
import java.io.File;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;

/** Background EBP2 reader and bounded resistance query. */
public final class BattleOverlayService extends com.xnas.wpe.suite.ModuleService {
    public static final String ACTION_START = "com.xnas.wpe.battlepanel.action.START";
    public static final String ACTION_STOP = "com.xnas.wpe.battlepanel.action.STOP";
    public static final String EXTRA_PACKAGE = "game_package";

    static final String GAME_PACKAGE_XXXY2026 = "com.gdoo.xxxy2026";
    static final String GAME_PACKAGE_DHXY = "com.gdoo.dhxy";
    private static final String MANAGER_CACHE_FILE = "manager-cache-v2";
    private static final String MANAGER_CACHE_FILE_DHXY = "manager-cache-v2-dhxy";
    private static final String READER_STATUS_PREFIX = "ENEMY_READER_STATUS|";
    private static final String CHANNEL_ID = "battle_panel_reader";
    private static final int NOTIFICATION_ID = 5201;

    private static volatile boolean active;
    private volatile boolean stopping;

    private final Handler mainHandler = new Handler(Looper.getMainLooper());
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
    private final LocalBinder binder = new LocalBinder();

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

    private volatile Process workerProcess;
    private volatile Thread workerThread;
    private volatile String workerStderr = "";
    private ResistanceQuery resistanceQuery;
    private ResistanceSnapshot resistanceSnapshot;
    private BattleFrameProtocol.Frame resistanceRequestedFrame;
    private String resistanceMessage = "";
    private volatile BattleFrameProtocol.Frame currentFrame;
    private long currentFrameTimestampUnixMs;
    private int currentStatus = BattleFrameProtocol.STATUS_LOCATING;
    private int lastNotifiedStatus = Integer.MIN_VALUE;
    private String currentDetail = "正在启动只读读取器…";
    private WindowManager windowManager;
    private android.content.Context overlayContext;
    private View overlayPanelView;
    private View overlayBallView;
    private FrameLayout overlayBallHostView;
    private WindowManager.LayoutParams panelParams;
    // Window geometry expands during an active touch; ballParams remains the 76dp ball geometry.
    private WindowManager.LayoutParams ballHostParams;
    private WindowManager.LayoutParams ballParams;
    private int lastBallCenterX;
    private int lastBallCenterY;
    private boolean hasLastBallCenter;
    private BattleOverlayRenderer overlayRenderer;

    final class LocalBinder extends Binder {
        BattleOverlayService getService() {
            return BattleOverlayService.this;
        }
    }

    static final class DisplayState {
        final boolean active;
        final String gamePackage;
        final int status;
        final String label;
        final String detail;
        final BattleFrameProtocol.Frame frame;
        final ResistanceSnapshot resistance;
        final String resistanceMessage;

        DisplayState(boolean active, String gamePackage, int status, String label, String detail,
                     BattleFrameProtocol.Frame frame, ResistanceSnapshot resistance,
                     String resistanceMessage) {
            this.active = active;
            this.gamePackage = gamePackage;
            this.status = status;
            this.label = label;
            this.detail = detail;
            this.frame = frame;
            this.resistance = resistance;
            this.resistanceMessage = resistanceMessage;
        }
    }

    public static boolean isActive() {
        return active;
    }

    DisplayState getDisplayState() {
        boolean running = active && !stopping;
        String label = running || currentStatus == BattleFrameProtocol.STATUS_READ_ERROR
                ? statusLabel(currentStatus) : "未启动";
        String detail = running || currentStatus == BattleFrameProtocol.STATUS_READ_ERROR
                ? currentDetail : "后台读取服务未运行";
        return new DisplayState(running, targetPackage, currentStatus, label, detail,
                currentFrame, resistanceSnapshot, resistanceMessage);
    }

    void requestResistanceIfNeeded() {
        if (Looper.myLooper() != Looper.getMainLooper()) {
            mainHandler.post(this::requestResistanceIfNeeded);
            return;
        }
        BattleFrameProtocol.Frame frame = currentFrame;
        if (!active || stopping || frame == null
                || currentStatus != BattleFrameProtocol.STATUS_LIVE) {
            resistanceMessage = "";
            renderOverlay();
            return;
        }
        if (resistanceSnapshot != null && resistanceSnapshot.matches(frame)) {
            return;
        }
        if (resistanceRequestedFrame != null
                && resistanceRequestedFrame.sameProcessAndBattle(frame)) {
            return;
        }
        resistanceRequestedFrame = frame;
        resistanceSnapshot = null;
        resistanceMessage = "正在查询抗性…";
        renderOverlay();
        String packageName = targetPackage == null ? getSelectedGamePackage(this) : targetPackage;
        resistanceQuery.query(frame, packageName, (snapshot, error) -> {
            if (stopping || activeService != this) return;
            BattleFrameProtocol.Frame latest = currentFrame;
            if (latest == null || !frame.sameProcessAndBattle(latest)) {
                resistanceMessage = "战斗状态已变化，结果未采用";
                resistanceSnapshot = null;
                renderOverlay();
                return;
            }
            resistanceSnapshot = snapshot != null && snapshot.matches(latest) ? snapshot : null;
            resistanceMessage = resistanceSnapshot == null
                    ? "本场未取得抗性 · " + (error == null ? "返回身份未确认" : error)
                    : "第 " + resistanceSnapshot.sampleRound + " 回合查询结果";
            renderOverlay();
        });
    }

    void requestStop() {
        stopReading(BattleFrameProtocol.STATUS_LOCATING, "后台读取已停止");
    }

    private void stopReading(int status, String detail) {
        stopping = true;
        active = false;
        resistanceQuery.cancel();
        resistanceSnapshot = null;
        resistanceRequestedFrame = null;
        resistanceMessage = "";
        mainHandler.removeCallbacksAndMessages(null);
        stopWorker();
        removeOverlayWindows();
        setPanelState(status, detail, null);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
            stopForeground(STOP_FOREGROUND_REMOVE);
        } else {
            stopForeground(true);
        }
        stopSelf();
    }

    @Override
    public void onCreate() {
        super.onCreate();
        windowManager = (WindowManager) getSystemService(WINDOW_SERVICE);
        overlayContext = com.xnas.wpe.suite.CompactUi.wrap(this);
        resistanceQuery = new ResistanceQuery(this);
        createNotificationChannel();
        activeService = this;
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        startAsForeground();
        if (intent != null && ACTION_STOP.equals(intent.getAction())) {
            requestStop();
            return START_NOT_STICKY;
        }
        if (!com.xnas.wpe.suite.LicenseManager.isActive()) { stopForeground(true); stopSelfResult(startId); return START_NOT_STICKY; }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M && !Settings.canDrawOverlays(this)) {
            if (active && !stopping) {
                removeOverlayWindows();
                Toast.makeText(this, "悬浮窗权限已关闭，后台读取继续运行",
                        Toast.LENGTH_LONG).show();
                return START_STICKY;
            }
            stopReading(BattleFrameProtocol.STATUS_READ_ERROR,
                    "缺少悬浮窗权限，读取未启动");
            return START_NOT_STICKY;
        }
        String requested = intent == null ? getSelectedGamePackage(this)
                : intent.getStringExtra(EXTRA_PACKAGE);
        if (requested == null || !com.xnas.wpe.suite.TargetGame.supportsModule(
                com.xnas.wpe.suite.TargetGame.BATTLE_PANEL, requested)) {
            if (active && !stopping) {
                Toast.makeText(this, "目标不支持，当前读取继续运行",
                        Toast.LENGTH_LONG).show();
                return START_STICKY;
            }
            stopReading(BattleFrameProtocol.STATUS_READ_ERROR, "目标游戏不支持");
            return START_NOT_STICKY;
        }
        if (active && targetPackage != null) {
            if (!targetPackage.equals(requested)) {
                Toast.makeText(this, "当前面板仍绑定另一个目标，请先停止后再切换",
                        Toast.LENGTH_LONG).show();
            }
            if (!showExpandedOverlay()) {
                stopReading(BattleFrameProtocol.STATUS_READ_ERROR,
                        "悬浮面板无法展开，读取已停止");
                return START_NOT_STICKY;
            }
            return START_STICKY;
        }
        targetPackage = requested;
        resistanceSnapshot = null;
        resistanceRequestedFrame = null;
        resistanceMessage = "";

        stopping = false;
        active = true;
        setPanelState(BattleFrameProtocol.STATUS_LOCATING, "正在启动只读读取器…", null);
        if (!showExpandedOverlay()) {
            stopReading(BattleFrameProtocol.STATUS_READ_ERROR,
                    "悬浮面板创建失败，读取未启动");
            return START_NOT_STICKY;
        }
        startWorkerIfNeeded(targetPackage);
        return START_STICKY;
    }

    @Override
    public void onDestroy() {
        stopping = true;
        active = false;
        resistanceQuery.shutdown();
        mainHandler.removeCallbacksAndMessages(null);
        stopWorker();
        removeOverlayWindows();
        overlayRenderer = null;
        overlayContext = null;
        windowManager = null;
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
        return binder;
    }

    private boolean showExpandedOverlay() {
        if (windowManager == null || overlayContext == null
                || (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M
                && !Settings.canDrawOverlays(this))) {
            return false;
        }
        if (overlayPanelView != null) {
            if (overlayRenderer != null) overlayRenderer.render(getDisplayState());
            return true;
        }

        int anchorX = ballParams == null
                ? (hasLastBallCenter ? lastBallCenterX : displayWidth() / 2)
                : ballParams.x + ballParams.width / 2;
        int anchorY = ballParams == null
                ? (hasLastBallCenter ? lastBallCenterY : displayHeight() / 2)
                : ballParams.y + ballParams.height / 2;
        rememberBallCenter(anchorX, anchorY);
        removeOverlay(overlayBallHostView);
        overlayBallHostView = null;
        overlayBallView = null;
        ballHostParams = null;
        ballParams = null;

        if (overlayRenderer == null) {
            overlayRenderer = new BattleOverlayRenderer(this,
                    new BattleOverlayRenderer.Listener() {
                        @Override public void onCollapseRequested() {
                            collapseOverlay();
                        }

                        @Override public void onResistancePageSelected() {
                            requestResistanceIfNeeded();
                        }
                    });
        }
        View panel = overlayRenderer.getView();
        WindowManager.LayoutParams params = panelLayoutParams(anchorX, anchorY);
        try {
            windowManager.addView(panel, params);
            overlayPanelView = panel;
            panelParams = params;
            overlayRenderer.render(getDisplayState());
            return true;
        } catch (RuntimeException error) {
            removeOverlay(panel);
            overlayPanelView = null;
            panelParams = null;
            overlayRenderer = null;
            return false;
        }
    }

    private void collapseOverlay() {
        if (overlayPanelView == null || windowManager == null) return;
        int anchorX = hasLastBallCenter ? lastBallCenterX
                : panelParams == null ? displayWidth() / 2
                : panelParams.x + panelParams.width / 2;
        int anchorY = hasLastBallCenter ? lastBallCenterY
                : panelParams == null ? displayHeight() / 2
                : panelParams.y + panelParams.height / 2;
        removeOverlay(overlayPanelView);
        overlayPanelView = null;
        panelParams = null;
        if (!showCollapsedBall(anchorX, anchorY)) {
            stopReading(BattleFrameProtocol.STATUS_READ_ERROR,
                    "悬浮球创建失败，读取已停止");
        }
    }

    private boolean showCollapsedBall(int anchorX, int anchorY) {
        if (windowManager == null || overlayContext == null
                || (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M
                && !Settings.canDrawOverlays(this))) {
            return false;
        }
        int size = overlayDp(76);
        FrameLayout ball = new FrameLayout(overlayContext);
        ball.setClickable(false);
        StateListDrawable background = new StateListDrawable();
        background.addState(new int[] { android.R.attr.state_pressed }, ballRing(true));
        background.addState(new int[0], ballRing(false));
        ball.setBackground(background);
        ball.setElevation(overlayDp(8));

        FrameLayout center = new FrameLayout(overlayContext);
        GradientDrawable centerBackground = new GradientDrawable(
                GradientDrawable.Orientation.TOP_BOTTOM,
                new int[] { Color.rgb(25, 67, 80), Color.rgb(8, 32, 49) });
        centerBackground.setShape(GradientDrawable.OVAL);
        centerBackground.setStroke(overlayDp(1), Color.argb(150, 219, 255, 243));
        center.setBackground(centerBackground);
        FrameLayout.LayoutParams centerParams = new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT);
        centerParams.setMargins(overlayDp(6), overlayDp(6), overlayDp(6), overlayDp(6));
        ball.addView(center, centerParams);

        LinearLayout labels = new LinearLayout(overlayContext);
        labels.setOrientation(LinearLayout.VERTICAL);
        labels.setGravity(Gravity.CENTER);
        TextView battleLabel = new TextView(overlayContext);
        battleLabel.setText("战");
        battleLabel.setTextColor(Color.WHITE);
        battleLabel.setTextSize(23f);
        battleLabel.setTypeface(android.graphics.Typeface.DEFAULT,
                android.graphics.Typeface.BOLD);
        battleLabel.setGravity(Gravity.CENTER);
        battleLabel.setIncludeFontPadding(false);
        TextView panelLabel = new TextView(overlayContext);
        panelLabel.setText("面板");
        panelLabel.setTextColor(Color.rgb(175, 239, 222));
        panelLabel.setTextSize(9f);
        panelLabel.setTypeface(android.graphics.Typeface.DEFAULT,
                android.graphics.Typeface.BOLD);
        panelLabel.setGravity(Gravity.CENTER);
        panelLabel.setIncludeFontPadding(false);
        LinearLayout.LayoutParams battleParams = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        LinearLayout.LayoutParams panelLabelParams = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.WRAP_CONTENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        panelLabelParams.topMargin = overlayDp(2);
        labels.addView(battleLabel, battleParams);
        labels.addView(panelLabel, panelLabelParams);
        center.addView(labels, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT));

        FrameLayout host = new FrameLayout(overlayContext);
        host.setContentDescription("战斗面板；点击展开，拖动移动");
        host.setClickable(true);
        host.addView(ball, new FrameLayout.LayoutParams(size, size));
        host.setOnClickListener(view -> {
            if (!showExpandedOverlay()) {
                stopReading(BattleFrameProtocol.STATUS_READ_ERROR,
                        "悬浮面板无法展开，读取已停止");
            }
        });
        installBallDragListener(host, ball);

        WindowManager.LayoutParams geometry = baseOverlayParams(size, size);
        geometry.gravity = Gravity.TOP | Gravity.START;
        geometry.x = clamp(anchorX - size / 2, 0, Math.max(0, displayWidth() - size));
        geometry.y = clamp(anchorY - size / 2, overlayDp(16),
                Math.max(overlayDp(16), displayHeight() - size - overlayDp(16)));
        WindowManager.LayoutParams hostParams = baseOverlayParams(size, size);
        hostParams.gravity = Gravity.TOP | Gravity.START;
        hostParams.x = geometry.x;
        hostParams.y = geometry.y;
        try {
            windowManager.addView(host, hostParams);
            overlayBallHostView = host;
            overlayBallView = ball;
            ballHostParams = hostParams;
            ballParams = geometry;
            rememberBallCenter(geometry.x + geometry.width / 2,
                    geometry.y + geometry.height / 2);
            if (overlayRenderer != null) overlayRenderer.render(getDisplayState());
            return true;
        } catch (RuntimeException error) {
            removeOverlay(host);
            overlayBallHostView = null;
            overlayBallView = null;
            ballHostParams = null;
            ballParams = null;
            return false;
        }
    }

    private void installBallDragListener(FrameLayout host, View ball) {
        final FloatingBallGesture gesture = new FloatingBallGesture();
        final int[] trackedPointerId = new int[] { -1 };
        final int touchSlop = ViewConfiguration.get(this).getScaledTouchSlop();
        host.setOnTouchListener((view, event) -> {
            switch (event.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                    int downIndex = event.getActionIndex();
                    float downRawX = rawPointerX(event, downIndex);
                    float downRawY = rawPointerY(event, downIndex);
                    if (!isPointerOverBall(downRawX, downRawY)) {
                        gesture.cancel();
                        trackedPointerId[0] = -1;
                        ball.setPressed(false);
                        restoreBallHostAfterGesture();
                        return true;
                    }
                    trackedPointerId[0] = event.getPointerId(downIndex);
                    gesture.begin(trackedPointerId[0], downRawX, downRawY,
                            ballParams == null ? 0 : ballParams.x,
                            ballParams == null ? 0 : ballParams.y, touchSlop);
                    view.requestUnbufferedDispatch(event);
                    ball.setPressed(true);
                    if (!expandBallHostForDrag()) {
                        gesture.cancel();
                        trackedPointerId[0] = -1;
                        ball.setPressed(false);
                    }
                    return true;
                case MotionEvent.ACTION_MOVE:
                    int moveIndex = event.findPointerIndex(trackedPointerId[0]);
                    if (moveIndex < 0) {
                        cancelBallGesture(gesture, trackedPointerId, ball);
                        return true;
                    }
                    FloatingBallGesture.Result moved = gesture.move(trackedPointerId[0],
                            rawPointerX(event, moveIndex), rawPointerY(event, moveIndex),
                            ballMinX(), ballMaxX(), ballMinY(), ballMaxY());
                    applyBallGesture(view, ball, moved);
                    if (moved != null && moved.dragging) ball.setPressed(false);
                    return true;
                case MotionEvent.ACTION_UP:
                    int upIndex = event.findPointerIndex(trackedPointerId[0]);
                    if (upIndex >= 0) {
                        finishBallGesture(view, ball, gesture.release(trackedPointerId[0],
                                rawPointerX(event, upIndex), rawPointerY(event, upIndex),
                                ballMinX(), ballMaxX(), ballMinY(), ballMaxY()));
                    } else {
                        cancelBallGesture(gesture, trackedPointerId, ball);
                    }
                    trackedPointerId[0] = -1;
                    return true;
                case MotionEvent.ACTION_POINTER_UP:
                    int liftedIndex = event.getActionIndex();
                    if (event.getPointerId(liftedIndex) == trackedPointerId[0]) {
                        cancelBallGesture(gesture, trackedPointerId, ball);
                    }
                    return true;
                case MotionEvent.ACTION_CANCEL:
                    cancelBallGesture(gesture, trackedPointerId, ball);
                    return true;
                default:
                    return true;
            }
        });
    }

    private void finishBallGesture(View host, View ball,
                                   FloatingBallGesture.Result result) {
        applyBallGesture(host, ball, result);
        boolean restored = restoreBallHostAfterGesture();
        ball.setPressed(false);
        if (!restored) return;
        if (result == null) return;
        if (result.dragging) {
            host.performHapticFeedback(android.view.HapticFeedbackConstants.VIRTUAL_KEY);
        } else if (result.shouldClick) {
            host.performClick();
        }
    }

    private void cancelBallGesture(FloatingBallGesture gesture, int[] trackedPointerId,
                                   View ball) {
        gesture.cancel();
        trackedPointerId[0] = -1;
        ball.setPressed(false);
        restoreBallHostAfterGesture();
    }

    private boolean expandBallHostForDrag() {
        FrameLayout host = overlayBallHostView;
        if (host == null || overlayBallView == null || ballHostParams == null
                || ballParams == null || windowManager == null) {
            return false;
        }
        overlayBallView.setTranslationX(ballParams.x);
        overlayBallView.setTranslationY(ballParams.y);
        ballHostParams.gravity = Gravity.TOP | Gravity.START;
        ballHostParams.x = 0;
        ballHostParams.y = 0;
        ballHostParams.width = displayWidth();
        ballHostParams.height = displayHeight();
        try {
            windowManager.updateViewLayout(host, ballHostParams);
            return true;
        } catch (RuntimeException ignored) {
            overlayBallView.setTranslationX(0f);
            overlayBallView.setTranslationY(0f);
            ballHostParams.x = ballParams.x;
            ballHostParams.y = ballParams.y;
            ballHostParams.width = ballParams.width;
            ballHostParams.height = ballParams.height;
            restoreBallHostAfterGesture();
            return false;
        }
    }

    private boolean restoreBallHostAfterGesture() {
        FrameLayout host = overlayBallHostView;
        if (host == null || overlayBallView == null || ballHostParams == null
                || ballParams == null || windowManager == null) {
            return false;
        }
        overlayBallView.setTranslationX(0f);
        overlayBallView.setTranslationY(0f);
        ballHostParams.gravity = Gravity.TOP | Gravity.START;
        ballHostParams.x = ballParams.x;
        ballHostParams.y = ballParams.y;
        ballHostParams.width = ballParams.width;
        ballHostParams.height = ballParams.height;
        try {
            windowManager.updateViewLayout(host, ballHostParams);
            return true;
        } catch (RuntimeException ignored) {
            removeOverlay(host);
            overlayBallHostView = null;
            overlayBallView = null;
            ballHostParams = null;
            ballParams = null;
            return false;
        }
    }

    private void applyBallGesture(View host, View ball,
                                  FloatingBallGesture.Result result) {
        if (result == null || ballParams == null || overlayBallHostView != host
                || overlayBallView != ball) {
            return;
        }
        ballParams.x = result.x;
        ballParams.y = result.y;
        rememberBallCenter(ballParams.x + ballParams.width / 2,
                ballParams.y + ballParams.height / 2);
        ball.setTranslationX(ballParams.x);
        ball.setTranslationY(ballParams.y);
    }

    private boolean isPointerOverBall(float rawX, float rawY) {
        return ballParams != null
                && rawX >= ballParams.x && rawX < (double) ballParams.x + ballParams.width
                && rawY >= ballParams.y && rawY < (double) ballParams.y + ballParams.height;
    }

    private float rawPointerX(MotionEvent event, int pointerIndex) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) return event.getRawX(pointerIndex);
        return event.getRawX() + event.getX(pointerIndex) - event.getX(0);
    }

    private float rawPointerY(MotionEvent event, int pointerIndex) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) return event.getRawY(pointerIndex);
        return event.getRawY() + event.getY(pointerIndex) - event.getY(0);
    }

    private int ballMinX() {
        return 0;
    }

    private int ballMaxX() {
        return Math.max(0, displayWidth() - (ballParams == null ? 0 : ballParams.width));
    }

    private int ballMinY() {
        return overlayDp(16);
    }

    private int ballMaxY() {
        return Math.max(ballMinY(), displayHeight()
                - (ballParams == null ? 0 : ballParams.height) - overlayDp(16));
    }

    private GradientDrawable ballRing(boolean pressed) {
        GradientDrawable ring = new GradientDrawable(GradientDrawable.Orientation.TL_BR,
                pressed
                        ? new int[] { Color.rgb(178, 255, 227), Color.rgb(34, 198, 168) }
                        : new int[] { Color.rgb(82, 247, 199), Color.rgb(9, 125, 139) });
        ring.setShape(GradientDrawable.OVAL);
        ring.setStroke(overlayDp(2), pressed
                ? Color.WHITE : Color.rgb(190, 255, 234));
        return ring;
    }

    private void rememberBallCenter(int centerX, int centerY) {
        lastBallCenterX = centerX;
        lastBallCenterY = centerY;
        hasLastBallCenter = true;
    }

    private WindowManager.LayoutParams panelLayoutParams(int anchorX, int anchorY) {
        int maxWidth = Math.max(overlayDp(200), displayWidth() - overlayDp(20));
        int width = Math.min(overlayDp(isLandscape() ? 820 : 600), maxWidth);
        int maxHeight = Math.max(overlayDp(200), displayHeight() - overlayDp(52));
        int height = Math.min(overlayDp(isLandscape() ? 480 : 690), maxHeight);
        WindowManager.LayoutParams params = baseOverlayParams(width, height);
        params.gravity = Gravity.TOP | Gravity.START;
        params.x = clamp(anchorX - width / 2, overlayDp(8),
                Math.max(overlayDp(8), displayWidth() - width - overlayDp(8)));
        params.y = clamp(anchorY - height / 2, overlayDp(24),
                Math.max(overlayDp(24), displayHeight() - height - overlayDp(24)));
        return params;
    }

    private WindowManager.LayoutParams baseOverlayParams(int width, int height) {
        int type = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
                : WindowManager.LayoutParams.TYPE_PHONE;
        int windowFlags = WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL
                | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN;
        return new WindowManager.LayoutParams(width, height, type, windowFlags,
                PixelFormat.TRANSLUCENT);
    }

    private int displayWidth() {
        DisplayMetrics metrics = new DisplayMetrics();
        if (windowManager != null) windowManager.getDefaultDisplay().getRealMetrics(metrics);
        return metrics.widthPixels;
    }

    private int displayHeight() {
        DisplayMetrics metrics = new DisplayMetrics();
        if (windowManager != null) windowManager.getDefaultDisplay().getRealMetrics(metrics);
        return metrics.heightPixels;
    }

    private boolean isLandscape() {
        return getResources().getConfiguration().orientation
                == android.content.res.Configuration.ORIENTATION_LANDSCAPE;
    }

    private int overlayDp(int value) {
        float density = overlayContext == null ? getResources().getDisplayMetrics().density
                : overlayContext.getResources().getDisplayMetrics().density;
        return (int) (value * density + 0.5f);
    }

    private int clamp(int value, int minimum, int maximum) {
        return Math.max(minimum, Math.min(maximum, value));
    }

    private void removeOverlay(View view) {
        if (view == null || windowManager == null) return;
        try {
            windowManager.removeView(view);
        } catch (RuntimeException ignored) {
            // The system may already have removed the window after permission revocation.
        }
    }

    private void removeOverlayWindows() {
        removeOverlay(overlayPanelView);
        removeOverlay(overlayBallHostView);
        overlayPanelView = null;
        overlayBallHostView = null;
        overlayBallView = null;
        panelParams = null;
        ballHostParams = null;
        ballParams = null;
        overlayRenderer = null;
    }

    private void renderOverlay() {
        if (active && (overlayPanelView != null || overlayBallHostView != null)
                && Build.VERSION.SDK_INT >= Build.VERSION_CODES.M
                && !Settings.canDrawOverlays(this)) {
            stopReading(BattleFrameProtocol.STATUS_READ_ERROR, "悬浮窗权限已关闭");
            return;
        }
        if (overlayRenderer != null) overlayRenderer.render(getDisplayState());
    }

    private void setPanelState(int status, String detail, BattleFrameProtocol.Frame frame) {
        if (activeService != null && activeService != this) {
            return;
        }
        currentStatus = status;
        currentDetail = detail;
        currentFrame = status == BattleFrameProtocol.STATUS_LIVE ? frame : null;
        if (currentFrame == null) {
            if (resistanceSnapshot != null) {
                resistanceSnapshot = null;
                resistanceMessage = "原快照已清除，等待下一场战斗查询";
            }
        } else if (resistanceRequestedFrame != null
                && !resistanceRequestedFrame.sameProcessAndBattle(currentFrame)) {
            resistanceSnapshot = null;
            resistanceRequestedFrame = null;
            resistanceMessage = "新战斗已确认";
        }
        currentFrameTimestampUnixMs = currentFrame == null ? 0L : currentFrame.timestampUnixMs;
        mainHandler.removeCallbacks(freshnessCheck);
        if (currentFrame != null) {
            mainHandler.postDelayed(freshnessCheck,
                    Math.max(250L, BattleFrameProtocol.MAX_FRAME_AGE_MS));
        }
        updateNotificationText();
        if (overlayRenderer != null && overlayRenderer.isShowingResistance()
                && currentFrame != null && active && !stopping) {
            requestResistanceIfNeeded();
        }
        renderOverlay();
    }

    private void acceptFrame(BattleFrameProtocol.Frame frame) {
        if (stopping || activeService != this) {
            return;
        }
        if (frame.status != BattleFrameProtocol.STATUS_LIVE) {
            if (BattleFrameProtocol.shouldRetainLastLiveFrame(currentFrame, frame)) {
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
            return;
        }

        BattleFrameProtocol.Frame old = currentFrame;
        boolean sameBattle = old != null && old.sameProcessAndBattle(frame);
        if (old != null && !sameBattle) {
            setPanelState(BattleFrameProtocol.STATUS_LOCATING,
                    "进程或战斗身份变化，已清除旧数据并重新确认", null);
        }
        setPanelState(BattleFrameProtocol.STATUS_LIVE, "", frame);
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
            return;
        }
        String gamePackage = requestedPackage;
        File worker = new File(getApplicationInfo().nativeLibraryDir, "libenemy_reader.so");
        if (!worker.isFile() || !worker.canExecute()) {
            setPanelState(BattleFrameProtocol.STATUS_READ_ERROR,
                    "读取器不存在或不可执行：libenemy_reader.so", null);
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

    static String statusLabel(int status) {
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

}
