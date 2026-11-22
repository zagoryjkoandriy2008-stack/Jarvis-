package com.andriy.jarvis;

/** Side-effect-free safety gates for Wear OS navigation and search. */
public final class JarvisWatchUiRules {
    private JarvisWatchUiRules() {}

    /** Last-resort Watch7 walking-card tap, only AFTER the destination
     * was selected, route cards were verified, and a short UP reveal swipe.
     * The walking card is BETWEEN public-transit and bicycle cards on the
     * user's screenshot. Never use the former 84% bottom-row (bicycle) tap. */
    public static final float MAPS_WALKING_FALLBACK_Y=0.56f;

    /** A gentle UP gesture that moves one route card slightly into view,
     * instead of scrolling the route list to an unrelated screen. */
    public static final float MAPS_ROUTE_SHORT_SWIPE_START_Y=0.75f;
    public static final float MAPS_ROUTE_SHORT_SWIPE_END_Y=0.55f;
    /** Short UP moves the middle walking card closer to upper-middle.
     * Use only after Maps route-stage confirmation, not on search/home. */
    public static final float MAPS_WALKING_AFTER_SWIPE_Y=0.45f;

    public static boolean mapsWalkingLabel(String text) {
        if(text==null)return false;
        String normalized=text.toLowerCase(java.util.Locale.ROOT);
        return normalized.matches("(?su).*\\b(?:walking|walk|pedestrian)\\b.*") ||
            normalized.contains("пішки") || normalized.contains("пішох") ||
            normalized.contains("пешеход") || normalized.contains("пешком") ||
            normalized.contains("ходьба") || normalized.contains("пішого");
    }

    public static boolean mapsDistanceLabel(String text) {
        if(text==null)return false;
        return java.util.regex.Pattern.compile(
            "(?iuU).*\\b[0-9]+(?:[.,][0-9]+)?\\s*(?:км|km)\\b.*")
            .matcher(text).matches();
    }

    /** Wear Maps sometimes hides every route label after a gesture.
     * An already selected destination and a completed short UP are
     * prerequisites; no tap on Home or behind the IME. */
    public static boolean allowWalkingCardTap(boolean directionsOpened,
            boolean shortSwipeCompleted, boolean mapsForeground,
            boolean mapsHome, boolean keyboardOrSearchOpen,
            boolean routeCardsStillVisible, boolean selectedDestination) {
        return directionsOpened && selectedDestination &&
            shortSwipeCompleted && mapsForeground &&
            !mapsHome && !keyboardOrSearchOpen;
    }

    /** A selected destination on a route screen may expose no
     * accessibility descriptions. After 2.4s of waiting, proceed with
     * the user's explicit short-UP-then-walking-tap sequence. */
    public static boolean allowShortRouteSwipe(boolean destinationSelected,
            boolean mapsForeground,boolean mapsHome,boolean keyboardOpen,
            boolean routesVisible,long elapsedMs) {
        return destinationSelected && mapsForeground &&
            !mapsHome && !keyboardOpen &&
            (routesVisible || elapsedMs>=2400L);
    }

    /** Bike can have no duration; even one duration plus km or mode
     * labels can reliably indicate the route-choices screen. */
    public static boolean mapsRouteCardsLikely(int distinctDurations,
            boolean walkingVisible,boolean distanceVisible,
            boolean otherModeVisible) {
        return distinctDurations>=2 ||
            (walkingVisible && (distanceVisible || distinctDurations>=1)) ||
            (distanceVisible && distinctDurations>=1) ||
            (distinctDurations>=1 && otherModeVisible);
    }

    /** The route cards on Wear Maps show durations, not necessarily the
     * word "Пішки". Match the text of visible duration rows safely. */
    public static boolean mapsRouteDurationLabel(String text) {
        if(text==null)return false;
        return java.util.regex.Pattern.compile(
            "(?iu).*\\b[0-9]+\\s*(?:год|хв|хвилин|hour|hours|hr|mins?|minutes?|мин).*")
            .matcher(text).matches();
    }

    /** We have actually reached the route choices, not a search-result list. */
    public static boolean mapsRouteChoicesConfirmed(int uniqueDurations,
                                                     boolean bottomDurationVisible) {
        return uniqueDurations>=2 && bottomDurationVisible;
    }

    /** After Directions (or a verified route-choice screen), select the
     * user's bottom walking card. Do not tap Search, the IME or Maps Home. */
    public static boolean allowBottomRouteModeTap(boolean directionsOpened,
            boolean mapsForeground,boolean mapsHome,boolean searchOrImeOpen,
            long walkingStageMs) {
        return directionsOpened && mapsForeground && !mapsHome &&
            !searchOrImeOpen && walkingStageMs>=1600L;
    }

    /** Never swipe if Search is already visible or the target app isn't foreground. */
    public static boolean revealSpotifyNavigation(int attempts,long searchAgeMs,
            long sinceLastRevealMs,boolean spotifyVisible,boolean searchAvailable) {
        return attempts>=0 && attempts<3 && searchAgeMs>=3800L &&
            sinceLastRevealMs>=2400L && spotifyVisible && !searchAvailable;
    }

    /** ACTION_SET_TEXT returning true is not evidence that text was inserted. */
    public static boolean spotifyQueryVisible(String expected,String actual) {
        if(expected==null || expected.trim().isEmpty() || actual==null)return false;
        return expected.trim().toLowerCase(java.util.Locale.ROOT)
            .replaceAll("\\s+"," ").equals(actual.trim()
                .toLowerCase(java.util.Locale.ROOT).replaceAll("\\s+"," "));
    }

    /** Wear Spotify SONG search is UP-ONLY. After the initial UP
     * reveal, at most two further UP swipes are allowed only while
     * Search is absent and Spotify remains foreground. Library playlist
     * scrolling remains a separate, unchanged DOWN gesture. */
    public enum WatchSearchRecovery { NONE, UP_AGAIN, EXHAUSTED }

    public static WatchSearchRecovery watchSearchRecovery(int missingSearchRounds,
            boolean searchVisible,boolean spotifyForeground,boolean imeVisible) {
        if(searchVisible || !spotifyForeground || imeVisible)
            return WatchSearchRecovery.NONE;
        return missingSearchRounds>=0 && missingSearchRounds<2
            ?WatchSearchRecovery.UP_AGAIN:WatchSearchRecovery.EXHAUSTED;
    }

    /** A result title belongs to its category subtitle only when both are
     * inside the same visible Watch7 search-card row. No Pixel coordinates. */
    public static boolean watchResultTitleAboveType(int titleY,int typeY,
            int titleX,int typeX,int width,int height) {
        return width>=160 && height>=160 &&
            typeY>titleY && typeY-titleY<=height*.13f &&
            Math.abs(typeX-titleX)<=width*.28f &&
            titleY>height*.16f && titleY<height*.87f;
    }

    /** Do not confuse search chips, nav icons, keyboard or the mini-player
     * with a searchable result on the round Wear display. */
    public static boolean watchResultRegion(int x,int y,int w,int h,
            int screenW,int screenH) {
        return screenW>=160 && screenH>=160 && w>=16 && h>=8 &&
            w<screenW*.95f && h<screenH*.19f &&
            x>screenW*.06f && x<screenW*.94f &&
            y>screenH*.17f && y<screenH*.85f;
    }

    /** Spotify Compose may hide the category for the FIRST row. If a
     * later row exposes a Song type, do not skip the untyped first row.
     * Exclude Watch navigation, filter chips and top query header. */
    public static boolean watchUntypedResultTitle(String title,
            int x,int y,int w,int h,int width,int height) {
        String text=SpotifyTrackSearchRules.normal(title);
        if(text.isEmpty() || text.length()>95 || text.equals("пошук") ||
           text.equals("поиск") || text.equals("search") ||
           text.equals("головна") || text.equals("home") ||
           text.equals("моя бібліотека") || text.equals("library") ||
           text.equals("пісні") || text.equals("песни") ||
           text.equals("songs") || text.equals("плейлісти") ||
           text.equals("плейлисты") || text.equals("playlists") ||
           text.equals("все") || text.equals("всі") ||
           text.equals("усі") || text.equals("all") ||
           text.equals("подкасти") || text.equals("podcasts") ||
           text.equals("виконавці") || text.equals("artists") ||
           SpotifyTrackSearchRules.mixedResultType(title)!=
                SpotifyTrackSearchRules.SearchResultType.UNKNOWN)return false;
        return watchResultRegion(x,y,w,h,width,height) &&
            w>width*.18f && h<height*.17f &&
            y>height*.29f && y<height*.78f;
    }

    /** Watch Spotify may merge a title and its "Плейліст"/"Пісня"
     * subtitle into ONE Accessibility description, separated by a bullet. */
    public static SpotifyTrackSearchRules.SearchResultType watchCombinedType(
            String text) {
        if(text==null)return SpotifyTrackSearchRules.SearchResultType.UNKNOWN;
        SpotifyTrackSearchRules.SearchResultType type=
            SpotifyTrackSearchRules.mixedResultType(text);
        if(type!=SpotifyTrackSearchRules.SearchResultType.UNKNOWN)return type;
        String[] parts=text.split("[,•·\\n]",4);
        if(parts.length<2)return type;
        for(int i=1;i<parts.length;i++) {
            type=SpotifyTrackSearchRules.mixedResultType(parts[i].trim());
            if(type!=SpotifyTrackSearchRules.SearchResultType.UNKNOWN)return type;
        }
        return SpotifyTrackSearchRules.SearchResultType.UNKNOWN;
    }

    public static String watchCombinedTitle(String text) {
        if(text==null)return null;
        String[] parts=text.split("[,•·\\n]",4);
        if(parts.length<2 ||
           SpotifyTrackSearchRules.mixedResultType(parts[0])!=
               SpotifyTrackSearchRules.SearchResultType.UNKNOWN)return null;
        boolean typed=false;
        for(int i=1;i<parts.length;i++)
            if(SpotifyTrackSearchRules.mixedResultType(parts[i])!=
               SpotifyTrackSearchRules.SearchResultType.UNKNOWN)
                typed=true;
        String title=parts[0].trim();
        return typed && !title.isEmpty() && title.length()<=120?title:null;
    }

    /** A named playlist must be discovered directly in the library,
     * never by submitting its name into the Spotify Search tab. */
    public static boolean spotifyPlaylistNameMatches(String wanted,String visible) {
        if(wanted==null || visible==null || wanted.trim().isEmpty())return false;
        String target=wanted.trim().toLowerCase(java.util.Locale.ROOT)
            .replaceAll("\\s+"," ");
        String actual=visible.trim().toLowerCase(java.util.Locale.ROOT)
            .replaceAll("\\s+"," ");
        return actual.equals(target) ||
            actual.equals(target+" playlist") ||
            actual.equals(target+" плейлист") ||
            actual.equals(target+" плейліст");
    }

    /** Retry bounded scrolling only after a visible-name lookup fails.
     * If the accessibility viewport does not change twice, stop. */
    public static boolean mayScrollForPlaylist(int scrollAttempts,
            int unchangedViewports,boolean spotifyForeground) {
        return spotifyForeground && scrollAttempts>=0 &&
            scrollAttempts<16 && unchangedViewports<2;
    }

    /** An unlabelled map icon may only be clicked when all four tiles and
     * the "Recent" heading were recognized in the actual Maps view. */
    public static boolean verifiedMapsHomeGrid(boolean recent,
            boolean upperLeft,boolean upperRight,boolean lowerLeft,boolean lowerRight) {
        return recent && upperLeft && upperRight && lowerLeft && lowerRight;
    }
}
