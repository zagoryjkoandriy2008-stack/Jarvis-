package com.andriy.jarvis;

import java.util.List;

/** Wear OS system-TTS selection. Pure rules are tested without a watch.
 * These preferences are intentionally separate from Pixel neural voices. */
public final class WatchTtsVoiceChoice {
    private WatchTtsVoiceChoice(){}
    public static final String PREF_VOICE_NAME="watchTtsVoiceName";
    public static final String PREF_TONE="watchTtsVoiceTone";
    public static final int LOWER=0;
    public static final int NORMAL=1;
    public static final int HIGHER=2;

    public static String availableVoice(List<String> available,String saved) {
        if(saved==null || saved.isEmpty() || available==null)return "";
        for(String name:available)
            if(saved.equals(name))return name;
        return "";
    }

    public static int toneMode(int value) {
        return value==LOWER || value==NORMAL || value==HIGHER?value:NORMAL;
    }

    public static float pitch(int value) {
        int mode=toneMode(value);
        return mode==LOWER?0.86f:mode==HIGHER?1.12f:1.0f;
    }

    public static String toneLabel(int value) {
        int mode=toneMode(value);
        return mode==LOWER?"нижчий":mode==HIGHER?"вищий":"звичайний";
    }
}
