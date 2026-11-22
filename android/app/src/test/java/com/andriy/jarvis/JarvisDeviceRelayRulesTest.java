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
        System.out.println("PASS JarvisDeviceRelayRulesTest");
    }
}
