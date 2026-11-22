package com.andriy.jarvis;

/** Regression: Spotify Watch7 should open cleanly except for next-result speech. */
public final class JarvisWatchSpotifyLaunchPolicyTest {
    private static void check(boolean ok,String description) {
        if(!ok)throw new AssertionError("Watch Spotify relaunch: "+description);
    }

    public static void main(String[] args) {
        check(JarvisWatchSpotifyLaunchPolicy.freshTaskForOpenCommand(),
            "explicit Open Spotify must create a fresh task");
        for(SpotifyMusicCommands.Kind kind:new SpotifyMusicCommands.Kind[]{
                SpotifyMusicCommands.Kind.TRACK,SpotifyMusicCommands.Kind.PLAYLIST,
                SpotifyMusicCommands.Kind.RAP,SpotifyMusicCommands.Kind.UKRAINIAN}) {
            check(JarvisWatchSpotifyLaunchPolicy.freshTaskForMusic(kind,false),
                "new track, saved playlist, rap and Ukrainian must reset old UI: "+kind);
            check(!JarvisWatchSpotifyLaunchPolicy.freshTaskForMusic(kind,true),
                "explicit result follow-up must never destroy the search page");
        }
        check(!JarvisWatchSpotifyLaunchPolicy.freshTaskForMusic(
            SpotifyMusicCommands.Kind.NEXT_RESULT,true),
            "Next Result preserves the prior result context");
        check(JarvisWatchSpotifyLaunchPolicy.clearPreviousSongCursor(
            SpotifyMusicCommands.Kind.PLAYLIST),"playlist clears old track cursor");
        check(JarvisWatchSpotifyLaunchPolicy.clearPreviousSongCursor(
            SpotifyMusicCommands.Kind.RAP),"rap clears old track cursor");
        check(!JarvisWatchSpotifyLaunchPolicy.clearPreviousSongCursor(
            SpotifyMusicCommands.Kind.TRACK),"track selection retains new cursor");
        check(!JarvisWatchSpotifyLaunchPolicy.clearPreviousSongCursor(
            SpotifyMusicCommands.Kind.NEXT_RESULT),"next retains current cursor");
        System.out.println("PASS JarvisWatchSpotifyLaunchPolicyTest");
    }
}
