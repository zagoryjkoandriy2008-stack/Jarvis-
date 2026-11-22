import com.andriy.jarvis.JarvisScreenViewCommands;

/** JVM-only QA for Pixel file commands; no Android/Gradle/APK needed. */
public final class WindowsFileVoiceContract {
    private static int checks;
    private static void assertCommand(boolean actual,boolean expected,String name) {
        checks++;
        if(actual!=expected)throw new AssertionError("Command mismatch: "+name);
    }
    public static void main(String[] ignored) {
        for(String phrase:new String[]{
            "файли комп'ютера", "Джарвіс обмін файлами",
            "файли пк.", "Дімон покажи файли комп’ютера",
            "Jarvis файли Windows", "обмін файлами з пк"
        }) assertCommand(JarvisScreenViewCommands.files(phrase),true,phrase);
        for(String phrase:new String[]{
            "покажи екран комп'ютера","відкрий блокнот",
            "файл пароль", "", "файли годинника",
            "відкрий windows powershell"
        }) assertCommand(JarvisScreenViewCommands.files(phrase),false,phrase);
        assertCommand(JarvisScreenViewCommands.files("а".repeat(111)),false,
            "overlength");
        assertCommand(JarvisScreenViewCommands.open("джарвіс покажи екран пк"),true,
            "existing screen command unchanged");
        assertCommand(JarvisScreenViewCommands.open("файли пк"),false,
            "file command must not start screen stream");
        System.out.println("PASS Pixel Windows file voice contracts: "+checks);
    }
}
