package com.andriy.jarvis;

/**
 * Wear OS Spotify navigation policy. Every NEW Spotify command must discard
 * Spotify's prior Activity task/back stack; "Інша пісня" is the sole exception
 * because it must reuse the existing on-watch search-result cursor.
 *
 * This deliberately does not force-stop Spotify or clear its app data.
 * Android may still restore Spotify-owned UI/media state after a new launch.
 */
public final class JarvisWatchSpotifyLaunchPolicy {
    private JarvisWatchSpotifyLaunchPolicy() {}

    public static boolean freshTaskForMusic(SpotifyMusicCommands.Kind kind,
                                             boolean nextResultFollowup) {
        return !nextResultFollowup &&
            kind != SpotifyMusicCommands.Kind.NEXT_RESULT;
    }

    public static boolean freshTaskForOpenCommand() {
        return true;
    }

    public static boolean clearPreviousSongCursor(SpotifyMusicCommands.Kind kind) {
        return kind != SpotifyMusicCommands.Kind.TRACK &&
            kind != SpotifyMusicCommands.Kind.NEXT_RESULT;
    }
}
