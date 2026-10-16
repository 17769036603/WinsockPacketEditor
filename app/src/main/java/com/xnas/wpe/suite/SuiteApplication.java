package com.xnas.wpe.suite;

import android.app.Application;
import android.content.Intent;
import java.util.concurrent.*;

public final class SuiteApplication extends Application {
    private final ScheduledExecutorService supervisor=Executors.newSingleThreadScheduledExecutor();
    private volatile boolean stopping;
    @Override public void onCreate() {
        super.onCreate();
        tick();
        supervisor.scheduleAtFixedRate(this::tick,30,30,TimeUnit.SECONDS);
    }
    private void tick() {
        if (LicenseManager.isActive()) stopping=false;
        if (LicenseManager.configured() && LicenseManager.hasSession(this)) {
            LicenseManager.verify(this,(message,ok)->{ if(!ok && !LicenseManager.isActive()) stopModules(); });
        } else {
            if (LicenseManager.hasSession(this) || LicenseManager.hasStoredLease(this)) LicenseManager.expire(this);
        }
    }
    private synchronized void stopModules() {
        if(stopping)return;stopping=true;
        stop( com.xnas.wpe.anlei.AnleiService.class,"stop");
        stop( com.xnas.wpe.mounthold.MountHoldService.class,com.xnas.wpe.mounthold.MountHoldService.ACTION_STOP);
        stop( com.xnas.wpe.battleloop.BattleLoopService.class,com.xnas.wpe.battleloop.BattleLoopService.STOP);
        stop( com.xnas.wpe.battlepanel.BattleOverlayService.class,com.xnas.wpe.battlepanel.BattleOverlayService.ACTION_STOP);
        stop( com.xnas.wpe.jinchan.ConnectionService.class,"stop");
    }
    private void stop(Class<?> service,String action) {
        try { startService(new Intent(this,service).setAction(action)); } catch(RuntimeException ignored) {}
    }
}
