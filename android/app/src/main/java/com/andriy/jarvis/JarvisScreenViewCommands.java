package com.andriy.jarvis;

import java.util.Locale;

/** Explicit voice intent to VIEW the Windows desktop on the Pixel only.
 * View is not remote control and never captures an Android screen. */
public final class JarvisScreenViewCommands {
    private JarvisScreenViewCommands(){}

    /** Pixel-local files page; never routed through Watch7/Windows shell. */
    public static boolean files(String raw) {
        if(raw==null || raw.length()>110)return false;
        String command=raw.trim().toLowerCase(Locale.forLanguageTag("uk-UA"))
            .replace('’','\'').replace('ʼ','\'').replace('′','\'')
            .replaceAll("[.!?]+$","").trim()
            .replaceFirst("^(?:джарвіс|джарвис|jarvis|дімон|димон|dimon)\\s+","")
            .replaceAll("\\s+"," ");
        switch(command) {
            case "файли комп'ютера":
            case "файли з комп'ютера":
            case "відкрий файли комп'ютера":
            case "покажи файли комп'ютера":
            case "обмін файлами":
            case "обмін файлами з пк":
            case "файли пк":
            case "файли windows":
                return true;
            default:return false;
        }
    }

    public static boolean open(String raw) {
        if(raw==null || raw.length()>110)return false;
        String command=raw.trim().toLowerCase(Locale.forLanguageTag("uk-UA"))
            .replace('’','\'').replace('ʼ','\'').replace('′','\'')
            .replaceAll("[.!?]+$","").trim()
            .replaceFirst("^(?:джарвіс|джарвис|jarvis|дімон|димон|dimon)\\s+","")
            .replaceAll("\\s+"," ");
        switch(command) {
            case "покажи екран комп'ютера":
            case "показати екран комп'ютера":
            case "відкрий екран комп'ютера":
            case "дивитися екран комп'ютера":
            case "перегляд екрана комп'ютера":
            case "екран комп'ютера":
            case "покажи екран пк":
            case "покажи екран windows":
            case "покажи екран компьютера":
            case "покажи екран компютера":
            case "показати екран компютера":
                return true;
            default:return false;
        }
    }
}
