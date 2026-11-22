package com.andriy.jarvis;

import android.content.Context;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import com.google.android.gms.wearable.DataItem;
import com.google.android.gms.wearable.DataItemBuffer;
import com.google.android.gms.wearable.DataMapItem;
import com.google.android.gms.wearable.PutDataMapRequest;
import com.google.android.gms.wearable.Wearable;

import java.util.concurrent.atomic.AtomicBoolean;

/** Phone-owned Spotify aliases replicated as a persistent Wear OS data item.
 * Only public playlist/track URIs and user-chosen aliases cross the Data Layer.
 * Neither the Spotify Client ID, authorization token nor any secret is sent. */
public final class SpotifyPlaylistSync {
    public static final String PATH = "/jarvis/spotify-playlists/v1";
    private static final String PREFS = "jarvis_watch_spotify_aliases_v1";
    private static final String KEY_ENTRIES = "entries";
    private static final String KEY_REVISION = "revision";
    private static final Handler MAIN = new Handler(Looper.getMainLooper());
    private SpotifyPlaylistSync() {}

    private static boolean isWatch(Context context) {
        return context.getPackageManager().hasSystemFeature(PackageManager.FEATURE_WATCH);
    }

    /** Publish on every successful settings save and once on phone startup.
     * An explicit empty snapshot clears aliases previously sent to the watch. */
    public static void publishFromPhone(Context context) {
        if (isWatch(context)) return;
        String entries = SpotifyPlaylistSyncRules.canonicalEntries(
            SpotifyDirectSettings.entries(context));
        if (entries == null) {
            Log.w("DIMON_SPOTIFY_SYNC", "Rejecting malformed phone playlist snapshot");
            return;
        }
        PutDataMapRequest data = PutDataMapRequest.create(PATH);
        data.getDataMap().putString(KEY_ENTRIES, entries);
        data.getDataMap().putLong(KEY_REVISION, System.currentTimeMillis());
        com.google.android.gms.wearable.PutDataRequest request =
            data.asPutDataRequest();
        request.setUrgent();
        try {
            Wearable.getDataClient(context.getApplicationContext())
                .putDataItem(request)
                .addOnFailureListener(error ->
                    Log.w("DIMON_SPOTIFY_SYNC", "Could not publish playlist aliases", error));
        } catch (RuntimeException unavailable) {
            // Spotify preferences must remain usable even without Play Services.
            Log.w("DIMON_SPOTIFY_SYNC", "Wear Data Layer unavailable on Pixel", unavailable);
        }
    }

    /** Receive a snapshot only on a Watch7; reject invalid data atomically. */
    public static boolean applyOnWatch(Context context, String source, long revision) {
        if (!isWatch(context) || revision < 0) return false;
        String entries = SpotifyPlaylistSyncRules.canonicalEntries(source);
        if (entries == null) return false;
        SharedPreferences preferences = context.getSharedPreferences(PREFS,
            Context.MODE_PRIVATE);
        synchronized (SpotifyPlaylistSync.class) {
            if (revision < preferences.getLong(KEY_REVISION, -1L)) return false;
            return preferences.edit().putString(KEY_ENTRIES, entries)
                .putLong(KEY_REVISION, revision).commit();
        }
    }

    public static String cachedPlaylist(Context context,
            SpotifyMusicCommands.Kind kind, String title) {
        String entries = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .getString(KEY_ENTRIES, "");
        return SpotifyPlaylistSyncRules.resolvePlaylist(entries, kind, title);
    }

    /** Human-readable watch-only diagnostics; never exposes account secrets. */
    public static String summaryOnWatch(Context context) {
        String entries = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
            .getString(KEY_ENTRIES, "");
        if (entries == null || entries.isEmpty())
            return "Плейлистів немає. Перевір Spotify Remote на Pixel, "+
                "збережи налаштування й увімкни з'єднання Galaxy Wearable.";
        StringBuilder names = new StringBuilder();
        int count=0;
        for (String line : entries.split("\\r?\\n")) {
            int divider=line.indexOf('|');
            if(divider<=0 || !line.substring(divider+1).trim()
                .startsWith("spotify:playlist:"))continue;
            if(++count<=8) {
                if(names.length()>0) names.append(", ");
                names.append(line.substring(0,divider).trim());
            }
        }
        return "Синхронізовано плейлистів: "+count+
            ". Назви: "+names+(count>8?" та інші.":".")+
            " Якщо «Реп» або «Укр муз» відсутній — додай його на Pixel.";
    }

    public interface Listener {
        void ready(String playlistUri);
    }

    /** Read network-persisted Wear data once per spoken command, then use
     * the local snapshot. A bounded timeout prevents loss of voice control
     * when Pixel is disconnected; late completions update cache for next time. */
    public static void resolveOnWatch(Context context,
            SpotifyMusicCommands.Kind kind, String title, Listener listener) {
        Context app = context.getApplicationContext();
        if (!isWatch(app)) {
            listener.ready(null);
            return;
        }
        AtomicBoolean returned = new AtomicBoolean(false);
        Runnable finish = () -> {
            if (returned.compareAndSet(false, true))
                listener.ready(cachedPlaylist(app, kind, title));
        };
        MAIN.postDelayed(finish, 2800L);
        try {
            Wearable.getDataClient(app).getDataItems()
                .addOnSuccessListener(items -> {
                try {
                    long newest = -1L;
                    String snapshot = null;
                    for (DataItem item : items) {
                        Uri uri = item.getUri();
                        if (uri == null || !PATH.equals(uri.getPath())) continue;
                        try {
                            DataMapItem map = DataMapItem.fromDataItem(item);
                            String candidate = map.getDataMap().getString(KEY_ENTRIES);
                            long revision = map.getDataMap().getLong(KEY_REVISION, -1L);
                            if (candidate != null &&
                                SpotifyPlaylistSyncRules.canonicalEntries(candidate) != null &&
                                revision > newest) {
                                newest = revision;
                                snapshot = candidate;
                            }
                        } catch (RuntimeException invalid) {
                            Log.w("DIMON_SPOTIFY_SYNC", "Ignoring malformed data item", invalid);
                        }
                    }
                    if (snapshot != null) applyOnWatch(app, snapshot, newest);
                } finally {
                    items.release();
                    MAIN.removeCallbacks(finish);
                    MAIN.post(finish);
                }
            }).addOnFailureListener(error -> {
                Log.w("DIMON_SPOTIFY_SYNC", "Wear alias refresh unavailable", error);
                MAIN.removeCallbacks(finish);
                MAIN.post(finish);
            });
        } catch (RuntimeException unavailable) {
            Log.w("DIMON_SPOTIFY_SYNC", "Could not query synced playlist aliases",
                unavailable);
            MAIN.removeCallbacks(finish);
            MAIN.post(finish);
        }
    }
}
