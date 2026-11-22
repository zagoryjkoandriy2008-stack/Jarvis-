package com.andriy.jarvis;

import java.util.regex.Pattern;

/** Single, auditable allowlist for Pixel's versioned device HTTP transport.
 * Plain Java: tested separately without producing an Android APK.
 */
public final class JarvisDeviceV2Paths {
    private JarvisDeviceV2Paths(){}
    private static final Pattern SIGNED=Pattern.compile(
        "^/device/v2/(?:pair/verify|commands/(?:send|result|heartbeat|status))$");

    /** Paths that MUST carry a per-device P-256 signature. */
    public static boolean signedPhonePath(String path){
        return path!=null && SIGNED.matcher(path).matches();
    }

    /** HTTP transport permits enrollment; it never signs enrollment on an
     * unpaired device or accepts arbitrary v2 routes from a remote response.
     */
    public static boolean httpAllowed(String path){
        return signedPhonePath(path) || "/device/v2/pair/request".equals(path);
    }
}
