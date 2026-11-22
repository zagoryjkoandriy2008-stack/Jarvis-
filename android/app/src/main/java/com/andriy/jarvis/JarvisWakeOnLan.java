package com.andriy.jarvis;

import android.content.Context;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.os.Handler;
import android.os.Looper;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** Wake-on-LAN only on the paired user's own Wi-Fi. Never open inbound ports. */
public final class JarvisWakeOnLan {
    private static final String PREFS="jarvis_wol_lan";
    private static final String KEY_MAC="mac";
    private static final String KEY_BROADCAST="broadcast";
    private static final ExecutorService IO=Executors.newSingleThreadExecutor();
    private static final Handler MAIN=new Handler(Looper.getMainLooper());
    public interface Result {void done(boolean sent,String detail);}
    private JarvisWakeOnLan(){}
    public static boolean isWakeCommand(String raw) {
        return WakeOnLanPacket.isWakeCommand(raw);
    }
    public static void configure(Context context,String mac,String broadcast) {
        WakeOnLanPacket.parseMac(mac);
        WakeOnLanPacket.parseLocalBroadcast(broadcast);
        context.getSharedPreferences(PREFS,Context.MODE_PRIVATE).edit()
            .putString(KEY_MAC,mac).putString(KEY_BROADCAST,broadcast).apply();
    }
    public static String savedMac(Context context) {
        return context.getSharedPreferences(PREFS,Context.MODE_PRIVATE).getString(KEY_MAC,"");
    }
    public static String savedBroadcast(Context context) {
        return context.getSharedPreferences(PREFS,Context.MODE_PRIVATE)
            .getString(KEY_BROADCAST,"192.168.1.255");
    }
    public static void send(Context context,Result completion) {
        if(completion==null)return;
        Context app=context.getApplicationContext();
        if(app.getPackageManager().hasSystemFeature(android.content.pm.PackageManager.FEATURE_WATCH)) {
            completion.done(false,"Wake-on-LAN відправляє Pixel через домашню Wi-Fi мережу.");return;
        }
        String mac=savedMac(app),broadcast=savedBroadcast(app);
        if(mac.isEmpty()) {
            completion.done(false,"Спочатку налаштуй MAC комп'ютера у розділі Wake-on-LAN на Pixel.");return;
        }
        IO.execute(()->{
            try{
                ConnectivityManager conn=(ConnectivityManager)app.getSystemService(Context.CONNECTIVITY_SERVICE);
                Network network=conn==null?null:conn.getActiveNetwork();
                NetworkCapabilities caps=network==null?null:conn.getNetworkCapabilities(network);
                if(caps==null || !caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI))
                    throw new IllegalStateException(
                        "Pixel не у домашньому Wi-Fi. Для мобільного інтернету потрібен окремий домашній WoL шлюз.");
                byte[] bytes=WakeOnLanPacket.build(mac);
                InetAddress destination=InetAddress.getByAddress(
                    WakeOnLanPacket.parseLocalBroadcast(broadcast));
                try(DatagramSocket socket=new DatagramSocket()) {
                    socket.setBroadcast(true);
                    socket.send(new DatagramPacket(bytes,bytes.length,destination,9));
                }
                MAIN.post(()->completion.done(true,
                    "⚡ Magic Packet надіслано в домашню мережу. Увімкнення ПК "+
                    "ще НЕ підтверджене; перевір його статус у JARVIS."));
            }catch(Exception failure){
                MAIN.post(()->completion.done(false,
                    "Wake-on-LAN не виконано: "+failure.getMessage()));
            }
        });
    }
}
