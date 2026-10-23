package com.xnas.wpe.suite;

public final class LicenseLeaseWindowSelfTest {
    private static int assertions;
    public static void main(String[] args) {
        eq(121000L, LicenseLeaseWindow.deadline(1000, 2000, 1700000120000L, 1700000000000L, 120));
        eq(61000L, LicenseLeaseWindow.deadline(1000, 60000, 1700000060000L, 1700000000000L, 60));
        rejects(() -> LicenseLeaseWindow.deadline(1000, 2000, 1700000119000L, 1700000000000L, 120));
        rejects(() -> LicenseLeaseWindow.deadline(1000, 2000, 1700000200000L, 1700000000000L, 121));
        rejects(() -> LicenseLeaseWindow.deadline(1000, 61000, 1700000060000L, 1700000000000L, 60));
        rejects(() -> LicenseLeaseWindow.deadline(Long.MAX_VALUE - 2, Long.MAX_VALUE - 1,
                1700000060000L, 1700000000000L, 60));
        rejects(() -> LicenseLeaseWindow.deadline(1000, 999, 1700000060000L, 1700000000000L, 60));
        if (assertions != 7) throw new AssertionError("expected 7 assertions, got " + assertions);
        System.out.println("LicenseLeaseWindowSelfTest PASS (" + assertions + " assertions)");
    }
    private static void eq(long expected, long actual) {
        assertions++;
        if (expected != actual) throw new AssertionError("expected " + expected + " got " + actual);
    }
    private static void rejects(Runnable call) {
        assertions++;
        try { call.run(); } catch (IllegalArgumentException expected) { return; }
        throw new AssertionError("expected lease rejection");
    }
}
