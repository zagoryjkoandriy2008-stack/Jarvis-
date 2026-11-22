package com.andriy.jarvis;

import java.util.HashSet;

/** Validated, secret-free alias/Spotify URI snapshot shared with Wear OS. */
public final class SpotifyPlaylistSyncRules {
    public static final int MAX_CHARS = 12500;
    public static final int MAX_LINES = 60;
    private SpotifyPlaylistSyncRules() {}

    /** Null means reject the WHOLE snapshot (never partially overwrite Wear). */
    public static String canonicalEntries(String source) {
        if (source == null || source.length() > MAX_CHARS) return null;
        String[] lines = source.split("\\r?\\n", -1);
        if (lines.length > MAX_LINES + 1) return null;
        StringBuilder out = new StringBuilder();
        HashSet<String> used = new HashSet<>();
        int count = 0;
        for (String raw : lines) {
            String line = raw.trim();
            if (line.isEmpty()) continue;
            if (++count > MAX_LINES) return null;
            int divider = line.indexOf('|');
            if (divider <= 0) return null;
            String name = line.substring(0, divider).trim();
            String alias = SpotifyUriRules.normalizedAlias(name);
            String uri = SpotifyUriRules.canonical(line.substring(divider + 1).trim());
            if (name.isEmpty() || name.length() > 75 || alias.isEmpty() ||
                uri == null || !used.add(alias)) return null;
            if (out.length() > 0) out.append('\n');
            out.append(name).append(" | ").append(uri);
        }
        return out.length() <= MAX_CHARS ? out.toString() : null;
    }

    public static String alias(SpotifyMusicCommands.Kind kind, String title) {
        if (kind == null) return null;
        switch (kind) {
            case RAP: return "реп";
            case UKRAINIAN: return "укр муз";
            case PLAYLIST: return title;
            default: return null;
        }
    }

    /** Returns only explicitly saved playlist URIs; never guesses an ID. */
    public static String resolvePlaylist(String entries,
            SpotifyMusicCommands.Kind kind, String title) {
        String requested = alias(kind, title);
        if (requested == null || entries == null) return null;
        String wanted = SpotifyUriRules.normalizedAlias(requested);
        if (wanted.isEmpty()) return null;
        for (String line : entries.split("\\r?\\n")) {
            int divider = line.indexOf('|');
            if (divider <= 0) continue;
            if (!wanted.equals(SpotifyUriRules.normalizedAlias(
                    line.substring(0, divider).trim()))) continue;
            String uri = SpotifyUriRules.canonical(line.substring(divider + 1).trim());
            return uri != null && uri.startsWith("spotify:playlist:") ? uri : null;
        }
        return null;
    }

    /** An Android ACTION_VIEW Spotify content link, not an arbitrary URL. */
    public static String playlistLink(String canonicalUri) {
        String uri = SpotifyUriRules.canonical(canonicalUri);
        if (uri == null || !uri.startsWith("spotify:playlist:")) return null;
        return "https://open.spotify.com/playlist/" +
            uri.substring("spotify:playlist:".length());
    }
}
