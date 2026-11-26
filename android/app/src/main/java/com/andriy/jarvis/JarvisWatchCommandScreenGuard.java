package com.andriy.jarvis;

import android.content.Context;
import android.content.pm.PackageManager;
import android.os.PowerManager;
import android.util.Log;

/**
 * Keeps the Watch7 display lit ONLY during an explicit on-watch UI command.
 * MainActivity FLAG_KEEP_SCREEN_ON cannot cover Spotify / Maps foreground.
 * The deprecated screen-bright wake level is needed specifically while
 * another app owns the window; it is bounded and always released.
 *
 * Wear OS may override this for battery saver, ambient mode, screen lock,
 * or when the user presses the hardware power button.
 */
public final class JarvisWatchCommandScreenGuard {
    private static final String TAG="DIMON_WATCH_SCREEN";
    private static final long MAX_HOLD_MS=65000L;
    private static PowerManager.WakeLock hold;

    private JarvisWatchCommandScreenGuard() {}

    @SuppressWarnings("deprecation")
    public static synchronized void begin(Context context) {
        end();
        if(context==null ||
           !context.getPackageManager().hasSystemFeature(PackageManager.FEATURE_WATCH))
            return;
        try {
            PowerManager manager=(PowerManager)context.getSystemService(Context.POWER_SERVICE);
            if(manager==null ||
               !manager.isWakeLockLevelSupported(PowerManager.SCREEN_BRIGHT_WAKE_LOCK)) {
                Log.w(TAG,"Screen-bright wake lock not supported on this Wear OS build");
                return;
            }
            PowerManager.WakeLock lock=manager.newWakeLock(
                PowerManager.SCREEN_BRIGHT_WAKE_LOCK,
                "com.andriy.jarvis:watch_command_screen");
            lock.setReferenceCounted(false);
            lock.acquire(MAX_HOLD_MS); // automatic system timeout, even after crash
            hold=lock;
        }catch(RuntimeException denied) {
            Log.w(TAG,"Wear OS denied command screen guard",denied);
            hold=null;
        }
    }

    public static synchronized void end() {
        PowerManager.WakeLock lock=hold;
        hold=null;
        if(lock==null)return;
        try {
            if(lock.isHeld())lock.release();
        }catch(RuntimeException error) {
            Log.w(TAG,"Unable to release Watch7 screen guard",error);
        }
    }

    static long maxHoldMs(){return MAX_HOLD_MS;}
}
