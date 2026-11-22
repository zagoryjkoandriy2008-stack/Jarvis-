package com.andriy.jarvis;

/** No packet send implies wake success. Require a NEW, signed, fresh Windows
 * heartbeat after the LAN wake request; never reuse stale online status.
 */
public final class WakeStatusPolicy {
    private WakeStatusPolicy(){}
    public static boolean confirmedOnlineAfterSignal(boolean online,
            long heartbeatMs,long signalMs,long nowMs) {
        return online && signalMs>0 && heartbeatMs>0 && nowMs>=signalMs &&
            heartbeatMs>=signalMs-3000L && heartbeatMs<=nowMs+3000L &&
            nowMs-heartbeatMs<35000L;
    }
}
