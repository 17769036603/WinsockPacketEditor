package com.xnas.wpe.suite;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/** Explicit allowlist DTO used only to create copyable diagnostics. */
public final class SuiteDiagnostics {
    public enum LicenseMode {
        STANDARD,
        BYPASS
    }

    public final String versionName;
    public final long versionCode;
    public final LicenseMode licenseMode;
    public final List<String> supportedAbis;
    public final List<ModuleStatusSnapshotStore.DiagnosticState> moduleStates;
    public final String scheduleSummary;

    public SuiteDiagnostics(String versionName, long versionCode, LicenseMode licenseMode,
                            List<String> supportedAbis,
                            List<ModuleStatusSnapshotStore.DiagnosticState> moduleStates,
                            String scheduleSummary) {
        this.versionName = versionName;
        this.versionCode = versionCode;
        this.licenseMode = licenseMode;
        this.supportedAbis = immutableCopy(supportedAbis);
        this.moduleStates = immutableCopy(moduleStates);
        this.scheduleSummary = scheduleSummary;
    }

    private static <T> List<T> immutableCopy(List<T> values) {
        if (values == null || values.isEmpty()) return Collections.emptyList();
        return Collections.unmodifiableList(new ArrayList<>(values));
    }
}
