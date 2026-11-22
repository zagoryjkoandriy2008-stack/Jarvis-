package com.andriy.jarvis;
import java.util.Arrays;
import java.util.Collections;

public final class WatchTtsVoiceChoiceTest {
    private static void ok(boolean v,String error){
        if(!v)throw new AssertionError("Watch7 TTS: "+error);
    }
    public static void main(String[] args){
        ok("uk-UA-voice2".equals(WatchTtsVoiceChoice.availableVoice(
            Arrays.asList("uk-UA-voice1","uk-UA-voice2"),"uk-UA-voice2")),
            "saved installed voice is selected by exact ID");
        ok(WatchTtsVoiceChoice.availableVoice(
            Arrays.asList("uk-UA-voice1"),"uk-UA-removed").isEmpty(),
            "uninstalled voice falls back to Android default");
        ok(WatchTtsVoiceChoice.availableVoice(
            Collections.emptyList(),"uk-UA-voice2").isEmpty(),
            "no Ukrainian voices produces a safe default");
        ok(WatchTtsVoiceChoice.availableVoice(null,null).isEmpty(),
            "uninitialized TTS does not crash voice menu");
        ok(WatchTtsVoiceChoice.pitch(WatchTtsVoiceChoice.LOWER)<
           WatchTtsVoiceChoice.pitch(WatchTtsVoiceChoice.NORMAL),
           "lower pitch is lower");
        ok(WatchTtsVoiceChoice.pitch(WatchTtsVoiceChoice.HIGHER)>
           WatchTtsVoiceChoice.pitch(WatchTtsVoiceChoice.NORMAL),
           "higher pitch is higher");
        ok(WatchTtsVoiceChoice.pitch(777)==1f,
           "corrupt preference safely uses natural pitch");
        ok(WatchTtsVoiceChoice.toneMode(-2)==WatchTtsVoiceChoice.NORMAL,
           "invalid tone is normalized");
        System.out.println("PASS WatchTtsVoiceChoiceTest");
    }
}
