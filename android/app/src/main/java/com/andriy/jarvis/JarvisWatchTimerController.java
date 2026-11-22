package com.andriy.jarvis;

import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import android.provider.AlarmClock;
import android.util.Log;

import java.util.List;

/**
 * Starts Samsung's installed Timer app on the WATCH itself. Unlike the old
 * Watch->Pixel assistant route, no Wear Data Layer or phone AlarmManager is used.
 *
 * ACTION_SET_TIMER + EXTRA_LENGTH is the public Android protocol for starting
 * a timer. Launch success alone is NOT evidence that a particular Samsung
 * Timer build accepted the request; our message says "sent" rather than
 * falsely reporting a verified running timer.
 */
public final class JarvisWatchTimerController {
    public interface Callback { void finished(boolean success,String detail); }
    public static final String SAMSUNG_TIMER="com.samsung.android.watch.timer";
    private static final String TAG="DIMON_WATCH_TIMER";

    private JarvisWatchTimerController(){}

    public static void execute(Context watch,JarvisWatchTimerRequests.Request request,
                               Callback callback) {
        if(watch==null || callback==null || request==null)return;
        if(!watch.getPackageManager().hasSystemFeature(
            PackageManager.FEATURE_WATCH)) {
            callback.finished(false,"Таймер Samsung запускається тільки на годиннику.");
            return;
        }
        if(!(watch instanceof Activity)) {
            callback.finished(false,"Відкрий JARVIS на годиннику і повтори команду.");
            return;
        }
        if(request.action==JarvisWatchTimerRequests.Action.START) {
            if(!TimerRules.valid(request.seconds)) {
                callback.finished(false,"Назви час таймера від 1 секунди до 3 годин.");
                return;
            }
            // Request visible Samsung Timer UI so the user can observe the
            // countdown; SKIP_UI=true may hide whether the timer was accepted.
            Intent timer=new Intent(AlarmClock.ACTION_SET_TIMER);
            timer.putExtra(AlarmClock.EXTRA_LENGTH,request.seconds);
            timer.putExtra(AlarmClock.EXTRA_MESSAGE,"JARVIS");
            timer.putExtra(AlarmClock.EXTRA_SKIP_UI,false);
            timer.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            if(launchSamsungTimerAction(watch,timer)) {
                callback.finished(true,"Передав "+duration(request.seconds)+
                    " у Samsung «Таймер» на Watch7. Перевір, що відлік почався "+
                    "на екрані годинника.");
                return;
            }
            if(launchOtherTimerAction(watch,timer)) {
                callback.finished(true,"Передав "+duration(request.seconds)+
                    " у програму «Таймер» на годиннику. Перевір відлік.");
                return;
            }
            if(openSamsungTimer(watch)) {
                callback.finished(false,"Samsung «Таймер» відкрився на Watch7, "+
                    "але встановлена версія не підтримує автоматичний "+
                    "ACTION_SET_TIMER. Вибери тривалість і натисни старт "+
                    "у програмі «Таймер». На Pixel таймер не запускав.");
            }else callback.finished(false,
                "На Watch7 не знайдено програму «Таймер» з підтримкою "+
                "автоматичного запуску. Перевір встановлені програми годинника.");
            return;
        }
        if(openSamsungTimer(watch)) {
            callback.finished(true,request.action==JarvisWatchTimerRequests.Action.DISMISS
                ?"Відкрив таймер Watch7. Для скасування поточного відліку "+
                  "натисни «Зупинити» в Samsung Timer."
                :"Відкрив Samsung «Таймер» на Watch7. Вибери тривалість.");
        } else callback.finished(false,"Не можу відкрити Samsung «Таймер» "+
            "на Watch7. Перевір, чи встановлена програма.");
    }

    private static boolean launchSamsungTimerAction(Context context,Intent original) {
        PackageManager pm=context.getPackageManager();
        if(pm.getLaunchIntentForPackage(SAMSUNG_TIMER)==null)return false;
        Intent samsung=new Intent(original);
        samsung.setPackage(SAMSUNG_TIMER);
        try {
            context.startActivity(samsung);
            Log.i(TAG,"ACTION_SET_TIMER delivered to Samsung Timer on watch");
            return true;
        } catch(ActivityNotFoundException|SecurityException error) {
            Log.w(TAG,"Samsung Timer does not accept ACTION_SET_TIMER",error);
            return false;
        }
    }

    private static boolean launchOtherTimerAction(Context context,Intent original) {
        // Fallback for non-Samsung Wear OS timers. Never open a chooser or
        // send the request to a phone; limit to the WATCH's installed activity.
        PackageManager pm=context.getPackageManager();
        List<ResolveInfo> handlers=pm.queryIntentActivities(
            original,PackageManager.MATCH_DEFAULT_ONLY);
        for(ResolveInfo info:handlers) {
            if(info.activityInfo==null || !info.activityInfo.exported)continue;
            String pkg=info.activityInfo.packageName;
            if(SAMSUNG_TIMER.equals(pkg))continue; // already tried
            Intent explicit=new Intent(original);
            explicit.setClassName(pkg,info.activityInfo.name);
            try {
                context.startActivity(explicit);
                Log.i(TAG,"ACTION_SET_TIMER delivered to "+pkg);
                return true;
            }catch(ActivityNotFoundException|SecurityException error) {
                Log.w(TAG,"Alternate on-watch Timer rejected intent",error);
            }
        }
        return false;
    }

    private static boolean openSamsungTimer(Context watch) {
        Intent launcher=watch.getPackageManager().getLaunchIntentForPackage(
            SAMSUNG_TIMER);
        if(launcher==null)return false;
        launcher.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK |
            Intent.FLAG_ACTIVITY_REORDER_TO_FRONT);
        try {
            watch.startActivity(launcher);
            return true;
        } catch(RuntimeException error) {
            Log.w(TAG,"Cannot show Samsung Timer home",error);
            return false;
        }
    }

    private static String duration(int seconds) {
        if(seconds%3600==0)return (seconds/3600)+" год";
        if(seconds%60==0)return (seconds/60)+" хв";
        return seconds+" с";
    }
}
