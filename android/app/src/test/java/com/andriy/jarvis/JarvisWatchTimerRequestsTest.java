package com.andriy.jarvis;

/** Watch7 native Timer commands must never be handed to Pixel by default. */
public final class JarvisWatchTimerRequestsTest {
    private static void check(boolean value,String detail) {
        if(!value)throw new AssertionError("Watch native Timer: "+detail);
    }
    private static void starts(String voice,int seconds) {
        JarvisWatchTimerRequests.Request request=JarvisWatchTimerRequests.parse(voice);
        check(request!=null && request.action==JarvisWatchTimerRequests.Action.START &&
            request.seconds==seconds,voice+" must use "+seconds+" seconds");
    }
    public static void main(String[] args) {
        starts("постав таймер на 1 хвилину",60);
        starts("Джарвіс постав таймер на 5 хвилин",300);
        starts("таймер на 30 секунд",30);
        starts("встанови таймер на 2 години",7200);
        starts("запусти таймер на 3 хв",180);
        starts("включи таймер на 10 секунд",10);
        starts("увімкни таймер на 1 годину",3600);
        starts("постав таймер на 3 години",10800);
        starts("постав таймер на 4 години",-1);
        starts("таймер на 0 секунд",-1);
        for(String voice:new String[]{"постав таймер","таймер",
                "відкрий таймер","відкрий програму таймер"}) {
            JarvisWatchTimerRequests.Request r=JarvisWatchTimerRequests.parse(voice);
            check(r!=null && r.action==JarvisWatchTimerRequests.Action.OPEN,
                "no-time command opens Watch7 Timer UI: "+voice);
        }
        for(String voice:new String[]{"скасуй таймер","зупини таймер",
                "вимкни таймер"}) {
            JarvisWatchTimerRequests.Request r=JarvisWatchTimerRequests.parse(voice);
            check(r!=null && r.action==JarvisWatchTimerRequests.Action.DISMISS,
                "stop command must open watch timer management: "+voice);
        }
        for(String voice:new String[]{"запиши нотатку постав таймер на 5 хвилин",
                "скільки залишилося таймеру","що таке таймер",
                "постав таймер на 5 хвилин та скажи привіт",
                "знайди таймер на 3 хвилини","увімкни пісню Таймер"}) {
            check(JarvisWatchTimerRequests.parse(voice)==null,
                "unrelated speech must not launch Samsung Timer: "+voice);
        }
        check(JarvisWatchTimerRequests.parse(null)==null,
            "null should not launch a timer");
        System.out.println("PASS JarvisWatchTimerRequestsTest");
    }
}
