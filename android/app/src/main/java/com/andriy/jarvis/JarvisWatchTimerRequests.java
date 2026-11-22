package com.andriy.jarvis;

import java.util.regex.Pattern;

/**
 * On-watch Timer intents must be intercepted BEFORE generic Watch -> Pixel AI.
 * Pure parser for Java regression tests; only explicit leading timer commands
 * qualify. Never infer a timer from note text or a search/AI question.
 */
public final class JarvisWatchTimerRequests {
    public enum Action { START, OPEN, DISMISS }
    public static final class Request {
        public final Action action;
        public final int seconds; // -1 = invalid duration, 0 = open UI
        Request(Action action,int seconds) {
            this.action=action;this.seconds=seconds;
        }
    }
    private static final Pattern TIMER_DURATION=Pattern.compile(
        "^(?:(?:постав|встанови|запусти|увімкни|умімкни|включи|ввімкни)\\s+)?" +
        "таймер\\s+(?:на\\s+)?\\d{1,4}\\s*" +
        "(?:секунд(?:у|и|ів)?|сек|с|хвилин(?:у|и)?|хв|годин(?:у|и)?|год)" +
        "[.!?]?$",
        Pattern.CASE_INSENSITIVE|Pattern.UNICODE_CASE
    );
    private JarvisWatchTimerRequests(){}

    public static Request parse(String spoken) {
        if(spoken==null || spoken.length()>150)return null;
        String normalized=CommandPatterns.normalizeControlVerb(spoken)
            .replaceFirst("^(?:умімкни|ввімкни)(?=\\s)","увімкни");
        if(normalized.equals("таймер") || normalized.equals("постав таймер") ||
           normalized.equals("відкрий таймер") ||
           normalized.equals("відкрий програму таймер") ||
           normalized.equals("запусти таймер"))
            return new Request(Action.OPEN,0);
        if(normalized.equals("скасуй таймер") ||
           normalized.equals("зупини таймер") ||
           normalized.equals("вимкни таймер"))
            return new Request(Action.DISMISS,0);
        if(!TIMER_DURATION.matcher(normalized).matches())return null;
        Integer duration=CommandPatterns.timerSeconds(normalized);
        if(duration==null)return null;
        return new Request(Action.START,duration);
    }
}
