package com.xnas.wpe.suite;

import java.util.Arrays;
import java.util.Base64;

public final class InstallationHintFixtureSelfTest {
    public static void main(String[] args) {
        byte[] fixture = new byte[32];
        Arrays.fill(fixture, (byte) 0xaa);
        String installationId = Base64.getUrlEncoder().withoutPadding().encodeToString(fixture);
        String hint = InstallationHint.fromInstallationId(installationId);
        if (!"CFF45713079D".equals(hint)) {
            throw new AssertionError("installation hint mismatch: " + hint);
        }
        System.out.println("InstallationHintFixtureSelfTest PASS (same 32-byte fixture as server; hint=" + hint + ")");
    }
}
