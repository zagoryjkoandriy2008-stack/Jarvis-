package com.andriy.jarvis;

/** Translate a tap on a FIT_CENTER ImageView into normalized Windows primary
 * monitor coordinates. Black letterbox margins must never generate clicks. */
public final class JarvisRemoteTouchMapper {
    public static final class Point {
        public final double x,y;
        private Point(double x,double y){this.x=x;this.y=y;}
    }
    private JarvisRemoteTouchMapper(){}
    public static Point map(float touchX,float touchY,int viewWidth,
                            int viewHeight,int imageWidth,int imageHeight) {
        if(viewWidth<1||viewHeight<1||imageWidth<1||imageHeight<1 ||
           !Float.isFinite(touchX)||!Float.isFinite(touchY))
            return null;
        double scale=Math.min(viewWidth/(double)imageWidth,
                              viewHeight/(double)imageHeight);
        double contentW=imageWidth*scale,contentH=imageHeight*scale;
        double left=(viewWidth-contentW)/2.0;
        double top=(viewHeight-contentH)/2.0;
        if(touchX<left || touchY<top ||
           touchX>left+contentW || touchY>top+contentH)
            return null;
        return new Point(
            Math.max(0,Math.min(1,(touchX-left)/contentW)),
            Math.max(0,Math.min(1,(touchY-top)/contentH)));
    }
}
