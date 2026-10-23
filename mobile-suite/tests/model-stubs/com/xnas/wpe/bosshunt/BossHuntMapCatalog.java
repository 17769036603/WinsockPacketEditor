package com.xnas.wpe.bosshunt;

/** Minimal model-test stub: recovery tests never load the Android-backed catalog. */
public final class BossHuntMapCatalog {
    private BossHuntMapCatalog() {
    }

    public static boolean contains(int id) {
        return id > 0;
    }

    public static String nameOf(int id) {
        return "";
    }
}
