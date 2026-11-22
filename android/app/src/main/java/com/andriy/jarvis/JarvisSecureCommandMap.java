package com.andriy.jarvis;
import java.util.Locale;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
/** Pure, strict mapping: no arbitrary natural-language arguments reach PC. */
public final class JarvisSecureCommandMap {
    public static final class Action {
        public final String kind;
        public final int volume;
        public final String app;
        public final boolean approval;
        Action(String kind,int volume,String app,boolean approval) {
            this.kind=kind;this.volume=volume;this.app=app;this.approval=approval;
        }
    }
    private static final Pattern VOLUME=Pattern.compile(
        "^(?:(?:зроби|постав|встанови)\\s+)?гучність\\s+(?:(?:пк|windows|комп['’]?ютера)\\s+)?(?:на\\s+)?(\\d{1,3})\\s*%?$");
    private JarvisSecureCommandMap(){}
    public static Action fromVoice(String utterance) {
        if(utterance==null || utterance.length()>250 ||
           utterance.indexOf('\n')>=0 || utterance.indexOf('\r')>=0 ||
           utterance.indexOf('\0')>=0)return null;
        String v=utterance.trim().toLowerCase(Locale.forLanguageTag("uk-UA"))
            .replaceAll("^[.,!?;:]+|[.,!?;:]+$","").trim()
            .replaceFirst("^(?:джарвіс|джарвис|jarvis|дімон|димон)\\s+","")
            .replaceAll("\\s+"," ");
        String s=v.replaceFirst("(?iu)\\s+(?:на|у|в)\\s+(?:комп['’]?ютері|пк|windows)$","").trim();
        if(s.matches("^(?:стан|статус)\\s+(?:пк|комп['’]?ютера|windows)$") ||
           s.matches("^(?:який|покажи)\\s+стан\\s+(?:пк|комп['’]?ютера)$"))
            return new Action("system.status",-1,"",false);
        if(s.matches("^(?:покажи|відкрий)\\s+(?:мої\\s+)?пристрої$") ||
           s.equals("статус пристроїв"))
            return new Action("devices.list",-1,"",false);
        if(s.equals("наступна пісня")||s.equals("наступний трек")||
           s.equals("інша пісня")||s.equals("не та пісня"))
            return new Action("media.next",-1,"",false);
        if(s.equals("попередня пісня")||s.equals("попередній трек"))
            return new Action("media.previous",-1,"",false);
        if(s.equals("пауза")||s.equals("продовж музику")||
           s.equals("відтворення")||s.equals("призупини музику"))
            return new Action("media.toggle",-1,"",false);
        Matcher m=VOLUME.matcher(v);
        if(m.matches()){
            int level=Integer.parseInt(m.group(1));
            return level<=100?new Action("volume.set",level,"",false):null;
        }
        if(s.equals("заблокуй windows")||s.equals("заблокуй пк")||
           s.matches("^заблокуй\\s+комп['’]?ютер$"))
            return new Action("windows.lock",-1,"",true);
        if(s.equals("вимкни монітор")||s.equals("виключи монітор")||
           s.equals("вимкни екран пк"))
            return new Action("monitor.off",-1,"",true);
        if(s.matches("^(?:відкрий|запусти)\\s+(?:блокнот|notepad)$"))
            return new Action("application.open",-1,"notepad",false);
        if(s.matches("^(?:відкрий|запусти)\\s+(?:браузер|browser)$"))
            return new Action("application.open",-1,"browser",false);
        if(s.matches("^(?:відкрий|запусти)\\s+(?:калькулятор|calculator)$"))
            return new Action("application.open",-1,"calculator",false);
        return null;
    }
}
