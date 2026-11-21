# Spotify Pixel: first visible result — regression audit after 0.1.46

**Status:** source-code change only. A new APK has NOT been assembled or installed.
No claim of verified audio playback on a real Pixel is made.

## Diagnosed previous failure paths

1. Result-click gate required an accessibility-visible query header or editable field.
   Spotify Compose can hide both after Search; the controller kept returning WAIT,
   so the first result was never tapped.
2. The gate treated getCurrentInputStarted() as proof that an IME keyboard was visible.
   Android can keep the input connection alive after the keyboard is dismissed.
   Now only an active system TYPE_INPUT_METHOD **window** blocks a touch.
3. Previous mixed-result logic returned the first TYPED song whenever any type
   labels were visible, skipping an earlier untyped playlist. The new pure
   SpotifyVisibleResultOrder merges typed and untyped rows by physical Y.
4. The IME submit step previously ignored performEditorAction/sendKeyEvent
   rejection and still reported CLICKED.

## Steps to test on Pixel

- [ ] Say «Увімкни пісню романтична». Spotify opens, taps Search, focuses field, types and confirms.
- [ ] The FIRST result may be «Сексуальный плейлист», before «Романтичная» by Syava.
      The first visible result must be pressed, whether tagged or untagged.
- [ ] On a mixed page, select the topmost actual card, not a filter chip or the mini-player.
- [ ] If the first result opens a playlist detail, press its own Play and verify playback.
- [ ] If a track opens, confirm the track title in the mini-player.
- [ ] With the keyboard still visible, do NOT tap the results.
- [ ] After Enter hides the keyboard, do NOT remain indefinitely in WAIT just
      because Spotify removed the query text from its accessibility hierarchy.
- [ ] Repeat «Не та пісня» to advance to the next genuine result.
- [ ] Confirm Spotify search and named playlists still work; check Bolt, JARVIS Finance,
      Google Maps Watch7 and Spotify Watch7 down-only navigation for regressions.

## Journal diagnostics

New entries called `SPOTIFY_RESULT` contain **numeric stage gates only**, no personal titles:

| Code | Meaning |
| --- | --- |
| 1 | Spotify accessibility root missing |
| 2 | System IME keyboard window still active |
| 3 | Different query visible in Spotify search field |
| 4 | Query header hidden and no verified recent submission |
| 5 | Search result selection authorized |
| 6 | Typed playlist result targeted |
| 7 | Other recognized result targeted, including an untyped first row |
| 8 | Android confirms row gesture COMPLETED (not proof of Spotify playback) |
| 9 | Row gesture cancelled |
| 10 | Selected result index not visible in current viewport |
| 11 | Calibrated fallback rejected by orientation/timing constraints |
| 12 | Last-resort calibrated result tap dispatched |
| 13 | Calibrated tap gesture completed (not proof of playback) |
| 14 | Calibrated tap cancelled |

`SPOTIFY_STAGE` tracks controller stage; `LOCAL_DONE` / `LOCAL_ERROR`
distinguish verified completion from failure. Do not infer music from code 8/13.

## Review after the initial fix

- [ ] Compile all Android sources (not an APK) and run all Java policy tests.
- [ ] Re-run mixed typed/untyped ordering unit test with an untyped playlist
      **above** a typed song.
- [ ] Re-run IME rejection and hidden-query safety cases.
- [ ] If no further source-level failure is found, STOP changes and wait for
      real Pixel evidence. Hardware playback remains unverified.
