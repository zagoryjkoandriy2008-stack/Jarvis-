package com.andriy.jarvis;

import java.util.Locale;

/**
 * Side-effect-free gates for confirming Spotify search on Wear OS.
 * Never infer a key position from Pixel coordinates or tap an unrelated app.
 */
public final class JarvisWatchSpotifySubmitRules {
    private JarvisWatchSpotifySubmitRules() { }

    private static String normalized(String raw) {
        return raw==null ? "" : raw.trim().toLowerCase(Locale.ROOT)
            .replaceAll("\\s+", " ");
    }

    /** Only the exact submit key inside TYPE_INPUT_METHOD, not Spotify's
     * navigation Search tab, suggestions or a keyboard shortcut. */
    public static boolean isSearchKey(String text,String description) {
        return isSubmitLabel(normalized(text)) ||
               isSubmitLabel(normalized(description));
    }

    private static boolean isSubmitLabel(String label) {
        switch(label) {
            case "search": case "search key": case "search button":
            case "go": case "go key": case "done": case "enter":
            case "enter key":
            case "пошук": case "клавіша пошуку": case "кнопка пошуку":
            case "шукати": case "знайти": case "готово": case "ввід":
            case "поиск": case "искать": case "найти": case "ввод":
                return true;
            default:return false;
        }
    }

    /** Reject whole-keyboard/root bounds and off-screen rectangles. */
    public static boolean safeKeyBounds(int left,int top,int right,int bottom,
                                        int screenWidth,int screenHeight) {
        if(screenWidth<160 || screenHeight<160 ||
           left<0 || top<0 || right>screenWidth || bottom>screenHeight)
            return false;
        int width=right-left,height=bottom-top;
        return width>=12 && height>=12 &&
               width<screenWidth*.48f && height<screenHeight*.38f;
    }

    /** Required before touching either keyboard buttons or input connection. */
    public static boolean maySubmit(boolean spotifyVisible,boolean keyboardVisible,
                                    boolean queryVerified) {
        return spotifyVisible && keyboardVisible && queryVerified;
    }
}
