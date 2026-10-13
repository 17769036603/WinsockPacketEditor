package com.xnas.wpe.suite;

import android.content.Context;

import java.io.File;

final class ModulePaths {
    private ModulePaths() {
    }

    static String idFor(Class<?> type) {
        String packageName = type.getPackage() == null ? "" : type.getPackage().getName();
        switch (packageName) {
            case "com.xnas.wpe.anlei": return "anlei";
            case "com.xnas.wpe.mounthold": return "mounthold";
            case "com.xnas.wpe.battleloop": return "battleloop";
            case "com.xnas.wpe.battleskip": return "battleskip";
            case "com.xnas.wpe.jinchan": return "jinchan";
            case "com.xnas.wpe.battlepanel": return "battlepanel";
            default: throw new IllegalStateException("Unregistered suite module: " + packageName);
        }
    }

    static File moduleDir(File base, String moduleId) {
        File directory = new File(new File(base, "modules"), moduleId);
        if (!directory.isDirectory() && !directory.mkdirs()) {
            throw new IllegalStateException("Cannot create module directory: " + directory);
        }
        return directory;
    }

    static File cacheChild(File baseCacheDir, String moduleId, String child) {
        File directory = new File(new File(new File(baseCacheDir, "modules"), moduleId), child);
        if (!directory.isDirectory() && !directory.mkdirs()) {
            throw new IllegalStateException("Cannot create module cache directory: " + directory);
        }
        return directory;
    }
}
