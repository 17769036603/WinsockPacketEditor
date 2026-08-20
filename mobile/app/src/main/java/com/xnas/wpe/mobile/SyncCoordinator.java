package com.xnas.wpe.mobile;

import android.content.Context;
import android.os.Handler;
import android.os.Looper;

import org.json.JSONObject;

import java.io.IOException;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicLong;

/** Single owner for sync, snapshot transactions, runtime polling and actions. */
public final class SyncCoordinator {
    public static final String DEFAULT_ENDPOINT = "https://192.168.0.100:89/";
    private static volatile SyncCoordinator INSTANCE;

    public enum Action {
        START, PAUSE, STOP
    }

    public interface Listener {
        void onSnapshotChanged(SyncModels.Snapshot snapshot, SyncModels.SyncState state);
        void onRuntimeChanged(SyncModels.RuntimeBundle runtime);
        void onStateChanged(SyncModels.SyncState state, String message);
        void onActionPending(boolean pending);
    }

    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private final AtomicBoolean syncInFlight = new AtomicBoolean(false);
    private final AtomicBoolean runtimeInFlight = new AtomicBoolean(false);
    private final AtomicBoolean actionInFlight = new AtomicBoolean(false);
    private final AtomicLong sessionGeneration = new AtomicLong(0L);
    private final Object sessionLock = new Object();
    private final Context context;
    private volatile Listener listener;
    private final SyncStore syncStore;
    private final SnapshotStore snapshotStore;
    private volatile WpeSyncClient client;
    private volatile SyncStore.ProfileState profile = new SyncStore.ProfileState("");
    private volatile SyncModels.Snapshot snapshot;
    private volatile SyncModels.RuntimeBundle runtime = SyncModels.RuntimeBundle.parse(null);
    private volatile SyncModels.SyncState state = SyncModels.SyncState.NO_PROFILE;
    private volatile String lastMessage = "";
    private volatile boolean actionPendingValue;
    private volatile boolean destroyed;

    private static final class SessionChangedException extends IOException {
        SessionChangedException() {
            super("同步会话已切换");
        }
    }

    private SyncCoordinator(Context context, Listener listener) {
        this.context = context.getApplicationContext();
        this.listener = listener;
        this.syncStore = new SyncStore(this.context);
        this.snapshotStore = new SnapshotStore(this.context);
    }

    public static SyncCoordinator obtain(Context context, Listener listener) {
        synchronized (SyncCoordinator.class) {
            if (INSTANCE == null || INSTANCE.destroyed) {
                INSTANCE = new SyncCoordinator(context, listener);
            } else {
                INSTANCE.listener = listener;
            }
            INSTANCE.replayCurrentState(listener);
            return INSTANCE;
        }
    }

    public static SyncCoordinator current() {
        SyncCoordinator value = INSTANCE;
        return value == null || value.destroyed ? null : value;
    }

    public void attachListener(Listener value) {
        listener = value;
        replayCurrentState(value);
    }

    public void detachListener(Listener value) {
        if (listener == value) {
            listener = null;
        }
    }

    private void replayCurrentState(Listener value) {
        if (value == null || destroyed) {
            return;
        }
        postToMain(() -> {
            if (value != listener || destroyed) {
                return;
            }
            value.onSnapshotChanged(snapshot, state);
            value.onRuntimeChanged(runtime);
            value.onStateChanged(state, lastMessage);
            value.onActionPending(actionPendingValue);
        });
    }

    public void configure(String endpoint, String profileName) {
        synchronized (sessionLock) {
            sessionGeneration.incrementAndGet();
            // The old executor work is allowed to finish, but it no longer owns
            // any of these admission flags or may publish into the new profile.
            syncInFlight.set(false);
            runtimeInFlight.set(false);
            actionInFlight.set(false);
            actionPendingValue = false;
            OverlayService.resetProfileState();
        }
        // Profile changes invalidate the old action admission immediately. Notify
        // the current Activity as well as the overlay; otherwise its local
        // actionPending flag can leave the controls disabled until recreation.
        publishActionPending(false);
        WpeSyncClient newClient;
        try {
            newClient = new WpeSyncClient(endpoint);
        } catch (IllegalArgumentException ex) {
            synchronized (sessionLock) {
                client = null;
                profile = new SyncStore.ProfileState("");
                snapshot = null;
                runtime = SyncModels.RuntimeBundle.parse(null);
            }
            publishSnapshot(null, SyncModels.SyncState.ERROR);
            publishError(ex.getMessage());
            return;
        }
        synchronized (sessionLock) {
            client = newClient;
            profile = syncStore.activateProfile(endpoint, profileName);
            snapshot = null;
            runtime = SyncModels.RuntimeBundle.parse(null);
        }
        publishRuntime(runtime);
        boolean loadedCache = loadCachedSnapshot();
        if (!loadedCache) {
            publishSnapshot(null, SyncModels.SyncState.CONNECTING);
        }
        publishState(profile.revision.isEmpty() ? SyncModels.SyncState.CONNECTING
                : SyncModels.SyncState.CONNECTED_CACHED, "正在连接电脑端");
        sync(false, true);
    }

    public boolean loadSavedConnection() {
        if (client != null && !profile.profileKey.isEmpty()) {
            return true;
        }
        String endpoint = syncStore.getEndpoint();
        if (endpoint.isEmpty()) {
            endpoint = DEFAULT_ENDPOINT;
        }
        // Keep the former username only as a local cache namespace during an
        // in-place upgrade. It is never sent to the desktop or used for auth.
        configure(endpoint, syncStore.getUsername());
        return true;
    }

    public boolean hasSavedConnection() {
        return client != null && !profile.profileKey.isEmpty();
    }

    public void connectFromSavedFields(String endpoint) {
        configure(endpoint, syncStore.getUsername());
    }

    public void manualUpdate() {
        if (actionInFlight.get()) {
            publishState(SyncModels.SyncState.ACTION_PENDING, "上一个操作仍在处理中，请稍候");
            return;
        }
        OverlayService.clearErrorStatus();
        sync(true, false);
    }

    public void refreshRuntime() {
        runtime(true);
    }

    public SyncModels.Snapshot getSnapshot() {
        return snapshot;
    }

    public SyncModels.RuntimeBundle getRuntime() {
        return runtime;
    }

    public SyncModels.SyncState getState() {
        return state;
    }

    public SyncStore.ProfileState getProfile() {
        return profile;
    }

    public UUID getSelected(SyncModels.Module module) {
        return profile.selected(module);
    }

    public boolean canControl() {
        return snapshot != null
                && (state == SyncModels.SyncState.CONNECTED_CACHED
                || state == SyncModels.SyncState.SYNCED)
                && runtime != null
                && runtime.send.state != SyncModels.RuntimeState.UNKNOWN
                && runtime.progression.state != SyncModels.RuntimeState.UNKNOWN
                && runtime.assistant.state != SyncModels.RuntimeState.UNKNOWN;
    }

    public boolean isRuntimeRevisionCurrent(SyncModels.Module module) {
        if (module == null || runtime == null) {
            return false;
        }
        SyncModels.RuntimeStatus status = runtime.forModule(module);
        if (status == null || status.state == SyncModels.RuntimeState.UNKNOWN) {
            return false;
        }
        if (!status.isBusy()) {
            return true;
        }
        return !profile.revision.isEmpty() && !status.revision.isEmpty()
                && profile.revision.equalsIgnoreCase(status.revision);
    }

    public void saveSelection(SyncModels.Module module, UUID id) {
        if (module == null) {
            return;
        }
        synchronized (sessionLock) {
            if (profile.profileKey.isEmpty()) {
                return;
            }
            syncStore.saveSelection(profile.profileKey, module, id);
            // Keep the in-memory profile aligned with the persisted selection.
            // The overlay can outlive/recreate the Activity, so reading only the
            // preference is not enough for the current coordinator instance.
            profile = syncStore.readProfile(profile.profileKey, true);
        }
    }

    public void action(SyncModels.Module module, UUID presetId, Action action) {
        if (module == null || action == null) {
            publishError("运行请求无效，请重新选择功能。");
            return;
        }
        if (!actionInFlight.compareAndSet(false, true)) {
            publishState(SyncModels.SyncState.ACTION_PENDING, "上一个操作仍在处理中，请稍候");
            return;
        }
        final WpeSyncClient currentClient = client;
        final SyncModels.Snapshot currentSnapshot = snapshot;
        final long requestGeneration = sessionGeneration.get();
        final String requestProfileKey = profile.profileKey;
        final String requestRevision = profile.revision;
        if (currentClient == null || currentSnapshot == null || profile.revision.isEmpty()) {
            actionInFlight.set(false);
            publishError("请先连接电脑端并同步预设。");
            return;
        }
        if (!isCurrentSession(requestGeneration, currentClient, requestProfileKey)) {
            actionInFlight.set(false);
            return;
        }
        if (action != Action.STOP && presetId == null) {
            actionInFlight.set(false);
            publishError("请先选择一个" + moduleName(module) + "预设。");
            return;
        }
        if (state == SyncModels.SyncState.NO_PROFILE
                || state == SyncModels.SyncState.CACHED_OFFLINE
                || state == SyncModels.SyncState.CONNECTING
                || state == SyncModels.SyncState.UPDATE_CHECKING
                || state == SyncModels.SyncState.UPDATING
                || state == SyncModels.SyncState.UPDATE_AVAILABLE
                || state == SyncModels.SyncState.ERROR) {
            actionInFlight.set(false);
            publishError("电脑端当前不可控，请先连接并确认预设状态。");
            return;
        }
        SyncModels.RuntimeStatus currentRuntime = runtime.forModule(module);
        if (action != Action.STOP && !isRuntimeRevisionCurrent(module)) {
            actionInFlight.set(false);
            publishError("当前任务对应旧预设版本，请先停止任务后再更新预设。");
            return;
        }
        if (action == Action.START && currentRuntime.isBusy() &&
                currentRuntime.state != SyncModels.RuntimeState.PAUSED) {
            actionInFlight.set(false);
            publishError("当前已有任务运行，请等待状态更新。");
            return;
        }
        if (action == Action.START && currentRuntime.state == SyncModels.RuntimeState.PAUSED
                && (currentRuntime.presetId == null || !currentRuntime.presetId.equals(presetId))) {
            actionInFlight.set(false);
            publishError("当前暂停任务与所选预设不一致，请先选择原任务预设。");
            return;
        }
        if (action == Action.PAUSE && (currentRuntime.jobId == null || !currentRuntime.isBusy())) {
            actionInFlight.set(false);
            publishError("当前没有可暂停的任务。");
            return;
        }
        if (action == Action.PAUSE
                && (currentRuntime.presetId == null || !currentRuntime.presetId.equals(presetId))) {
            actionInFlight.set(false);
            publishError("当前任务与所选预设不一致，请刷新运行状态。");
            return;
        }
        if (action == Action.PAUSE
                && currentRuntime.state != SyncModels.RuntimeState.RUNNING
                && currentRuntime.state != SyncModels.RuntimeState.STARTING) {
            actionInFlight.set(false);
            publishError("当前任务不在可暂停状态，请刷新运行状态。");
            return;
        }
        if (action == Action.STOP && (currentRuntime.jobId == null || !currentRuntime.isBusy())) {
            actionInFlight.set(false);
            publishError("当前没有可停止的任务。");
            return;
        }
        if (action == Action.STOP && currentRuntime.state == SyncModels.RuntimeState.STOPPING) {
            actionInFlight.set(false);
            publishState(SyncModels.SyncState.ACTION_PENDING, "停止请求已提交，等待电脑端状态");
            return;
        }

        final String path = actionPath(module, presetId, action, currentRuntime);
        final JSONObject body = new JSONObject();
        final String requestId = UUID.randomUUID().toString();
        try {
            body.put("expectedRevision", requestRevision);
            body.put("requestId", requestId);
            if (action != Action.START && currentRuntime.jobId != null) {
                body.put("jobId", currentRuntime.jobId.toString());
            }
        } catch (Exception ex) {
            actionInFlight.set(false);
            publishError("无法创建运行请求。");
            return;
        }

        publishActionPending(true);
        OverlayService.clearErrorStatus();
        publishState(SyncModels.SyncState.ACTION_PENDING,
                action == Action.START ? "正在开始" : action == Action.PAUSE ? "正在暂停" : "正在停止");
        try {
            executor.execute(() -> {
                try {
                    // Every action carries a requestId. Retrying START with that
                    // same body is safe and prevents a lost response from causing
                    // the user to submit a second start with a new job.
                    ensureCurrentSession(requestGeneration, currentClient, requestProfileKey);
                    JSONObject response = currentClient.postJsonObjectWithRetry(path, body);
                    ensureCurrentSession(requestGeneration, currentClient, requestProfileKey);
                    SyncModels.ActionResult result = SyncModels.ActionResult.parse(response);
                    String expectedAction = expectedAction(module, action, currentRuntime);
                    boolean validAction = expectedAction.equalsIgnoreCase(result.action);
                    if (action == Action.START) {
                        // Start and resume share one desktop route. The runtime may
                        // change between the last poll and the POST, so either
                        // successful admission result is valid for this request.
                        validAction = (module.wireName + "-start").equalsIgnoreCase(result.action)
                                || (module.wireName + "-resume").equalsIgnoreCase(result.action);
                    }
                    boolean invalidStopJob = action == Action.STOP
                            && (result.jobId == null || currentRuntime.jobId == null
                            || !result.jobId.equals(currentRuntime.jobId));
                    boolean invalidStopPreset = action == Action.STOP
                            && (result.id == null || currentRuntime.presetId == null
                            || !result.id.equals(currentRuntime.presetId));
                    if (!result.accepted
                            || !requestRevision.equalsIgnoreCase(result.revision)
                            || !validAction
                            || (action != Action.STOP && result.jobId == null)
                            || invalidStopJob
                            || invalidStopPreset
                            || (action != Action.STOP
                            && (result.id == null || !result.id.equals(presetId)))) {
                        throw new IOException("电脑端未接受此操作或返回了无效结果");
                    }
                    ensureCurrentSession(requestGeneration, currentClient, requestProfileKey);
                    publishStateForSession(SyncModels.SyncState.ACTION_PENDING,
                            "操作已提交，等待电脑端状态",
                            requestGeneration, currentClient, requestProfileKey);
                    boolean refreshed = fetchRuntime(currentClient, false,
                            requestGeneration, requestProfileKey);
                    ensureCurrentSession(requestGeneration, currentClient, requestProfileKey);
                    if (refreshed) {
                        publishStateForSession(SyncModels.SyncState.SYNCED, "运行状态已刷新",
                                requestGeneration, currentClient, requestProfileKey);
                    } else {
                        publishErrorForSession("操作已提交，但电脑端运行状态暂时无法确认。",
                                requestGeneration, currentClient, requestProfileKey);
                    }
                } catch (Exception ex) {
                    if (isCurrentSession(requestGeneration, currentClient, requestProfileKey)
                            && !(ex instanceof SessionChangedException)) {
                        publishErrorForSession(userMessage(ex),
                                requestGeneration, currentClient, requestProfileKey);
                    }
                } finally {
                    if (sessionGeneration.get() == requestGeneration) {
                        actionInFlight.set(false);
                        publishActionPending(false);
                    }
                }
            });
        } catch (RuntimeException ex) {
            actionInFlight.set(false);
            publishActionPending(false);
            publishErrorForSession("运行请求已取消，请重新连接。",
                    requestGeneration, currentClient, requestProfileKey);
        }
    }

    public void shutdown() {
        destroyed = true;
        executor.shutdownNow();
        mainHandler.removeCallbacksAndMessages(null);
        OverlayService.resetProfileState();
        synchronized (SyncCoordinator.class) {
            if (INSTANCE == this) {
                INSTANCE = null;
            }
        }
    }

    private void sync(final boolean manual, final boolean connecting) {
        final WpeSyncClient currentClient = client;
        final long requestGeneration = sessionGeneration.get();
        final String requestProfileKey = profile.profileKey;
        if (currentClient == null) {
            publishError("请先连接电脑端。");
            return;
        }
        if (!isCurrentSession(requestGeneration, currentClient, requestProfileKey)) {
            return;
        }
        if (!syncInFlight.compareAndSet(false, true)) {
            return;
        }
        publishState(manual ? SyncModels.SyncState.UPDATE_CHECKING : SyncModels.SyncState.CONNECTING,
                manual ? "正在检查预设版本" : "正在连接电脑端");
        try {
            executor.execute(() -> {
                try {
                    ensureCurrentSession(requestGeneration, currentClient, requestProfileKey);
                    SyncModels.Manifest manifest = SyncModels.Manifest.parse(
                            currentClient.getJson("MobileSync/manifest"),
                            SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES);
                    ensureCurrentSession(requestGeneration, currentClient, requestProfileKey);
                    boolean hasCache = snapshot != null && !profile.revision.isEmpty();
                    boolean sameManifest = hasCache
                            && manifest.revision.equalsIgnoreCase(profile.revision)
                            && manifest.payloadSha256.equalsIgnoreCase(profile.payloadSha256)
                            && snapshot.payloadBytes == manifest.payloadBytes;
                    boolean changed = !sameManifest;
                    if (manual && !changed && hasCache) {
                        if (fetchRuntime(currentClient, true, requestGeneration, requestProfileKey)) {
                            publishStateForSession(SyncModels.SyncState.SYNCED,
                                    hasRuntimeRevisionMismatch(runtime)
                                            ? "预设已是最新；当前任务对应旧版本，请先停止任务"
                                            : "预设已是最新",
                                    requestGeneration, currentClient, requestProfileKey);
                        }
                        return;
                    }
                    if (!manual && connecting && hasCache) {
                        if (fetchRuntime(currentClient, true, requestGeneration, requestProfileKey)) {
                            publishStateForSession(changed ? SyncModels.SyncState.UPDATE_AVAILABLE
                                    : SyncModels.SyncState.CONNECTED_CACHED,
                                    changed ? "电脑端有更新，请点击更新预设" : "已连接 · 预设未自动更新",
                                    requestGeneration, currentClient, requestProfileKey);
                        }
                        return;
                    }
                    if (!manual && hasCache) {
                        if (fetchRuntime(currentClient, true, requestGeneration, requestProfileKey)) {
                            publishStateForSession(changed ? SyncModels.SyncState.UPDATE_AVAILABLE
                                    : SyncModels.SyncState.CONNECTED_CACHED,
                                    changed ? "电脑端有更新，请点击更新预设" : "已连接",
                                    requestGeneration, currentClient, requestProfileKey);
                        }
                        return;
                    }
                    fetchAndCommit(currentClient, manifest, requestGeneration, requestProfileKey);
                    ensureCurrentSession(requestGeneration, currentClient, requestProfileKey);
                    if (fetchRuntime(currentClient, true, requestGeneration, requestProfileKey)) {
                        publishStateForSession(SyncModels.SyncState.SYNCED,
                                hasRuntimeRevisionMismatch(runtime)
                                        ? "预设已更新；当前任务对应旧版本，请先停止任务"
                                        : "预设已更新",
                                requestGeneration, currentClient, requestProfileKey);
                    }
                } catch (Exception ex) {
                    if (isCurrentSession(requestGeneration, currentClient, requestProfileKey)
                            && !(ex instanceof SessionChangedException)) {
                        publishErrorForSession(userMessage(ex),
                                requestGeneration, currentClient, requestProfileKey);
                    }
                } finally {
                    if (sessionGeneration.get() == requestGeneration) {
                        syncInFlight.set(false);
                    }
                }
            });
        } catch (RuntimeException ex) {
            syncInFlight.set(false);
            publishErrorForSession("同步请求已取消，请重新连接。",
                    requestGeneration, currentClient, requestProfileKey);
        }
    }

    private void fetchAndCommit(WpeSyncClient currentClient, SyncModels.Manifest initial,
                                long requestGeneration, String requestProfileKey)
            throws Exception {
        ensureCurrentSession(requestGeneration, currentClient, requestProfileKey);
        if (!publishStateForSession(SyncModels.SyncState.UPDATING, "正在获取完整预设快照",
                requestGeneration, currentClient, requestProfileKey)) {
            throw new SessionChangedException();
        }
        Exception last = null;
        for (int attempt = 0; attempt < 3; attempt++) {
            try {
                ensureCurrentSession(requestGeneration, currentClient, requestProfileKey);
                JSONObject raw = currentClient.getJson("MobileSync/snapshot",
                        snapshotResponseLimit(initial.payloadBytes));
                SyncModels.Snapshot candidate = SyncModels.Snapshot.parse(
                        raw, initial, SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES);
                SyncModels.Manifest finalManifest = SyncModels.Manifest.parse(
                        currentClient.getJson("MobileSync/manifest"),
                        SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES);
                if (!candidate.revision.equalsIgnoreCase(finalManifest.revision)
                        || !candidate.payloadSha256.equalsIgnoreCase(finalManifest.payloadSha256)
                        || candidate.payloadBytes != finalManifest.payloadBytes) {
                    throw new SyncModels.SyncException("snapshot_revision_mismatch",
                            "电脑端在同步期间再次修改了预设或快照校验值。", true);
                }
                synchronized (sessionLock) {
                    ensureCurrentSession(requestGeneration, currentClient, requestProfileKey);
                    SyncStore.ProfileState committedProfile = profile;
                    snapshotStore.commit(requestProfileKey, candidate.revision,
                            raw, candidate.payloadSha256, candidate.payloadBytes);
                    syncStore.saveSnapshot(committedProfile, candidate.revision,
                            candidate.payloadSha256, candidate.revision,
                            System.currentTimeMillis());
                    profile = syncStore.readProfile(requestProfileKey, true);
                    snapshot = candidate;
                }
                ensureCurrentSession(requestGeneration, currentClient, requestProfileKey);
                if (!publishSnapshotForSession(candidate, SyncModels.SyncState.SYNCED,
                        requestGeneration, currentClient, requestProfileKey)) {
                    throw new SessionChangedException();
                }
                return;
            } catch (Exception ex) {
                last = ex;
                if (attempt < 2) {
                    ensureCurrentSession(requestGeneration, currentClient, requestProfileKey);
                    initial = SyncModels.Manifest.parse(currentClient.getJson("MobileSync/manifest"),
                            SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES);
                }
            }
        }
        throw last == null ? new IOException("快照同步失败") : last;
    }

    private void runtime(boolean showState) {
        final WpeSyncClient currentClient = client;
        runtime(showState, sessionGeneration.get(), profile.profileKey, currentClient);
    }

    private void runtime(boolean showState, long requestGeneration,
                         String requestProfileKey, WpeSyncClient currentClient) {
        if (currentClient == null
                || !isCurrentSession(requestGeneration, currentClient, requestProfileKey)
                || !runtimeInFlight.compareAndSet(false, true)) {
            return;
        }
        try {
            executor.execute(() -> {
                try {
                    fetchRuntime(currentClient, showState, requestGeneration, requestProfileKey);
                } finally {
                    if (sessionGeneration.get() == requestGeneration) {
                        runtimeInFlight.set(false);
                    }
                }
            });
        } catch (RuntimeException ex) {
            runtimeInFlight.set(false);
            if (isCurrentSession(requestGeneration, currentClient, requestProfileKey)) {
                publishErrorForSession("运行状态刷新已取消，请重新连接。",
                        requestGeneration, currentClient, requestProfileKey);
            }
        }
    }

    private boolean fetchRuntime(WpeSyncClient currentClient, boolean showState,
                                 long requestGeneration, String requestProfileKey) {
        try {
            ensureCurrentSession(requestGeneration, currentClient, requestProfileKey);
            SyncModels.RuntimeBundle value = SyncModels.RuntimeBundle.parseStrict(
                currentClient.getJson("MobileSync/runtime"));
            ensureCurrentSession(requestGeneration, currentClient, requestProfileKey);
            boolean revisionMismatch = hasRuntimeRevisionMismatch(value);
            synchronized (sessionLock) {
                ensureCurrentSession(requestGeneration, currentClient, requestProfileKey);
                runtime = value;
            }
            if (!publishRuntimeForSession(value, requestGeneration, currentClient, requestProfileKey)) {
                return false;
            }
            if (showState && snapshot != null && !actionPendingValue
                    && state != SyncModels.SyncState.ACTION_PENDING) {
                ensureCurrentSession(requestGeneration, currentClient, requestProfileKey);
                String message = revisionMismatch
                        ? "运行任务对应旧预设，请先停止当前任务，再更新预设"
                        : "运行状态已刷新";
                if (!publishStateForSession(SyncModels.SyncState.SYNCED, message,
                        requestGeneration, currentClient, requestProfileKey)) {
                    return false;
                }
            }
            return true;
        } catch (SessionChangedException ignored) {
            return false;
        } catch (Exception ex) {
            if (!isCurrentSession(requestGeneration, currentClient, requestProfileKey)) {
                return false;
            }
            SyncModels.RuntimeBundle unknown = SyncModels.RuntimeBundle.parse(null);
            synchronized (sessionLock) {
                if (!isCurrentSession(requestGeneration, currentClient, requestProfileKey)) {
                    return false;
                }
                runtime = unknown;
            }
            if (!publishRuntimeForSession(unknown, requestGeneration,
                    currentClient, requestProfileKey)) {
                return false;
            }
            if (showState) {
                String message = ex instanceof SyncModels.SyncException
                        ? ex.getMessage() : "运行状态未知，请重新连接";
                publishStateForSession(state == SyncModels.SyncState.UPDATE_AVAILABLE
                        ? SyncModels.SyncState.UPDATE_AVAILABLE
                        : SyncModels.SyncState.CACHED_OFFLINE, message,
                        requestGeneration, currentClient, requestProfileKey);
            }
            return false;
        }
    }

    private boolean hasRuntimeRevisionMismatch(SyncModels.RuntimeBundle value) {
        if (value == null || profile.revision.isEmpty()) {
            return false;
        }
        for (SyncModels.RuntimeStatus status : new SyncModels.RuntimeStatus[] {
                value.send, value.progression, value.assistant }) {
            if (status == null || !status.isBusy()) {
                continue;
            }
            if (status.revision.isEmpty()
                    || !profile.revision.equalsIgnoreCase(status.revision)) {
                return true;
            }
        }
        return false;
    }

    private boolean loadCachedSnapshot() {
        if (profile.profileKey.isEmpty() || profile.snapshotVersion.isEmpty()) {
            return false;
        }
        try {
            JSONObject raw = snapshotStore.read(profile.profileKey, profile.snapshotVersion);
            if (raw == null) {
                return false;
            }
            SyncModels.Manifest manifest = syntheticManifest(raw, profile);
            snapshot = SyncModels.Snapshot.parse(raw, manifest,
                    SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES);
            if (!profile.payloadSha256.equalsIgnoreCase(snapshot.payloadSha256)) {
                throw new IOException("本地快照校验指针不一致");
            }
            publishSnapshot(snapshot, SyncModels.SyncState.CACHED_OFFLINE);
            return true;
        } catch (Exception ignored) {
            snapshot = null;
            return false;
        }
    }

    private static SyncModels.Manifest syntheticManifest(JSONObject raw, SyncStore.ProfileState profile)
            throws SyncModels.SyncException {
        JSONObject manifest = new JSONObject();
        try {
            manifest.put("schemaVersion", SyncModels.SCHEMA_VERSION);
            manifest.put("revision", profile.revision);
            manifest.put("payloadSha256", profile.payloadSha256);
            manifest.put("payloadBytes", raw == null ? 0 : SyncModels.canonicalPayload(raw)
                    .getBytes(java.nio.charset.StandardCharsets.UTF_8).length);
            manifest.put("sendCount", raw == null || raw.optJSONArray("send") == null
                    ? 0 : raw.optJSONArray("send").length());
            manifest.put("progressionCount", raw == null || raw.optJSONArray("progression") == null
                    ? 0 : raw.optJSONArray("progression").length());
            manifest.put("assistantCount", raw == null || raw.optJSONArray("assistant") == null
                    ? 0 : raw.optJSONArray("assistant").length());
            manifest.put("capabilities", new org.json.JSONArray()
                    .put("fullSnapshot").put("expectedRevision"));
        } catch (Exception ex) {
            throw new SyncModels.SyncException("snapshot_invalid", "本地快照元数据无效。");
        }
        return SyncModels.Manifest.parse(manifest, SyncModels.DEFAULT_MAX_SNAPSHOT_BYTES);
    }

    private static int snapshotResponseLimit(int payloadBytes) {
        long requested = Math.max(64L * 1024L, (long) payloadBytes + 64L * 1024L);
        return (int) Math.min(WpeSyncClient.MAX_RESPONSE_BYTES, requested);
    }

    private boolean isCurrentSession(long requestGeneration, WpeSyncClient requestClient,
                                     String requestProfileKey) {
        return !destroyed
                && sessionGeneration.get() == requestGeneration
                && client == requestClient
                && profile.profileKey.equals(requestProfileKey);
    }

    private void ensureCurrentSession(long requestGeneration, WpeSyncClient requestClient,
                                      String requestProfileKey) throws SessionChangedException {
        if (!isCurrentSession(requestGeneration, requestClient, requestProfileKey)) {
            throw new SessionChangedException();
        }
    }

    private void publishSnapshot(SyncModels.Snapshot value, SyncModels.SyncState newState) {
        OverlayService.updateSnapshot(value);
        postToMain(() -> {
            if (snapshot != value) {
                return;
            }
            if (listener != null) {
                listener.onSnapshotChanged(value, newState);
            }
        });
    }

    private void publishRuntime(SyncModels.RuntimeBundle value) {
        OverlayService.updateRuntimeBundle(value);
        postToMain(() -> {
            if (runtime != value) {
                return;
            }
            if (listener != null) {
                listener.onRuntimeChanged(value);
            }
        });
    }

    private void publishState(SyncModels.SyncState newState, String message) {
        state = newState;
        lastMessage = message == null ? "" : message;
        postToMain(() -> {
            if (listener != null) {
                listener.onStateChanged(newState, message);
            }
            if (newState == SyncModels.SyncState.ERROR) {
                OverlayService.updateErrorStatus(message);
            } else {
                OverlayService.updateSyncStatus(message);
            }
            OverlayService.updateControlAvailability(canControl());
        });
    }

    private boolean publishSnapshotForSession(SyncModels.Snapshot value,
                                              SyncModels.SyncState newState,
                                              long requestGeneration,
                                              WpeSyncClient requestClient,
                                              String requestProfileKey) {
        synchronized (sessionLock) {
            if (!isCurrentSession(requestGeneration, requestClient, requestProfileKey)) {
                return false;
            }
            OverlayService.updateSnapshot(value);
        }
        postToMain(() -> {
            if (!isCurrentSession(requestGeneration, requestClient, requestProfileKey)
                    || snapshot != value) {
                return;
            }
            if (listener != null) {
                listener.onSnapshotChanged(value, newState);
            }
        });
        return true;
    }

    private boolean publishRuntimeForSession(SyncModels.RuntimeBundle value,
                                              long requestGeneration,
                                              WpeSyncClient requestClient,
                                              String requestProfileKey) {
        synchronized (sessionLock) {
            if (!isCurrentSession(requestGeneration, requestClient, requestProfileKey)) {
                return false;
            }
            OverlayService.updateRuntimeBundle(value);
        }
        postToMain(() -> {
            if (!isCurrentSession(requestGeneration, requestClient, requestProfileKey)
                    || runtime != value) {
                return;
            }
            if (listener != null) {
                listener.onRuntimeChanged(value);
            }
        });
        return true;
    }

    private boolean publishStateForSession(SyncModels.SyncState newState, String message,
                                            long requestGeneration,
                                            WpeSyncClient requestClient,
                                            String requestProfileKey) {
        String safeMessage = message == null ? "" : message;
        synchronized (sessionLock) {
            if (!isCurrentSession(requestGeneration, requestClient, requestProfileKey)) {
                return false;
            }
            state = newState;
            lastMessage = safeMessage;
        }
        postToMain(() -> {
            if (!isCurrentSession(requestGeneration, requestClient, requestProfileKey)) {
                return;
            }
            if (listener != null) {
                listener.onStateChanged(newState, safeMessage);
            }
            if (newState == SyncModels.SyncState.ERROR) {
                OverlayService.updateErrorStatus(safeMessage);
            } else {
                OverlayService.updateSyncStatus(safeMessage);
            }
            OverlayService.updateControlAvailability(canControl());
        });
        return true;
    }

    private boolean publishErrorForSession(String message, long requestGeneration,
                                            WpeSyncClient requestClient,
                                            String requestProfileKey) {
        return publishStateForSession(SyncModels.SyncState.ERROR,
                message == null ? "同步失败" : message,
                requestGeneration, requestClient, requestProfileKey);
    }

    private void publishError(String message) {
        publishState(SyncModels.SyncState.ERROR, message == null ? "同步失败" : message);
    }

    private void publishActionPending(boolean pending) {
        actionPendingValue = pending;
        postToMain(() -> {
            if (listener != null) {
                listener.onActionPending(pending);
            }
        });
    }

    private void postToMain(Runnable action) {
        if (destroyed) {
            return;
        }
        mainHandler.post(() -> {
            if (!destroyed && action != null) {
                action.run();
            }
        });
    }

    private static String actionPath(SyncModels.Module module, UUID presetId,
                                     Action action, SyncModels.RuntimeStatus runtime) {
        if (action == Action.STOP) {
            return "MobileSync/" + module.wireName + "/stop";
        }
        String suffix = action == Action.PAUSE ? "pause" : "start";
        return "MobileSync/" + module.wireName + "/" + presetId + "/" + suffix;
    }

    private static String expectedAction(SyncModels.Module module, Action action,
                                         SyncModels.RuntimeStatus runtime) {
        String suffix;
        if (action == Action.STOP) {
            suffix = "stop";
        } else if (action == Action.PAUSE) {
            suffix = "pause";
        } else {
            suffix = runtime != null && runtime.state == SyncModels.RuntimeState.PAUSED
                    ? "resume" : "start";
        }
        return module.wireName + "-" + suffix;
    }

    private static String moduleName(SyncModels.Module module) {
        switch (module) {
            case PROGRESSION:
                return "递进";
            case ASSISTANT:
                return "助手";
            default:
                return "发送";
        }
    }

    private static String userMessage(Exception ex) {
        if (ex instanceof WpeSyncClient.WpeHttpException) {
            return ex.getMessage();
        }
        if (ex instanceof SyncModels.SyncException) {
            return ex.getMessage();
        }
        String message = ex.getMessage();
        return message == null || message.isEmpty() ? "电脑端操作失败" : message;
    }
}
