package com.andriy.jarvis;

import java.util.Locale;

/**
 * Strict, local-only Spotify Connect labels from the WATCH accessibility tree.
 * Never match a generic "phone", a media row or a phone brand/device that
 * the user did not explicitly identify as the "Ваш телефон" target.
 */
public final class JarvisWatchSpotifyPhoneRules {
    private JarvisWatchSpotifyPhoneRules() {}

    private static String normalize(String value) {
        return value==null?"":value.toLowerCase(Locale.ROOT)
            .trim().replaceAll("\\s+"," ");
    }

    private static boolean exactOrDetails(String label,String required) {
        return label.equals(required) ||
            label.startsWith(required+",") ||
            label.startsWith(required+" ·") ||
            label.startsWith(required+" •") ||
            label.startsWith(required+" (") ||
            label.startsWith(required+" —") ||
            label.startsWith(required+" –") ||
            label.startsWith(required+":");
    }

    public static boolean isYourPhone(String text,String description) {
        return target(normalize(text)) || target(normalize(description));
    }

    private static boolean target(String value) {
        return exactOrDetails(value,"ваш телефон") ||
            exactOrDetails(value,"your phone") ||
            exactOrDetails(value,"мій телефон") ||
            exactOrDetails(value,"мой телефон");
    }

    public static boolean isDevicePicker(String text,String description) {
        return picker(normalize(text)) || picker(normalize(description));
    }

    private static boolean picker(String label) {
        switch(label) {
            case "connect to a device":
            case "connect to device":
            case "available devices":
            case "devices available":
            case "connect to devices":
            case "devices":
            case "підключитися до пристрою":
            case "підключити пристрій":
            case "вибрати пристрій":
            case "доступні пристрої":
            case "пристрої":
            case "подключиться к устройству":
            case "выбрать устройство":
            case "доступные устройства":
                return true;
            default:return false;
        }
    }
}
