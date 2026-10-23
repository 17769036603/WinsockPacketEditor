package com.xnas.wpe.suite;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;

public final class InstallationHint {
    private static final char[] HEX = "0123456789ABCDEF".toCharArray();
    private InstallationHint() {}

    public static String fromInstallationId(String installationId) {
        if (installationId == null) throw new IllegalArgumentException("installationId");
        try {
            byte[] digest=MessageDigest.getInstance("SHA-256")
                    .digest(installationId.getBytes(StandardCharsets.UTF_8));
            char[] shortHex=new char[12];
            for(int i=0;i<6;i++) {
                int value=digest[i]&0xff;
                shortHex[i*2]=HEX[value>>>4];
                shortHex[i*2+1]=HEX[value&0x0f];
            }
            return new String(shortHex);
        } catch(NoSuchAlgorithmException impossible) {
            throw new IllegalStateException("SHA-256 unavailable",impossible);
        }
    }
}
