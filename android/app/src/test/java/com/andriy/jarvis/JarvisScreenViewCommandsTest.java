package com.andriy.jarvis;

public final class JarvisScreenViewCommandsTest {
    private static void assertThat(boolean value,String detail) {
        if(!value)throw new AssertionError("Screen view: "+detail);
    }
    public static void main(String[] args) {
        for(String speech:new String[]{
            "покажи екран комп'ютера",
            "Покажи екран комп’ютера",
            "Джарвіс покажи екран комп’ютера",
            "дімон відкрий екран комп'ютера",
            "екран комп'ютера",
            "показати екран компютера",
            "покажи екран windows",
            "покажи екран пк",
            "перегляд екрана комп'ютера"
        })assertThat(JarvisScreenViewCommands.open(speech),
            "open only via explicit owner request: "+speech);
        for(String unrelated:new String[]{
            "увімкни пісню Екран комп'ютера",
            "постав таймер на 1 хвилину",
            "вмикай комп'ютер",
            "комп'ютер",
            "телефон",
            "створи нотатку про екран комп'ютера",
            "покажи екран телефону",
            "покажи пароль з комп'ютера",
            "відправ скріншот комп'ютера",
            "записуй відео екрана",
            "",
        })assertThat(!JarvisScreenViewCommands.open(unrelated),
            "do not unexpectedly start streaming: "+unrelated);
        assertThat(!JarvisScreenViewCommands.open(null),"null input");
        System.out.println("PASS JarvisScreenViewCommandsTest");
    }
}
