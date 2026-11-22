package com.andriy.jarvis;

import java.util.Locale;
import java.util.regex.Pattern;

/** Explicit target routing. Bare commands switch the mode, not run as AI.
 * Existing Watch7 "телефон" entry and "годинник" exit are kept intact. */
public final class JarvisDeviceRelayRules {
    public enum Target { NONE, PHONE, WATCH, COMPUTER }
    private JarvisDeviceRelayRules() {}

    public static Target select(String spoken) {
        if(spoken==null || spoken.length()>100)return Target.NONE;
        String s=spoken.toLowerCase(Locale.forLanguageTag("uk-UA")).trim()
            .replaceAll("[.,!?;:—]+$","").trim()
            .replaceFirst("^(?:джарвіс|джарвис|jarvis|дімон|димон|dimon)\\s+","")
            .replaceAll("\\s+"," ");
        switch(s) {
            case "телефон":case "режим телефон":case "phone":return Target.PHONE;
            case "годинник":case "режим годинник":case "watch":return Target.WATCH;
            case "комп'ютер":case "комп’ютер":case "компютер":
            case "компьютер":case "комп'ютор":case "computer":
            case "режим комп'ютер":case "пк":return Target.COMPUTER;
            default:return Target.NONE;
        }
    }
    public static String icon(Target target) {
        switch(target) {
            case PHONE:return "📱";
            case WATCH:return "⌚";
            case COMPUTER:return "🖥";
            default:return "🤖";
        }
    }
    public static boolean isFinance(String utterance) {
        if(utterance==null || utterance.isEmpty() || utterance.length()>250)return false;
        String s=utterance.toLowerCase(Locale.forLanguageTag("uk-UA")).trim()
            .replaceFirst("^(?:джарвіс|джарвис|jarvis|дімон|димон|dimon)\\s+","");
        if(FinanceVoiceCommands.parse(utterance)!=null ||
           FinanceInsightsCommands.parse(utterance)!=null ||
           FinanceVoiceFollowups.parse(utterance)!=null ||
           FinanceBatchCommands.looksLikeBatch(utterance) ||
           FinanceVoiceQueryMatcher.matches(s))return true;
        return s.equals("відкрий фінанси") || s.equals("відкрий фінансовий менеджер") ||
            s.equals("фінансовий звіт") || s.equals("звіти") ||
            s.matches("^(?:скільки|покажи|який|яка)\\s+.*(?:витрат|дохід|баланс|бюджет|капітал|чайов|крипто|платіж|грошей).*$");
    }
    /** Only user-initiated, bounded, low-privilege remote commands.
     * The receiver validates the command AGAIN before doing any action.
     * No arbitrary shell, passwords, screen-unlock, Telegram sending or AI. */
    public static boolean permittedRemotePhone(String utterance) {
        if(utterance==null || utterance.isEmpty() || utterance.length()>250 ||
           utterance.indexOf('\n')>=0 || utterance.indexOf('\r')>=0 ||
           utterance.indexOf('\u0000')>=0)return false;
        String s=utterance.toLowerCase(Locale.ROOT).trim()
            .replaceFirst("^(?:джарвіс|джарвис|jarvis|дімон|димон|dimon)\\s+","");
        if(s.contains("пароль")||s.contains("pin")||s.contains("токен")||
           s.contains("api key")||s.contains("розблок"))return false;
        return isFinance(utterance) ||
            s.matches("^(?:увімкни|включи|ввімкни|умімкни)\\s+(?:пісню|песню|плейлист|плейліст)\\s+.+$") ||
            s.equals("наступна пісня") || s.equals("інша пісня") ||
            s.equals("не та пісня") ||
            s.matches("^(?:постав таймер на|таймер)\\s+[0-9]{1,4}\\s+.+$") ||
            s.startsWith("гучність ") || s.startsWith("постав гучність на ") ||
            s.equals("ліхтарик") || s.equals("увімкни ліхтарик") ||
            s.equals("вимкни ліхтарик") ||
            s.matches("^(?:відкрий|запусти)\\s+(?:spotify|спотіфай|браузер|калькулятор|ютуб|youtube)$");
    }
    public static boolean permittedRemoteWatch(String utterance) {
        if(utterance==null || utterance.length()>250)return false;
        String s=utterance.toLowerCase(Locale.ROOT).trim();
        return !s.contains("пароль") && !s.contains("pin") &&
            (s.matches("(?iu)^(?:(?:джарвіс|дімон)\\s+)?(?:увімкни|включи|ввімкни|умімкни)\\s+(?:пісню|песню|плейлист|плейліст)\\s+.+$") ||
            s.matches("(?iu)^(?:відкрий|відкрити)\\s+(?:spotify|спотіфай)$") ||
            s.matches("(?iu)^(?:знайди дорогу до|проклади маршрут до)\\s+.+$") ||
            s.matches("(?iu)^(?:скажи)\\s+.{1,100}$"));
    }
}
