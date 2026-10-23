package com.xnas.wpe.suite;

import android.app.Application;
import java.util.concurrent.*;

public final class SuiteApplication extends Application {
    private final ScheduledExecutorService supervisor=Executors.newSingleThreadScheduledExecutor();
    @Override public void onCreate() {
        super.onCreate();
        LicenseManager.initialize(this);
        ScheduleService.startIfNeeded(this);
        tick();
        supervisor.scheduleAtFixedRate(this::tick,30,30,TimeUnit.SECONDS);
    }
    private void tick() {
        if (LicenseManager.BYPASS) {
            LicenseManager.enforceGameVersionRestriction(this);
            return;
        }
        if (LicenseManager.configured() && LicenseManager.hasSession(this)) {
            LicenseManager.verify(this,(message,ok)->{ if(!ok && !LicenseManager.isActive()) LicenseManager.expire(this); });
        } else {
            if (LicenseManager.hasSession(this) || LicenseManager.hasStoredLease(this)) LicenseManager.expire(this);
        }
    }
}
