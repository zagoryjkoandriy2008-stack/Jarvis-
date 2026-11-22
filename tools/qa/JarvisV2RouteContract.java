package com.andriy.jarvis;

public final class JarvisV2RouteContract {
    private JarvisV2RouteContract(){}
    private static void check(boolean result,String label) {
        if(!result)throw new AssertionError(label);
    }
    public static void main(String[] args) {
        for(String path:new String[]{
            "/device/v2/pair/verify",
            "/device/v2/commands/send",
            "/device/v2/commands/result",
            "/device/v2/commands/heartbeat",
            "/device/v2/commands/status"
        }){
            check(JarvisDeviceV2Paths.httpAllowed(path),"Allowed http: "+path);
            check(JarvisDeviceV2Paths.signedPhonePath(path),"Signed route: "+path);
        }
        check(JarvisDeviceV2Paths.httpAllowed("/device/v2/pair/request"),
            "Enrollment route allowed");
        check(!JarvisDeviceV2Paths.signedPhonePath("/device/v2/pair/request"),
            "Unpaired enrollment cannot be signed");
        for(String path:new String[]{
            "/device/v2/pair/approve",
            "/device/v2/pair/revoke",
            "/device/v2/commands/ack",
            "/device/v2/commands/poll",
            "/device/v2/commands/send?origin=computer",
            "/device/v2/commands/send/../ack",
            "//device/v2/commands/send",
            "https://example.com/device/v2/commands/send",
            "/device/v2/commands/send%2Fanything",
            "/device/v2/commands/preview",
            "/device/v2/commands/send#test"
        })check(!JarvisDeviceV2Paths.httpAllowed(path),"Forbidden route: "+path);
        check(!JarvisDeviceV2Paths.httpAllowed(null),"Null route");
        System.out.println("PASS JARVIS v2 route allowlist and signed transport contracts");
    }
}
