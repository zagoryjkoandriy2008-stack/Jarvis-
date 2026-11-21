package com.andriy.jarvis;

import java.util.Arrays;
import java.util.Collections;
import java.util.List;

public final class SpotifyVisibleResultOrderTest {
    private static final class Result {
        final String title;final int y;final boolean typed;
        Result(String title,int y,boolean typed){
            this.title=title;this.y=y;this.typed=typed;
        }
    }
    private static void ok(boolean flag,String message){
        if(!flag)throw new AssertionError("Spotify mixed result order: "+message);
    }
    public static void main(String[] args) {
        Result firstPlaylist=new Result("Сексуальный плейлист",319,false);
        Result laterSong=new Result("Романтичная",690,true);
        List<Result> rows=SpotifyVisibleResultOrder.combine(
            Arrays.asList(laterSong),Arrays.asList(firstPlaylist),
            r->r.y,r->r.y,1536);
        ok(rows.size()==2 && rows.get(0)==firstPlaylist && rows.get(1)==laterSong,
            "first untyped playlist must precede later typed song");
        Result typedPlaylist=new Result("Сексуальный плейлист",340,true);
        rows=SpotifyVisibleResultOrder.combine(
            Arrays.asList(typedPlaylist,laterSong),
            Arrays.asList(firstPlaylist),r->r.y,r->r.y,1536);
        ok(rows.size()==2 && rows.get(0)==typedPlaylist && rows.get(1)==laterSong,
            "typed metadata wins only for duplicate of the same row");
        rows=SpotifyVisibleResultOrder.combine(
            Collections.emptyList(),Arrays.asList(firstPlaylist,laterSong),
            r->r.y,r->r.y,1536);
        ok(rows.size()==2 && rows.get(0)==firstPlaylist,
            "fully untyped Spotify screen retains first result");
        rows=SpotifyVisibleResultOrder.combine(
            Arrays.asList(laterSong),Collections.emptyList(),
            r->r.y,r->r.y,1536);
        ok(rows.size()==1 && rows.get(0)==laterSong,
            "fully typed Spotify screen still works");
        System.out.println("PASS SpotifyVisibleResultOrderTest");
    }
}
