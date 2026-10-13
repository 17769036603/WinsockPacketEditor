package com.xnas.wpe.suite;

import android.app.Activity;
import android.content.SharedPreferences;

import java.io.File;

/** Base for the original feature screens, with per-module private storage. */
public abstract class ModuleActivity extends Activity {
    private String moduleId;

    final String moduleIdForHost() {
        if (moduleId == null) moduleId = ModulePaths.idFor(getClass());
        return moduleId;
    }

    @Override
    public File getFilesDir() {
        return ModulePaths.moduleDir(super.getFilesDir(), moduleIdForHost());
    }

    @Override
    public File getCacheDir() {
        return ModulePaths.moduleDir(super.getCacheDir(), moduleIdForHost());
    }

    @Override
    public File getCodeCacheDir() {
        return ModulePaths.cacheChild(super.getCacheDir(), moduleIdForHost(), "code_cache");
    }

    @Override
    public SharedPreferences getSharedPreferences(String name, int mode) {
        return super.getSharedPreferences("module." + moduleIdForHost() + "." + name, mode);
    }

    protected final void requestModulePermissions(String[] permissions, int requestCode) {
        if (getParent() instanceof MainActivity) {
            ((MainActivity) getParent()).requestPermissionsFromModule(this, permissions, requestCode);
        } else {
            requestPermissions(permissions, requestCode);
        }
    }
}
