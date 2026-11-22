package com.andriy.jarvis;

import android.content.pm.PackageManager;
import android.net.Uri;
import android.util.Log;

import com.google.android.gms.wearable.DataEvent;
import com.google.android.gms.wearable.DataEventBuffer;
import com.google.android.gms.wearable.DataMapItem;
import com.google.android.gms.wearable.WearableListenerService;

/** Stores phone-owned Spotify alias snapshots on the watch even when the
 * watch Activity is closed. Play Services validates the paired app signature. */
public final class JarvisWearSpotifySyncReceiver extends WearableListenerService {
    @Override public void onDataChanged(DataEventBuffer events) {
        if (!getPackageManager().hasSystemFeature(PackageManager.FEATURE_WATCH))
            return;
        for (DataEvent change : events) {
            if (change.getType() != DataEvent.TYPE_CHANGED) continue;
            Uri uri = change.getDataItem().getUri();
            if (uri == null || !SpotifyPlaylistSync.PATH.equals(uri.getPath()))
                continue;
            try {
                com.google.android.gms.wearable.DataMap map =
                    DataMapItem.fromDataItem(change.getDataItem()).getDataMap();
                SpotifyPlaylistSync.applyOnWatch(this,
                    map.getString("entries"), map.getLong("revision", -1L));
            } catch (RuntimeException invalid) {
                Log.w("DIMON_SPOTIFY_SYNC", "Rejected invalid playlist update", invalid);
            }
        }
    }
}
