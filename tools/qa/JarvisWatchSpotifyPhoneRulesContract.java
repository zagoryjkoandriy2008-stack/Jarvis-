package com.andriy.jarvis;

/** JDK-only contract for the post-song Spotify Connect phone target. */
public final class JarvisWatchSpotifyPhoneRulesContract {
    private static int checks;
    private static void check(boolean actual,String description) {
        checks++;
        if(!actual)throw new AssertionError(description);
    }
    public static void main(String[] args) {
        for(String wanted:new String[]{
            "Ваш телефон", "ВАШ ТЕЛЕФОН", "Ваш телефон, підключено",
            "Your phone", "Your phone (Pixel 9)", "Your phone · Available",
            "Мій телефон", "Мой телефон"
        })check(JarvisWatchSpotifyPhoneRules.isYourPhone(wanted,null),
            "Missing exact phone target "+wanted);
        check(JarvisWatchSpotifyPhoneRules.isYourPhone(null,"Ваш телефон"),
            "Accessibility description missing");
        for(String other:new String[]{
            "Ваш годинник","This watch","Watch7",
            "Your phonebook","Your phone number","Your phone cases",
            "Phone","Pixel 9","Search","Пошук","Ваша музика",
            "Your phone is near","Connect to phone","Unknown device",
            "Дімон телефон",""
        })check(!JarvisWatchSpotifyPhoneRules.isYourPhone(other,null),
            "Unsafe loose phone match: "+other);
        check(!JarvisWatchSpotifyPhoneRules.isYourPhone(null,null),
            "Null phone label wrongly accepted");
        for(String opener:new String[]{
            "Connect to a device","Available devices","Devices",
            "Підключитися до пристрою","Вибрати пристрій",
            "Подключиться к устройству"
        })check(JarvisWatchSpotifyPhoneRules.isDevicePicker(opener,null),
            "Device picker label rejected: "+opener);
        for(String wrong:new String[]{
            "This watch","Connected device Pixel 9","Find phone",
            "Пошук","Your phone","Spotify Premium",""
        })check(!JarvisWatchSpotifyPhoneRules.isDevicePicker(wrong,null),
            "Device picker matched wrong control: "+wrong);
        System.out.println("Spotify Watch7 phone output contract: "+
            checks+" assertions passed");
    }
}
