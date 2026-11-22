package com.andriy.jarvis;

/** Standalone no-Android unit test for FIT_CENTER screen touch mapping. */
public final class JarvisRemoteTouchMapperTest {
    private static void check(boolean ok,String reason) {
        if(!ok)throw new AssertionError("Windows remote touch mapping: "+reason);
    }
    private static void equalsNear(double expected,double got,String reason) {
        check(Math.abs(expected-got)<0.0001,reason+": "+got);
    }
    public static void main(String[] args) {
        // PC 16:9 screenshot on a tall 9:16 Pixel view has black
        // letterbox margins above/below the actual computer image.
        JarvisRemoteTouchMapper.Point mid=JarvisRemoteTouchMapper.map(
            450,800,900,1600,1920,1080);
        check(mid!=null,"picture center");
        equalsNear(.5,mid.x,"horizontal image center");
        equalsNear(.5,mid.y,"vertical image center");
        check(JarvisRemoteTouchMapper.map(450,50,900,1600,1920,1080)==null,
            "black margin above the screen must never click Windows");
        check(JarvisRemoteTouchMapper.map(450,1550,900,1600,1920,1080)==null,
            "black margin below image must never click Windows");
        JarvisRemoteTouchMapper.Point origin=JarvisRemoteTouchMapper.map(
            0,546.875f,900,1600,1920,1080);
        check(origin!=null,"top left screen point");
        equalsNear(0,origin.x,"x left");
        equalsNear(0,origin.y,"y top");
        JarvisRemoteTouchMapper.Point right=JarvisRemoteTouchMapper.map(
            900,1053.125f,900,1600,1920,1080);
        check(right!=null,"bottom right screen point");
        equalsNear(1,right.x,"x right");
        equalsNear(1,right.y,"y bottom");
        // Wide Pixel: black margins left and right of tall PC screenshot.
        check(JarvisRemoteTouchMapper.map(5,500,1600,900,900,1600)==null,
            "black horizontal letterbox rejects touch");
        check(JarvisRemoteTouchMapper.map(
            Float.NaN,0,900,1600,1920,1080)==null,"NaN rejected");
        check(JarvisRemoteTouchMapper.map(0,0,0,1600,1920,1080)==null,
            "no view dimensions rejected");
        System.out.println("PASS JarvisRemoteTouchMapperTest");
    }
}
