package com.xnas.wpe.suite;

public final class InstallationHintSelfTest {
    public static void main(String[] args) {
        eq("F618DCB1FB3D", InstallationHint.fromInstallationId("piaomiao-installation-hint-test"));
        eq("E3B0C44298FC", InstallationHint.fromInstallationId(""));
        eq(12, InstallationHint.fromInstallationId("安装识别").length());
        System.out.println("InstallationHintSelfTest PASS (3 assertions)");
    }
    private static void eq(Object expected,Object actual) {
        if(!expected.equals(actual))throw new AssertionError("expected "+expected+" got "+actual);
    }
}
