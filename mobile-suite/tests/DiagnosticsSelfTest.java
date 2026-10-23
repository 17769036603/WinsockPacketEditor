package com.xnas.wpe.suite;

import java.util.Arrays;
import java.util.List;

public final class DiagnosticsSelfTest {
    private static int assertions;

    public static void main(String[] args) {
        unreadAndExpiredSnapshotsBecomeUnknown();
        moduleAndTargetSnapshotsStayIndependent();
        oldSnapshotCannotReplaceNewerSnapshot();
        formatterKeepsOnlyAllowlistedFields();
        scheduleSummaryRequiresKnownEnumAndMinuteTime();
        System.out.println("DiagnosticsSelfTest PASS (" + assertions + " assertions)");
    }

    private static void unreadAndExpiredSnapshotsBecomeUnknown() {
        ModuleStatusSnapshotStore store = new ModuleStatusSnapshotStore();
        ModuleStatusSnapshotStore.Snapshot unread = store.get("mounthold",
                ModuleStatusSnapshotStore.TARGET_DHXY, 100L);
        eq(ModuleStatus.State.UNKNOWN, unread.state, "first read is unknown");
        eq(ModuleStatusSnapshotStore.Freshness.UNKNOWN, unread.freshness,
                "first read has unknown freshness");
        check(!unread.hasSample, "first read has no sample");

        store.publish("mounthold", ModuleStatusSnapshotStore.TARGET_DHXY,
                ModuleStatus.State.RUNNING, 100L);
        ModuleStatusSnapshotStore.Snapshot freshAtBoundary = store.get("mounthold",
                ModuleStatusSnapshotStore.TARGET_DHXY,
                100L + ModuleStatusSnapshotStore.FRESH_FOR_MILLIS);
        eq(ModuleStatus.State.RUNNING, freshAtBoundary.state,
                "freshness boundary is inclusive");
        eq(ModuleStatusSnapshotStore.Freshness.FRESH, freshAtBoundary.freshness,
                "snapshot at freshness boundary is fresh");

        ModuleStatusSnapshotStore.Snapshot expired = store.get("mounthold",
                ModuleStatusSnapshotStore.TARGET_DHXY,
                101L + ModuleStatusSnapshotStore.FRESH_FOR_MILLIS);
        eq(ModuleStatus.State.UNKNOWN, expired.state, "expired state is hidden as unknown");
        eq(ModuleStatusSnapshotStore.Freshness.STALE, expired.freshness,
                "expired freshness is stale");
        check(expired.hasSample, "expired snapshot records that it was previously read");
    }

    private static void moduleAndTargetSnapshotsStayIndependent() {
        ModuleStatusSnapshotStore store = new ModuleStatusSnapshotStore();
        store.publish("mounthold", ModuleStatusSnapshotStore.TARGET_DHXY,
                ModuleStatus.State.RUNNING, 200L);
        store.publish("mounthold", ModuleStatusSnapshotStore.TARGET_XXXY,
                ModuleStatus.State.STOPPED, 201L);
        store.publish("battleloop", ModuleStatusSnapshotStore.TARGET_DHXY,
                ModuleStatus.State.WAITING, 202L);

        eq(ModuleStatus.State.RUNNING, store.get("mounthold",
                ModuleStatusSnapshotStore.TARGET_DHXY, 203L).state,
                "DHXY mount state remains independent");
        eq(ModuleStatus.State.STOPPED, store.get("mounthold",
                ModuleStatusSnapshotStore.TARGET_XXXY, 203L).state,
                "XXXY mount state remains independent");
        eq(ModuleStatus.State.WAITING, store.get("battleloop",
                ModuleStatusSnapshotStore.TARGET_DHXY, 203L).state,
                "another module retains its own state");
        eq(ModuleStatus.State.UNKNOWN, store.get("battleloop",
                ModuleStatusSnapshotStore.TARGET_XXXY, 203L).state,
                "another target remains unread");

        check(!store.publish("mounthold", "com.example.injected",
                ModuleStatus.State.ERROR, 204L), "unknown target is rejected");
        check(!store.publish("mounthold\nsecret=token", ModuleStatusSnapshotStore.TARGET_DHXY,
                ModuleStatus.State.ERROR, 204L), "unknown module id is rejected");
    }

    private static void oldSnapshotCannotReplaceNewerSnapshot() {
        ModuleStatusSnapshotStore store = new ModuleStatusSnapshotStore();
        store.publish("battlepanel", ModuleStatusSnapshotStore.TARGET_DHXY,
                ModuleStatus.State.RUNNING, 500L);
        check(!store.publish("battlepanel", ModuleStatusSnapshotStore.TARGET_DHXY,
                ModuleStatus.State.STOPPED, 499L), "older result is rejected for its module and target");
        eq(ModuleStatus.State.RUNNING, store.get("battlepanel",
                ModuleStatusSnapshotStore.TARGET_DHXY, 500L).state,
                "older result cannot overwrite the current target state");

        check(store.publish("battlepanel", ModuleStatusSnapshotStore.TARGET_XXXY,
                ModuleStatus.State.STOPPED, 10L), "different target has an independent sequence");
        eq(ModuleStatus.State.STOPPED, store.get("battlepanel",
                ModuleStatusSnapshotStore.TARGET_XXXY, 10L).state,
                "a target's timestamp does not constrain another target");
    }

    private static void formatterKeepsOnlyAllowlistedFields() {
        ModuleStatusSnapshotStore store = new ModuleStatusSnapshotStore();
        store.publish("mounthold", ModuleStatusSnapshotStore.TARGET_DHXY,
                ModuleStatus.State.RUNNING, 1_000L);
        store.publish("battleloop", ModuleStatusSnapshotStore.TARGET_DHXY,
                ModuleStatus.State.ERROR, 0L);
        List<ModuleStatusSnapshotStore.DiagnosticState> states =
                store.diagnosticStates(ModuleStatusSnapshotStore.TARGET_DHXY, 6_001L);
        states.add(new ModuleStatusSnapshotStore.DiagnosticState(
                "anlei\nrole=secret", ModuleStatus.State.RUNNING,
                ModuleStatusSnapshotStore.Freshness.FRESH));

        SuiteDiagnostics diagnostics = new SuiteDiagnostics(
                "2026.10.18.1-friends-open-v1", 14L,
                SuiteDiagnostics.LicenseMode.BYPASS,
                Arrays.asList("x86_64", "arm64-v8a", "device-secret"),
                states, "confirmed|09:07");
        String text = DiagnosticsFormatter.format(diagnostics);
        check(text.contains("版本名: 2026.10.18.1-friends-open-v1"),
                "full version name is retained");
        check(text.contains("版本码: 14"), "version code is retained");
        check(text.contains("授权模式: 免激活"), "bypass mode is retained");
        check(text.contains("支持架构: x86_64, arm64-v8a"), "known ABIs are retained");
        check(text.contains("mounthold | 未确认 | 过期"),
                "expired module state is shown as unknown");
        check(text.contains("battleloop | 未确认 | 过期"),
                "expired error state is not repeated as current");
        check(text.contains("anlei | 未确认 | 未读取"), "unread fixed module is listed");
        check(text.contains("最近调度: confirmed|09:07"),
                "approved minute summary is retained");
        check(!text.contains("com.gdoo."), "target package is not copied");
        check(!text.contains("device-secret"), "unknown ABI is omitted");
        check(!text.contains("role=secret"), "unknown module id is omitted");
        check(text.split("\\n").length >= 14, "all fixed modules are listed");

        SuiteDiagnostics untrusted = new SuiteDiagnostics(
                "release\ncode=999", -1L, null,
                Arrays.asList("../../token"), states, "started|token=abcd");
        String safe = DiagnosticsFormatter.format(untrusted);
        check(safe.contains("版本名: unknown"), "version string with line breaks is rejected");
        check(safe.contains("版本码: unknown"), "negative version code is rejected");
        check(safe.contains("授权模式: unknown"), "unknown license mode is not copied");
        check(safe.contains("支持架构: unknown"), "unknown ABI is not copied");
        check(safe.contains("最近调度: unknown|--:--"),
                "invalid schedule text is replaced with a fixed value");
        check(!safe.contains("token=abcd"), "untrusted schedule text is not copied");
    }

    private static void scheduleSummaryRequiresKnownEnumAndMinuteTime() {
        SuiteDiagnostics base = new SuiteDiagnostics("test-1", 1L,
                SuiteDiagnostics.LicenseMode.STANDARD, Arrays.asList("x86_64"), null, null);
        eq("最近调度: none|--:--", DiagnosticsFormatter.format(new SuiteDiagnostics(
                base.versionName, base.versionCode, base.licenseMode, base.supportedAbis,
                base.moduleStates, "none|--:--")).substring(
                DiagnosticsFormatter.format(base).lastIndexOf("最近调度: ")),
                "empty schedule marker uses no time");

        String invalidResult = DiagnosticsFormatter.format(new SuiteDiagnostics(
                base.versionName, base.versionCode, base.licenseMode, base.supportedAbis,
                base.moduleStates, "start|09:07"));
        check(invalidResult.endsWith("最近调度: unknown|--:--"),
                "unapproved schedule result is replaced");
        String invalidClock = DiagnosticsFormatter.format(new SuiteDiagnostics(
                base.versionName, base.versionCode, base.licenseMode, base.supportedAbis,
                base.moduleStates, "failed|29:07"));
        check(invalidClock.endsWith("最近调度: unknown|--:--"),
                "invalid hour is rejected");
        String invalidMinute = DiagnosticsFormatter.format(new SuiteDiagnostics(
                base.versionName, base.versionCode, base.licenseMode, base.supportedAbis,
                base.moduleStates, "failed|09:60"));
        check(invalidMinute.endsWith("最近调度: unknown|--:--"),
                "invalid minute is rejected");
        String misleadingNone = DiagnosticsFormatter.format(new SuiteDiagnostics(
                base.versionName, base.versionCode, base.licenseMode, base.supportedAbis,
                base.moduleStates, "none|09:07"));
        check(misleadingNone.endsWith("最近调度: unknown|--:--"),
                "none cannot carry a timestamp");
    }

    private static void check(boolean condition, String message) {
        assertions++;
        if (!condition) throw new AssertionError(message);
    }

    private static void eq(Object expected, Object actual, String message) {
        check(expected == null ? actual == null : expected.equals(actual),
                message + " expected=" + expected + " actual=" + actual);
    }
}
