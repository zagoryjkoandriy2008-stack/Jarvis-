package com.andriy.jarvis;

/** Process-local phone routing shared by the main and floating assistant.
 * Does not survive reboot or device force-stop; no unintended persistent
 * remote-control mode. Watch7 uses its own foreground screen state. */
public final class JarvisDeviceTargetSession {
    private static volatile JarvisDeviceRelayRules.Target phoneTarget=
        JarvisDeviceRelayRules.Target.NONE;
    private JarvisDeviceTargetSession(){}
    public static JarvisDeviceRelayRules.Target get(){return phoneTarget;}
    public static void select(JarvisDeviceRelayRules.Target value) {
        phoneTarget=value==null?JarvisDeviceRelayRules.Target.NONE:value;
    }
    public static void clear(){phoneTarget=JarvisDeviceRelayRules.Target.NONE;}
}
