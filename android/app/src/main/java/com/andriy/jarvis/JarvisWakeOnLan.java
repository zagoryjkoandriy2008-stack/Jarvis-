package com.andriy.jarvis;

import android.content.Context;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.net.LinkProperties;
import android.net.LinkAddress;
import java.net.Inet4Address;
import android.os.Handler;
import android.os.Looper;
import org.json.JSONObject;
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
    public interface Progress {void update(String detail);}
    private static final ExecutorService VERIFY=Executors.newSingleThreadExecutor();
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
    /** Sends the Magic Packet, then waits for a *new signed* Windows heartbeat.
     * If the PC never confirms, signal delivery is still reported accurately.
     * On Watch7, the final callback is sent once, never as an early ACK.
     */
    public static void sendAndConfirm(Context context,Progress progress,Result completion) {
        if(completion==null)return;
        final Context app=context.getApplicationContext();
        final long signalStarted=System.currentTimeMillis();
        send(app,(sent,detail)->{
            if(!sent){completion.done(false,detail);return;}
            if(progress!=null)progress.update(detail+" Перевіряю зв'язок Windows…");
            if(!JarvisSignedDeviceCommands.enabled(app) ||
               !JarvisDeviceRelay.enabled(app)) {
                completion.done(true,detail+" Підписаний Relay v2 не активний: "+
                    "підтвердити увімкнення ПК неможливо.");
                return;
            }
            VERIFY.execute(()->{
                boolean confirmed=false;
                final long deadline=android.os.SystemClock.elapsedRealtime()+20000L;
                try {
                    AiChatClient client=new AiChatClient(app);
                    while(android.os.SystemClock.elapsedRealtime()<deadline){
                        JSONObject query=new JSONObject();
                        query.put("device","computer");
                        String raw=query.toString();
                        JSONObject status=client.deviceRelayRequest(
                            "/device/v2/commands/status","POST",query,
                            JarvisDeviceIdentity.signedHeaders(
                                "/device/v2/commands/status",
                                new JarvisDeviceIdentity.JSONObjectWrapper(raw)),raw);
                        if(WakeStatusPolicy.confirmedOnlineAfterSignal(
                             status.optBoolean("online",false),
                             status.optLong("lastSeenMs",0),signalStarted,
                             System.currentTimeMillis())) {
                            confirmed=true;break;
                        }
                        if(android.os.SystemClock.elapsedRealtime()+2500L>=deadline)break;
                        Thread.sleep(2500L);
                    }
                }catch(InterruptedException interrupted){
                    Thread.currentThread().interrupt();
                }catch(Exception ignored){
                    // Offline/missing pairing is not proof the computer woke up.
                }
                final boolean online=confirmed;
                MAIN.post(()->completion.done(true,online?
                    "✅ Windows доступний: отримано новий підписаний heartbeat "+
                    "після сигналу Wake-on-LAN.":
                    "⚡ Magic Packet надіслано, але новий підписаний heartbeat "+
                    "Windows не отримано. Увімкнення ПК не підтверджене."));
            });
        });
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
                byte[] destinationBytes=WakeOnLanPacket.parseLocalBroadcast(broadcast);
                LinkProperties networkInfo=conn.getLinkProperties(network);
                boolean matched=false;
                if(networkInfo!=null)for(LinkAddress local:networkInfo.getLinkAddresses()){
                    if(local.getAddress() instanceof Inet4Address &&
                       WakeOnLanPacket.matchesSubnet(local.getAddress().getAddress(),
                         local.getPrefixLength(),destinationBytes)){
                        matched=true;break;
                    }
                }
                if(!matched)throw new IllegalStateException(
                    "Поточний Wi-Fi Pixel не відповідає налаштованій локальній підмережі ПК.");
                byte[] bytes=WakeOnLanPacket.build(mac);
                InetAddress destination=InetAddress.getByAddress(destinationBytes);
                try(DatagramSocket socket=new DatagramSocket()) {
                    network.bindSocket(socket);
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
