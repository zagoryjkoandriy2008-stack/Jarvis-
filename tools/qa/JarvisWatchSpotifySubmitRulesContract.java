package com.andriy.jarvis;

/**
 * JDK-only regressions for Watch7 Spotify submit.
 * The live Accessibility/IME path is validated by Android Java compilation.
 */
public final class JarvisWatchSpotifySubmitRulesContract {
    private static int checks;
    private static void verify(boolean condition,String message) {
        checks++;
        if(!condition)throw new AssertionError(message);
    }

    public static void main(String[] ignored) {
        // Samsung/Google keyboard labels in Ukrainian, English, Russian.
        for(String key:new String[]{"Пошук","Search","SEARCH KEY",
                "Go","Enter","Кнопка пошуку","пОиСк","Найти","Готово"}) {
            verify(JarvisWatchSpotifySubmitRules.isSearchKey(key,null),
                "Valid keyboard submit missing: "+key);
        }
        verify(JarvisWatchSpotifySubmitRules.isSearchKey(null,"Пошук"),
            "Content description Search missing");
        verify(JarvisWatchSpotifySubmitRules.isSearchKey(null,"Search button"),
            "Content description Search button missing");
        for(String wrong:new String[]{"Search Spotify","Пошук пісень",
                "Search for songs", "What do you want to listen to?",
                "Список пісень","Огляд","Наступна пісня",
                "пошуковий результат","Delete","Cancel","Back","Space",""}) {
            verify(!JarvisWatchSpotifySubmitRules.isSearchKey(wrong,null),
                "Not an IME submit key: "+wrong);
        }
        verify(!JarvisWatchSpotifySubmitRules.isSearchKey(null,null),
            "Empty key accepted");
        verify(JarvisWatchSpotifySubmitRules.safeKeyBounds(
            250,365,390,427,480,480),"Visible Search key bounds rejected");
        verify(!JarvisWatchSpotifySubmitRules.safeKeyBounds(
            0,0,480,480,480,480),"Whole keyboard mistaken for Search key");
        verify(!JarvisWatchSpotifySubmitRules.safeKeyBounds(
            -10,350,50,410,480,480),"Offscreen key accepted");
        verify(!JarvisWatchSpotifySubmitRules.safeKeyBounds(
            440,350,600,410,480,480),"Beyond screen key accepted");
        verify(!JarvisWatchSpotifySubmitRules.safeKeyBounds(
            100,100,105,108,480,480),"Tiny icon accepted");
        verify(JarvisWatchSpotifySubmitRules.maySubmit(true,true,true),
            "Valid verified Spotify keyboard rejected");
        verify(!JarvisWatchSpotifySubmitRules.maySubmit(false,true,true),
            "Search allowed behind another app");
        verify(!JarvisWatchSpotifySubmitRules.maySubmit(true,false,true),
            "Search allowed without keyboard");
        verify(!JarvisWatchSpotifySubmitRules.maySubmit(true,true,false),
            "Search allowed with corrupt/unverified title");
        System.out.println("Watch7 Spotify keyboard submit contract: "+
            checks+" checks passed");
    }
}
