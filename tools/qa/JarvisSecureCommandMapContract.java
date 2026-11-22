package com.andriy.jarvis;
public final class JarvisSecureCommandMapContract {
  private static void check(boolean ok,String message) {
    if(!ok)throw new AssertionError(message);
  }
  private static void kind(String phrase,String expected){
    JarvisSecureCommandMap.Action a=JarvisSecureCommandMap.fromVoice(phrase);
    check(a!=null&&expected.equals(a.kind),"kind "+phrase);
  }
  public static void main(String[] args){
    kind("Джарвіс який стан комп'ютера","system.status");
    kind("статус пк","system.status");
    kind("покажи пристрої","devices.list");
    kind("наступна пісня на ПК","media.next");
    kind("пауза","media.toggle");
    kind("відкрий браузер на комп'ютері","application.open");
    kind("вимкни монітор","monitor.off");
    kind("заблокуй windows","windows.lock");
    JarvisSecureCommandMap.Action volume=JarvisSecureCommandMap.fromVoice(
      "зроби гучність ПК 40");
    check(volume!=null&&volume.volume==40,"volume exact 40");
    String[] denied={"видали файли","постав гучність на 101",
      "відкрий powershell","напиши пароль","який баланс карти",
      "увімкни Spotify і введи пароль","запусти макрос секрет",
      "скажи привіт\nвідкрий браузер"};
    for(String phrase:denied)
      check(JarvisSecureCommandMap.fromVoice(phrase)==null,"reject "+phrase);
    System.out.println("PASS signed Windows voice action map");
  }
}
