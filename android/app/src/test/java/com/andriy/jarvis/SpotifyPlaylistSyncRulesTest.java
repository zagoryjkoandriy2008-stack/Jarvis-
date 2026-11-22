package com.andriy.jarvis;

/** JDK-only regression tests for phone->watch Spotify alias transport. */
public final class SpotifyPlaylistSyncRulesTest {
    private static final String ONE="1234567890123456789012";
    private static final String TWO="ABCDEFGHIJKLMNOPQRSTUV";
    private static void check(boolean ok,String message) {
        if(!ok)throw new AssertionError(message);
    }
    private static void eq(String actual,String expected,String message) {
        check(expected.equals(actual),message+" actual="+actual);
    }
    public static void main(String[] args) {
        String raw="Реп | https://open.spotify.com/playlist/"+ONE+
            "?si=frompixel\\nУкр муз | spotify:playlist:"+TWO+
            "\\nТренування | https://open.spotify.com/playlist/"+TWO;
        String entries=SpotifyPlaylistSyncRules.canonicalEntries(raw);
        check(entries!=null,"should validate owned playlist links");
        eq(SpotifyPlaylistSyncRules.resolvePlaylist(entries,
            SpotifyMusicCommands.Kind.RAP,"реп"),
            "spotify:playlist:"+ONE,"rap must use saved Remote URI");
        eq(SpotifyPlaylistSyncRules.resolvePlaylist(entries,
            SpotifyMusicCommands.Kind.UKRAINIAN,"українські пісні"),
            "spotify:playlist:"+TWO,"Ukrainian alias mapped to Remote");
        eq(SpotifyPlaylistSyncRules.resolvePlaylist(entries,
            SpotifyMusicCommands.Kind.PLAYLIST,"Тренування"),
            "spotify:playlist:"+TWO,"named playlist exact alias");
        check(SpotifyPlaylistSyncRules.resolvePlaylist(entries,
            SpotifyMusicCommands.Kind.TRACK,"Реп")==null,
            "song search must remain independent");
        eq(SpotifyPlaylistSyncRules.playlistLink("spotify:playlist:"+ONE),
            "https://open.spotify.com/playlist/"+ONE,"only spotify deep link");
        check(SpotifyPlaylistSyncRules.playlistLink("spotify:track:"+ONE)==null,
            "track must not be opened as playlist");
        check(SpotifyPlaylistSyncRules.playlistLink("https://evil.example/"+ONE)==null,
            "untrusted external URLs rejected");
        check(SpotifyPlaylistSyncRules.canonicalEntries(
            "Реп | spotify:playlist:"+ONE+"\\nреп | spotify:playlist:"+TWO)==null,
            "case-insensitive duplicate aliases rejected");
        check(SpotifyPlaylistSyncRules.canonicalEntries(
            "Реп | https://evil.example/playlist/"+ONE)==null,
            "untrusted snapshot rejected in full");
        check(SpotifyPlaylistSyncRules.canonicalEntries(
            "Реп | spotify:playlist:"+ONE+"\\nBad line")==null,
            "malformed line rejected atomically");
        check(SpotifyPlaylistSyncRules.canonicalEntries("")!=null,
            "empty source means explicit deletion");
        check(SpotifyPlaylistSyncRules.resolvePlaylist("",
            SpotifyMusicCommands.Kind.RAP,"реп")==null,
            "cleared aliases must not be resurrected");
        StringBuilder many=new StringBuilder();
        for(int i=0;i<61;i++)
            many.append("Alias"+i+" | spotify:playlist:"+ONE+"\\n");
        check(SpotifyPlaylistSyncRules.canonicalEntries(many.toString())==null,
            "too many entries rejected");
        System.out.println("SpotifyPlaylistSyncRulesTest OK");
    }
}
