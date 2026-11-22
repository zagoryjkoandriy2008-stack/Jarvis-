package com.andriy.jarvis;

import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.widget.Toast;

/**
 * Watch-only, user-triggered Spotify search/playback. Spotify Remote playlist
 * aliases arrive from Pixel through a public-URI-only Wear Data Layer snapshot;
 * music playback itself stays on Watch7, with no phone screen actions.
 */
public final class JarvisWatchSpotifyController {
    private static final String PACKAGE="com.spotify.music";
    private static final long TOTAL_TIMEOUT_MS=57000L;
    private static final Handler MAIN=new Handler(Looper.getMainLooper());
    private static final SpotifyResultCursor RESULT_CURSOR=new SpotifyResultCursor();

    private JarvisWatchSpotifyController() {}

    public static void play(Context context,SpotifyMusicCommands.Kind kind,
                            String title,JarvisWatchLocalController.Callback completion) {
        if(kind==SpotifyMusicCommands.Kind.NEXT_RESULT) {
            long now=SystemClock.elapsedRealtime();
            int next=RESULT_CURSOR.next(now);
            String lastQuery=RESULT_CURSOR.query(now);
            if(next<0 || lastQuery==null) {
                completion.finished(false,"Немає останнього списку Spotify на Watch7. "+
                    "Спочатку скажи «Увімкни пісню [назва]».");
                return;
            }
            playIndexed(context,SpotifyMusicCommands.Kind.TRACK,
                lastQuery,next,true,completion);
            return;
        }
        if(kind==SpotifyMusicCommands.Kind.RAP ||
           kind==SpotifyMusicCommands.Kind.UKRAINIAN ||
           kind==SpotifyMusicCommands.Kind.PLAYLIST) {
            // Use the same user-owned alias saved in Spotify Remote on Pixel.
            // Await the Wear data snapshot briefly, then continue from cache.
            SpotifyPlaylistSync.resolveOnWatch(context,kind,title,uri -> {
                if(uri==null && (kind==SpotifyMusicCommands.Kind.RAP ||
                                 kind==SpotifyMusicCommands.Kind.UKRAINIAN)) {
                    completion.finished(false,"Немає плейлиста «"+title+
                        "» у синхронізованих Spotify Remote налаштуваннях. "+
                        "Збережи його URI на Pixel і перевір зв'язок Watch7.");
                    return;
                }
                playIndexed(context,kind,title,0,false,completion,uri);
            });
            return;
        }
        playIndexed(context,kind,title,0,false,completion,null);
    }

    private static void playIndexed(Context context,SpotifyMusicCommands.Kind kind,
            String title,int resultOrdinal,boolean followup,
            JarvisWatchLocalController.Callback completion) {
        playIndexed(context,kind,title,resultOrdinal,followup,completion,null);
    }

    private static void playIndexed(Context context,SpotifyMusicCommands.Kind kind,
            String title,int resultOrdinal,boolean followup,
            JarvisWatchLocalController.Callback completion,String syncedPlaylistUri) {
        JarvisWatchSpotifyTrace.mark(context,"PLAY_ENTERED");
        if(kind!=SpotifyMusicCommands.Kind.TRACK &&
           kind!=SpotifyMusicCommands.Kind.PLAYLIST &&
           kind!=SpotifyMusicCommands.Kind.RAP &&
           kind!=SpotifyMusicCommands.Kind.UKRAINIAN) {
            completion.finished(false,"Команда Spotify не підтримується на Watch7.");
            return;
        }
        if(title==null || title.trim().isEmpty() || title.length()>75) {
            completion.finished(false,"Назва пісні або плейлиста не розпізнана.");
            return;
        }
        JarvisWatchSpotifyTrace.mark(context,"CHECK_ACCESSIBILITY");
        if(!JarvisWatchScreenService.available(context)) {
            JarvisWatchSpotifyTrace.mark(context,"ACCESSIBILITY_UNAVAILABLE");
            completion.finished(false,"Увімкни на годиннику службу "+
                "«Дімон — екран годинника» у спеціальних можливостях.");
            return;
        }
        JarvisWatchSpotifyTrace.mark(context,"CHECK_SPOTIFY_INSTALL");
        Intent intent=context.getPackageManager().getLaunchIntentForPackage(PACKAGE);
        if(intent==null) {
            JarvisWatchSpotifyTrace.mark(context,"SPOTIFY_LAUNCHER_NOT_FOUND");
            completion.finished(false,"Установи Spotify на Galaxy Watch7 та увійди в акаунт.");
            return;
        }
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK |
            Intent.FLAG_ACTIVITY_REORDER_TO_FRONT);
        boolean deepLinkOpened=false;
        String playlistLink=SpotifyPlaylistSyncRules.playlistLink(syncedPlaylistUri);
        if(playlistLink!=null) {
            Intent deep=new Intent(Intent.ACTION_VIEW,Uri.parse(playlistLink));
            deep.setPackage(PACKAGE); // Never send the saved link to a browser or Pixel.
            deep.addCategory(Intent.CATEGORY_BROWSABLE);
            deep.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK |
                Intent.FLAG_ACTIVITY_REORDER_TO_FRONT);
            try {
                if(deep.resolveActivity(context.getPackageManager())!=null) {
                    context.startActivity(deep);
                    deepLinkOpened=true;
                    JarvisWatchSpotifyTrace.mark(context,"SYNCED_PLAYLIST_LINK_OPENED");
                }
            } catch(RuntimeException unsupported) {
                android.util.Log.w("DIMON_WATCH_SPOTIFY",
                    "Spotify Wear did not accept playlist content link",unsupported);
            }
        }
        if(!deepLinkOpened) {
            try {
                JarvisWatchSpotifyTrace.mark(context,"START_ACTIVITY_REQUESTED");
                JarvisDiagnostics.event(JarvisDiagnostics.Event.EXTERNAL_LAUNCH,1);
                context.startActivity(intent);
                JarvisWatchSpotifyTrace.mark(context,"START_ACTIVITY_RETURNED");
            } catch(RuntimeException failure) {
                android.util.Log.e("DIMON_WATCH_SPOTIFY","Failed to launch Spotify",failure);
                JarvisWatchSpotifyTrace.mark(context,"START_ACTIVITY_FAILED");
                completion.finished(false,"Wear OS не вдалося відкрити Spotify.");
                return;
            }
        }
        if(kind==SpotifyMusicCommands.Kind.TRACK && !followup)
            RESULT_CURSOR.begin(title,SystemClock.elapsedRealtime());
        Session session=new Session(context.getApplicationContext(),
            kind,title,resultOrdinal,followup,completion,
            playlistLink!=null,deepLinkOpened);
        session.status("Відкриваю Spotify на годиннику…");
        // Do not begin tapping until Spotify actually owns the foreground.
        // This also distinguishes failed app launch from a UI automation failure.
        MAIN.postDelayed(()->session.waitForSpotify(SystemClock.elapsedRealtime()+13000L),
            900L);
    }

    private static final class Session {
        final Context context;
        final SpotifyMusicCommands.Kind kind;
        final String title;
        final int resultOrdinal;
        final boolean followup;
        final boolean syncedPlaylist;
        final boolean deepLinkOpened;
        boolean deepLinkFallback;
        final long launchAt=SystemClock.elapsedRealtime();
        final JarvisWatchLocalController.Callback callback;
        final long deadline=SystemClock.elapsedRealtime()+TOTAL_TIMEOUT_MS;
        long searchStart=SystemClock.elapsedRealtime();
        long stageStart=SystemClock.elapsedRealtime();
        int revealAttempts;
        long lastRevealAt;
        int queryRetries;
        int keyboardDismissals;
        int searchRecoveryRound;
        String selectedPlaylistTitle;
        int playlistScrolls;
        int unchangedPlaylistViews;
        String lastPlaylistViewport="";
        boolean finished;
        final JarvisCallbackGate steps=new JarvisCallbackGate();

        Session(Context context,SpotifyMusicCommands.Kind kind,String title,
                int resultOrdinal,boolean followup,
                JarvisWatchLocalController.Callback callback,
                boolean syncedPlaylist,boolean deepLinkOpened) {
            this.context=context;this.kind=kind;this.title=title;
            this.resultOrdinal=resultOrdinal;this.followup=followup;
            this.syncedPlaylist=syncedPlaylist;
            this.deepLinkOpened=deepLinkOpened;
            this.callback=callback;
            MAIN.postDelayed(()->done(false,"Час очікування Spotify минув."),TOTAL_TIMEOUT_MS);
        }

        void status(String detail) {
            if(finished || !callback.active())return;
            Toast.makeText(context,detail,Toast.LENGTH_SHORT).show();
        }

        void done(boolean ok,String detail) {
            if(finished || !callback.active())return;
            finished=true;
            steps.cancel();
            JarvisWatchSpotifyTrace.finish(context,ok);
            Toast.makeText(context,detail,Toast.LENGTH_SHORT).show();
            callback.finished(ok,detail);
            // Restore the Jarvis watch face after all spoken playback
            // commands (track, playlist, rap, Ukrainian), even on failure.
            // Do not attempt background recording: MainActivity enables the
            // microphone only once the assistant is foreground again.
            if(SpotifyMusicCommands.resumeWatchListeningAfter(kind))
                returnToJarvisForListening();
        }

        private void returnToJarvisForListening() {
            try{
                Intent intent=new Intent(context,MainActivity.class);
                intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK |
                    Intent.FLAG_ACTIVITY_REORDER_TO_FRONT |
                    Intent.FLAG_ACTIVITY_SINGLE_TOP);
                intent.putExtra(MainActivity.EXTRA_WATCH_SPOTIFY_LISTEN,true);
                context.startActivity(intent);
            }catch(RuntimeException failure){
                android.util.Log.w("DIMON_WATCH_SPOTIFY",
                    "Cannot restore Jarvis watch microphone",failure);
            }
        }

        void waitForSpotify(long expires) {
            if(finished || !callback.active())return;
            if(JarvisWatchScreenService.foreground(PACKAGE)) {
                JarvisWatchSpotifyTrace.mark(context,"SPOTIFY_FOREGROUND");
                if(kind==SpotifyMusicCommands.Kind.TRACK) {
                    // Reuse the previous results when possible. To find the
                    // Spotify Search icon, reveal navigation with UP only;
                    // no right/back Home-reset swipes on Watch7.
                    if(followup && JarvisWatchScreenService.spotifySearchResultsVisible(title))
                        run(JarvisWatchScreenService.SpotifyStep.SELECT_RESULT);
                    else if(JarvisWatchScreenService.spotifySearchAvailable())
                        run(JarvisWatchScreenService.SpotifyStep.OPEN_SEARCH);
                    else
                        run(JarvisWatchScreenService.SpotifyStep.REVEAL_NAV);
                } else {
                    if(syncedPlaylist && deepLinkOpened && !deepLinkFallback) {
                        // A successful ACTION_VIEW request alone is NOT proof
                        // Spotify opened the chosen playlist. Verify the page
                        // and its title before touching Play.
                        if(JarvisWatchScreenService.spotifySelectedPlaylistPageReady(
                                title,null)) {
                            run(JarvisWatchScreenService.SpotifyStep.PLAY_PLAYLIST);
                            return;
                        }
                        if(SystemClock.elapsedRealtime()-launchAt<6500L) {
                            MAIN.postDelayed(()->waitForSpotify(expires),650L);
                            return;
                        }
                        deepLinkFallback=true;
                        status("Spotify не підтвердив посилання плейлиста. "+
                            "Шукаю його у бібліотеці Watch7…");
                    }
                    // Named/synced playlists use the library DOWN flow.
                    // The separate TRACK song search remains strictly UP-only.
                    run(JarvisWatchScreenService.SpotifyStep.REVEAL_NAV);
                }
                return;
            }
            if(SystemClock.elapsedRealtime()>=expires) {
                JarvisWatchSpotifyTrace.mark(context,"SPOTIFY_NOT_FOREGROUND");
                done(false,"Spotify не з'явився на екрані Watch7. "+
                    "Скажи «Відкрий Spotify на годиннику» для перевірки.");
                return;
            }
            MAIN.postDelayed(()->waitForSpotify(expires),2000L);
        }

        void run(JarvisWatchScreenService.SpotifyStep step) {
            if(finished || !callback.active())return;
            JarvisWatchSpotifyTrace.mark(context,"STEP_"+step.name());
            long now=SystemClock.elapsedRealtime();
            if(now>deadline) {
                done(false,"Spotify не завершив пошук на годиннику. "+
                    "Перевір екран, інтернет і авторизацію.");
                return;
            }
            if(kind==SpotifyMusicCommands.Kind.TRACK &&
               selectedPlaylistTitle!=null &&
               step==JarvisWatchScreenService.SpotifyStep.PLAY_PLAYLIST &&
               !JarvisWatchScreenService.spotifySelectedPlaylistPageReady(
                   selectedPlaylistTitle,title)) {
                if(now-stageStart>12000L)
                    done(false,"Сторінку плейлиста «"+selectedPlaylistTitle+
                        "» не підтверджено. Play не натискав навмання.");
                else MAIN.postDelayed(()->run(step),850L);
                return;
            }
            if(kind==SpotifyMusicCommands.Kind.TRACK &&
               step==JarvisWatchScreenService.SpotifyStep.REVEAL_NAV &&
               JarvisWatchScreenService.spotifySearchAvailable()) {
                run(JarvisWatchScreenService.SpotifyStep.OPEN_SEARCH);
                return;
            }
            perform(step,next(step));
        }

        private JarvisWatchScreenService.SpotifyStep next(
            JarvisWatchScreenService.SpotifyStep step) {
            switch(step) {
                case SEARCH_RECOVER_UP:
                    return JarvisWatchScreenService.SpotifyStep.OPEN_SEARCH;
                case REVEAL_NAV:
                    return kind==SpotifyMusicCommands.Kind.PLAYLIST || syncedPlaylist
                        ?JarvisWatchScreenService.SpotifyStep.FIND_LIBRARY_PLAYLIST
                        :JarvisWatchScreenService.SpotifyStep.OPEN_SEARCH;
                case FIND_LIBRARY_PLAYLIST:
                    return JarvisWatchScreenService.SpotifyStep.PLAY_PLAYLIST;
                case SCROLL_LIBRARY_PLAYLISTS:
                    return JarvisWatchScreenService.SpotifyStep.FIND_LIBRARY_PLAYLIST;
                case OPEN_SEARCH:
                    return JarvisWatchScreenService.SpotifyStep.FOCUS_SEARCH;
                case FOCUS_SEARCH:
                    return JarvisWatchScreenService.SpotifyStep.SET_QUERY;
                case SET_QUERY:
                    return JarvisWatchScreenService.SpotifyStep.VERIFY_QUERY;
                case VERIFY_QUERY:
                    return JarvisWatchScreenService.SpotifyStep.SUBMIT_QUERY;
                case SUBMIT_QUERY:
                    return JarvisWatchScreenService.SpotifyStep.SELECT_RESULT;
                case SELECT_RESULT:
                    return kind==SpotifyMusicCommands.Kind.TRACK ? null :
                        JarvisWatchScreenService.SpotifyStep.PLAY_PLAYLIST;
                case PLAY_PLAYLIST:
                    return JarvisWatchScreenService.SpotifyStep.VERIFY_PLAYLIST;
                default:return null;
            }
        }

        private void perform(JarvisWatchScreenService.SpotifyStep step,
                             JarvisWatchScreenService.SpotifyStep next) {
            final long stepToken=steps.begin();
            JarvisWatchScreenService.Result result;
            try {
                String effectiveTitle=kind==SpotifyMusicCommands.Kind.TRACK &&
                    selectedPlaylistTitle!=null &&
                    (step==JarvisWatchScreenService.SpotifyStep.PLAY_PLAYLIST ||
                     step==JarvisWatchScreenService.SpotifyStep.VERIFY_PLAYLIST)
                        ?selectedPlaylistTitle:title;
                result=JarvisWatchScreenService.spotifyStep(step,effectiveTitle,
                    kind!=SpotifyMusicCommands.Kind.TRACK &&
                    step!=JarvisWatchScreenService.SpotifyStep.PLAY_PLAYLIST &&
                    step!=JarvisWatchScreenService.SpotifyStep.VERIFY_PLAYLIST,
                    kind==SpotifyMusicCommands.Kind.RAP ||
                    kind==SpotifyMusicCommands.Kind.UKRAINIAN,
                    resultOrdinal,(ok,detail)->{
                    if(finished || !callback.active() || !steps.finish(stepToken))return;
                    if(!ok) {
                        done(false,"Wear OS скасувала натискання у Spotify.");
                        return;
                    }
                    advance(step,next);
                });
            } catch(RuntimeException failure) {
                // Some Wear OS versions invalidate accessibility nodes during
                // rapid Spotify/IME window transitions. Report a controlled
                // failure instead of crashing the watch's assistant process.
                android.util.Log.e("DIMON_WATCH_SPOTIFY",
                    "Failed Spotify stage "+step,failure);
                done(false,"Spotify змінив екран під час пошуку. "+
                    "Дімон залишився активним; спробуй ще раз.");
                return;
            }
            switch(result) {
                case DONE:
                    if(steps.finish(stepToken))advance(step,next);
                    break;
                case RUNNING:
                    // Gesture callback advances exactly once on completion.
                    break;
                case WAIT:
                    steps.finish(stepToken);
                    if(step==JarvisWatchScreenService.SpotifyStep.OPEN_SEARCH &&
                       kind==SpotifyMusicCommands.Kind.TRACK) {
                        if(JarvisWatchScreenService.spotifySearchAvailable()) {
                            MAIN.postDelayed(()->run(step),350L);
                            break;
                        }
                        if(SystemClock.elapsedRealtime()-stageStart<1500L) {
                            MAIN.postDelayed(()->run(step),550L);
                            break;
                        }
                        JarvisWatchUiRules.WatchSearchRecovery plan=
                            JarvisWatchUiRules.watchSearchRecovery(
                                searchRecoveryRound,
                                JarvisWatchScreenService.spotifySearchAvailable(),
                                JarvisWatchScreenService.foreground(PACKAGE),
                                JarvisWatchScreenService.spotifyKeyboardVisible());
                        if(plan==JarvisWatchUiRules.WatchSearchRecovery.UP_AGAIN) {
                            searchRecoveryRound++;
                            status("Пошуку не видно. Повторюю тільки свайп угору ("+
                                (searchRecoveryRound+1)+"/3)...");
                            MAIN.postDelayed(()->run(
                                JarvisWatchScreenService.SpotifyStep.SEARCH_RECOVER_UP),
                                600L);
                        } else if(plan==JarvisWatchUiRules.WatchSearchRecovery.EXHAUSTED) {
                            done(false,"Після трьох свайпів угору іконки пошуку Spotify "+
                                "не знайдено. Вправо не свайпаю.");
                        } else MAIN.postDelayed(()->run(step),650L);
                    } else if(step==JarvisWatchScreenService.SpotifyStep.FIND_LIBRARY_PLAYLIST) {
                        // Search visible playlist names directly. The scroll is
                        // attempted only AFTER the current viewport was checked.
                        String visible=JarvisWatchScreenService.spotifyPlaylistViewport();
                        if(playlistScrolls>0 && !visible.isEmpty() &&
                           visible.equals(lastPlaylistViewport))
                            unchangedPlaylistViews++;
                        else unchangedPlaylistViews=0;
                        lastPlaylistViewport=visible;
                        if(JarvisWatchUiRules.mayScrollForPlaylist(
                            playlistScrolls,unchangedPlaylistViews,
                            JarvisWatchScreenService.foreground(PACKAGE))) {
                            MAIN.postDelayed(
                                ()->run(JarvisWatchScreenService.SpotifyStep.SCROLL_LIBRARY_PLAYLISTS),
                                2000L);
                        } else done(false,"Плейлист «"+title+"» не знайдено у видимому "+
                            "списку Spotify на Watch7. Пошук не запускав.");
                    } else if(step==JarvisWatchScreenService.SpotifyStep.PLAY_PLAYLIST &&
                       SystemClock.elapsedRealtime()-stageStart>12000L) {
                        done(false,"Плейлист відкрито, але кнопку відтворення "+
                            "на годиннику не знайдено.");
                    } else if(step==JarvisWatchScreenService.SpotifyStep.VERIFY_PLAYLIST &&
                              SystemClock.elapsedRealtime()-stageStart>9000L) {
                        done(false,"Натиснув відтворення плейлиста, але Spotify "+
                            "не підтвердив запуск музики.");
                    } else if(step==JarvisWatchScreenService.SpotifyStep.SELECT_RESULT &&
                              SystemClock.elapsedRealtime()-stageStart>11000L) {
                        done(false,"Spotify Watch7 не показав результат №"+
                            (resultOrdinal+1)+" у доступному списку. "+
                            "Не натискав інші кнопки навмання.");
                    } else if(step==JarvisWatchScreenService.SpotifyStep.VERIFY_QUERY &&
                              SystemClock.elapsedRealtime()-stageStart>8500L) {
                        if(++queryRetries<=2) {
                            // ACTION_SET_TEXT is idempotent. Retry only if the
                            // actual Spotify search field still lacks the text.
                            MAIN.postDelayed(()->run(JarvisWatchScreenService.SpotifyStep.SET_QUERY),2000L);
                        } else done(false,"Клавіатура Spotify відкрилася, але "+
                            "назва пісні не з'явилася в полі пошуку Watch7.");
                    } else {
                        MAIN.postDelayed(()->run(step),2000L);
                    }
                    break;
                case BLOCKED:
                default:
                    done(false,"Spotify не дозволив цю дію на годиннику.");
            }
        }

        /** A result without visible type may open a playlist detail.
         * If so, use the actual PLAY step. A completed tap alone is
         * never claimed to prove audio playback. */
        private void confirmUnknownSearchResult(String chosenTitle,long tappedAt) {
            if(finished || !callback.active())return;
            if(JarvisWatchScreenService.spotifySelectedPlaylistPageReady(
                    chosenTitle,title)) {
                selectedPlaylistTitle=chosenTitle;
                RESULT_CURSOR.selected(resultOrdinal,
                    SystemClock.elapsedRealtime(),true,chosenTitle);
                stageStart=SystemClock.elapsedRealtime();
                status("Відкрився плейлист «"+chosenTitle+
                    "». Натискаю Play…");
                MAIN.postDelayed(()->run(
                    JarvisWatchScreenService.SpotifyStep.PLAY_PLAYLIST),450L);
                return;
            }
            if(SystemClock.elapsedRealtime()-tappedAt<3400L) {
                MAIN.postDelayed(()->confirmUnknownSearchResult(
                    chosenTitle,tappedAt),700L);
                return;
            }
            done(true,"Натиснув результат №"+(resultOrdinal+1)+
                " у Spotify Watch7. Android підтвердив жест, але "+
                "сам запуск музики цим ще не доведений.");
        }

        private void advance(JarvisWatchScreenService.SpotifyStep step,
                             JarvisWatchScreenService.SpotifyStep next) {
            if(finished || !callback.active())return;
            if(step==JarvisWatchScreenService.SpotifyStep.SELECT_RESULT &&
               kind==SpotifyMusicCommands.Kind.TRACK) {
                JarvisWatchScreenService.SpotifySearchSelection chosen=
                    JarvisWatchScreenService.spotifyLastSelectedSearchResult();
                boolean playlist=chosen!=null && chosen.playlist;
                String chosenTitle=chosen==null?null:chosen.title;
                RESULT_CURSOR.selected(resultOrdinal,SystemClock.elapsedRealtime(),
                    playlist,chosenTitle);
                if(playlist && chosenTitle!=null) {
                    selectedPlaylistTitle=chosenTitle;
                    status("Результат №"+(resultOrdinal+1)+" — плейлист «"+
                        chosenTitle+"». Чекаю Play...");
                    stageStart=SystemClock.elapsedRealtime();
                    MAIN.postDelayed(()->run(
                        JarvisWatchScreenService.SpotifyStep.PLAY_PLAYLIST),1200L);
                } else if(chosenTitle!=null) {
                    // A first search result can be a playlist whose type
                    // subtitle was hidden by Spotify Compose. Inspect the
                    // destination page before claiming playback succeeded.
                    status("Торкнувся результату №"+(resultOrdinal+1)+
                        ". Перевіряю, чи це плейлист…");
                    final long tappedAt=SystemClock.elapsedRealtime();
                    MAIN.postDelayed(()->confirmUnknownSearchResult(
                        chosenTitle,tappedAt),750L);
                } else {
                    done(false,"Android завершив натискання, але назва "+
                        "першого результату не доступна. Відтворення "+
                        "Spotify не підтверджено.");
                }
                return;
            }
            if(step==JarvisWatchScreenService.SpotifyStep.VERIFY_PLAYLIST) {
                if(kind==SpotifyMusicCommands.Kind.TRACK &&
                   selectedPlaylistTitle!=null) {
                    // No right-swipe after Play. Bring Jarvis microphone back,
                    // then use saved query + ordinal on "Не та пісня".
                    done(true,"Плейлист «"+selectedPlaylistTitle+
                        "» запущено. Дімон слухає. «Не та пісня» "+
                        "вибере наступний результат без свайпів вправо.");
                } else {
                    done(true,"Spotify показує відтворення плейлиста «"+title+
                        "» на Watch7.");
                }
                return;
            }
            if(next==null) {
                done(false,"Не вдалося завершити сценарій Spotify.");return;
            }
            if(step==JarvisWatchScreenService.SpotifyStep.SCROLL_LIBRARY_PLAYLISTS)
                playlistScrolls++;
            if(step==JarvisWatchScreenService.SpotifyStep.REVEAL_NAV)
                status(kind==SpotifyMusicCommands.Kind.TRACK
                    ?"Відкриваю пошук Spotify свайпом угору…"
                    :"Відкриваю бібліотеку Spotify свайпом униз…");
            if(step==JarvisWatchScreenService.SpotifyStep.FIND_LIBRARY_PLAYLIST)
                status("Знайшов плейлист «"+title+"». Відкриваю без пошуку.");
            if(step==JarvisWatchScreenService.SpotifyStep.OPEN_SEARCH)
                status("Відкрито пошук Spotify.");
            if(step==JarvisWatchScreenService.SpotifyStep.SET_QUERY)
                status("Шукаю «"+title+"»…");
            if(step==JarvisWatchScreenService.SpotifyStep.SELECT_RESULT)
                status("Результат вибрано. Перевіряю його тип…");
            stageStart=SystemClock.elapsedRealtime();
            long delay=step==JarvisWatchScreenService.SpotifyStep.SUBMIT_QUERY?
                1600L:step==JarvisWatchScreenService.SpotifyStep.REVEAL_NAV?
                1400L:step==JarvisWatchScreenService.SpotifyStep.SEARCH_RECOVER_UP?
                950L:step==JarvisWatchScreenService.SpotifyStep.SCROLL_LIBRARY_PLAYLISTS?
                1100L:step==JarvisWatchScreenService.SpotifyStep.SET_QUERY?
                1100L:step==JarvisWatchScreenService.SpotifyStep.SELECT_RESULT?
                1200L:650L;
            MAIN.postDelayed(()->run(next),delay);
        }
    }
}
