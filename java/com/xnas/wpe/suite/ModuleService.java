package com.xnas.wpe.suite;

import android.app.Service;
import android.content.SharedPreferences;

import java.io.File;

/** Base for feature services, with storage separate from all other modules. */
public abstract class ModuleService extends Service {
    private String moduleId;

    private String moduleId() {
        if (moduleId == null) moduleId = ModulePaths.idFor(getClass());
        return moduleId;
    }

    @Override
    public File getFilesDir() {
        return ModulePaths.moduleDir(super.getFilesDir(), moduleId());
    }

    @Override
    public File getCacheDir() {
        return ModulePaths.moduleDir(super.getCacheDir(), moduleId());
    }

    @Override
    public File getCodeCacheDir() {
        return ModulePaths.cacheChild(super.getCacheDir(), moduleId(), "code_cache");
    }

    @Override
    public SharedPreferences getSharedPreferences(String name, int mode) {
        return super.getSharedPreferences("module." + moduleId() + "." + name, mode);
    }
}
