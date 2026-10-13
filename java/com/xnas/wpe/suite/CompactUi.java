package com.xnas.wpe.suite;

import android.content.Context;
import android.content.res.Configuration;
import android.content.res.Resources;
import android.util.DisplayMetrics;

/** Creates a smaller resource context for the suite's Activities. */
public final class CompactUi {
    private static final float DENSITY_SCALE = 0.8f;
    private static final float FONT_SCALE = 0.9f;

    private CompactUi() {
    }

    public static Context wrap(Context base) {
        Context application = base.getApplicationContext();
        Resources baselineResources = application == null
                ? base.getResources()
                : application.getResources();
        Configuration baseline = baselineResources.getConfiguration();

        int sourceDensityDpi = baseline.densityDpi;
        if (sourceDensityDpi <= 0) {
            float density = baselineResources.getDisplayMetrics().density;
            sourceDensityDpi = density > 0f
                    ? Math.round(density * DisplayMetrics.DENSITY_DEFAULT)
                    : DisplayMetrics.DENSITY_DEFAULT;
        }

        float sourceFontScale = baseline.fontScale > 0f ? baseline.fontScale : 1f;
        Configuration override = new Configuration();
        override.densityDpi = Math.max(1, Math.round(sourceDensityDpi * DENSITY_SCALE));
        override.fontScale = sourceFontScale * FONT_SCALE;
        return base.createConfigurationContext(override);
    }
}
