package com.andriy.jarvis;

import java.util.ArrayList;
import java.util.List;
import java.util.function.ToIntFunction;

/** One physical Spotify search-result list, containing both typed and
 * untyped cards. A later typed song must not hide an earlier playlist.
 * Pure Java: regression-tested without an Android emulator. */
public final class SpotifyVisibleResultOrder {
    private SpotifyVisibleResultOrder() {}

    public static <T> ArrayList<T> combine(List<T> typed,List<T> untyped,
            ToIntFunction<T> centerY,ToIntFunction<T> topY,int height) {
        ArrayList<T> rows=new ArrayList<>(typed);
        for(T candidate:untyped) {
            boolean same=false;
            for(T known:typed) {
                if(SpotifyTrackSearchRules.sameVisibleResultRow(
                    centerY.applyAsInt(known),centerY.applyAsInt(candidate),height)) {
                    same=true;break; // keep typed info, not duplicate title
                }
            }
            if(!same)rows.add(candidate);
        }
        rows.sort((a,b)->Integer.compare(topY.applyAsInt(a),topY.applyAsInt(b)));
        return rows;
    }
}
