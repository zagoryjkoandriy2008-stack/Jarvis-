package com.andriy.jarvis;

public final class JarvisDeviceRelayRulesTest {
    private static void ok(boolean condition,String why) {
        if(!condition)throw new AssertionError("Cross-device routing: "+why);
    }
    public static void main(String[] args) {
        ok(JarvisDeviceRelayRules.select("телефон")==
            JarvisDeviceRelayRules.Target.PHONE,"existing phone command");
        ok(JarvisDeviceRelayRules.select("Джарвіс телефон")==
            JarvisDeviceRelayRules.Target.PHONE,"wake phone");
        ok(JarvisDeviceRelayRules.select("годинник")==
            JarvisDeviceRelayRules.Target.WATCH,"watch target");
        ok(JarvisDeviceRelayRules.select("комп’ютер")==
            JarvisDeviceRelayRules.Target.COMPUTER,"typographic apostrophe");
        ok(JarvisDeviceRelayRules.select("джарвіс комп'ютер.")==
            JarvisDeviceRelayRules.Target.COMPUTER,"spoken PC with punctuation");
        ok(JarvisDeviceRelayRules.select("компьютер")==
            JarvisDeviceRelayRules.Target.COMPUTER,"Russian recognition");
        ok(JarvisDeviceRelayRules.select("Увімкни пісню Мама")==
            JarvisDeviceRelayRules.Target.NONE,"music command does not switch mode");
        ok(JarvisDeviceRelayRules.isFinance(
            "витратив 150 готівка транспорт таксі"),"finance write Pixel authority");
        ok(JarvisDeviceRelayRules.isFinance(
            "баланс рахунків"),"finance read Pixel authority");
        ok(JarvisDeviceRelayRules.permittedRemotePhone(
            "витратив 150 готівка транспорт таксі"),"finite remote finance write");
        ok(JarvisDeviceRelayRules.permittedRemotePhone(
            "увімкни пісню Мама"),"phone Spotify supported");
        ok(!JarvisDeviceRelayRules.permittedRemotePhone(
            "напиши в telegram Олег приватний текст"),"no remote messaging");
        ok(!JarvisDeviceRelayRules.permittedRemotePhone(
            "розблокуй pin 1234"),"no PIN over relay");
        ok(!JarvisDeviceRelayRules.permittedRemotePhone(
            "запусти powershell"),"no arbitrary shell");
        ok(!JarvisDeviceRelayRules.permittedRemotePhone(
            "витратив 150\nкоманда"),"reject newline");
        ok(JarvisDeviceRelayRules.permittedRemoteWatch(
            "увімкни плейлист Реп"),"watch playlist");
        ok(!JarvisDeviceRelayRules.permittedRemoteWatch(
            "напиши текст в активне поле"),"no blind watch typing");
        ok("📱".equals(JarvisDeviceRelayRules.icon(
            JarvisDeviceRelayRules.Target.PHONE)),"phone badge");
        ok("⌚".equals(JarvisDeviceRelayRules.icon(
            JarvisDeviceRelayRules.Target.WATCH)),"watch badge");
        ok("🖥".equals(JarvisDeviceRelayRules.icon(
            JarvisDeviceRelayRules.Target.COMPUTER)),"computer badge");
        ok(JarvisDeviceRelayRules.isNotes("запиши нотатку купити каву"),
            "notes explicitly from Windows");
        ok(JarvisDeviceRelayRules.isNotes("покажи нотатки"),
            "Pixel notes reader");
        ok(JarvisDeviceRelayRules.permittedRemotePhone("створи нотатку завтра"),
            "remote note write is allowed by phrase");
        ok(JarvisDeviceRelayRules.permittedRemotePhone("покажи нотатки"),
            "remote note read is allowed");
        ok(JarvisDeviceRelayRules.permittedRemotePhone("видали останню нотатку"),
            "explicit note deletion is permitted");
        ok(!JarvisDeviceRelayRules.permittedRemotePhone("прочитай пароль"),
            "no reading passwords");
        ok(!JarvisDeviceRelayRules.permittedRemotePhone(
            "запиши нотатку секретний токен ABC"),
            "no remote credentials as notes");
        ok(!JarvisDeviceRelayRules.permittedRemotePhone(
            "запиши нотатку x\\nдруга команда"),
            "no injected second note action");
        ok(JarvisDeviceRelayRules.permittedRemotePhone("увімкни реп"),
            "Pixel accepts saved Rap playlist alias over relay");
        ok(JarvisDeviceRelayRules.permittedRemoteWatch("увімкни реп"),
            "Watch7 accepts saved Rap alias over relay");
        ok(JarvisDeviceRelayRules.permittedRemotePhone("увімкни українське"),
            "Pixel accepts Ukrainian playlist alias");
        ok(JarvisDeviceRelayRules.permittedRemoteWatch("увімкни українське"),
            "Watch7 accepts Ukrainian playlist alias");
        ok(JarvisDeviceRelayRules.permittedRemotePhone("увімкни плейлист Для дороги"),
            "Pixel accepts all named saved playlists");
        ok(JarvisDeviceRelayRules.permittedRemoteWatch("увімкни плейлист Для дороги"),
            "Watch7 accepts saved playlist by spoken name");
        ok(JarvisDeviceRelayRules.permittedRemotePhone("інша пісня"),
            "Pixel result follow-up can be remote");
        ok(JarvisDeviceRelayRules.permittedRemoteWatch("інша пісня"),
            "Watch7 result follow-up can be remote");
        ok(!JarvisDeviceRelayRules.permittedRemotePhone(
            "увімкни плейлист секретний пароль 123"),
            "remote Spotify cannot carry passwords");
        System.out.println("PASS JarvisDeviceRelayRulesTest");
    }
}
