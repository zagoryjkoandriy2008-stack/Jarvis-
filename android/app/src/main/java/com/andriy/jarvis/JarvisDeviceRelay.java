package com.andriy.jarvis;

import android.content.Context;
import android.content.pm.PackageManager;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import com.google.android.gms.wearable.Node;
import com.google.android.gms.wearable.Wearable;
import org.json.JSONArray;
import org.json.JSONObject;
import java.nio.charset.StandardCharsets;
import java.util.HashSet;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

/** Private user-opt-in relay: Windows -> Pixel -> Watch7 (Data Layer).
 * AI-generated text is NEVER accepted as device-control instructions.
 * Finance writes execute ONLY on the phone's original SQLCipher journal.
 * Incoming commands are checked against a narrow, local allowlist.
 */
public final class JarvisDeviceRelay {
    public interface Callback {void finished(boolean ok,String detail);}
    private static final String PREFS="jarvis_device_relay_v1";
    private static final String ENABLED="enabled";
    private static final String TAG="DIMON_DEVICE_RELAY";
    private static final String WATCH_REQUEST="/jarvis/device-bridge/v1/watch-request";
    private static final String WATCH_REPLY="/jarvis/device-bridge/v1/watch-reply";
    private static final Handler MAIN=new Handler(Looper.getMainLooper());
    private static final ExecutorService IO=Executors.newSingleThreadExecutor();
    private static final ExecutorService OUTBOUND=Executors.newCachedThreadPool();
    private static ScheduledExecutorService periodic;
    private static final Set<String> pending=new HashSet<>();
    private JarvisDeviceRelay(){}

    public static boolean enabled(Context context) {
        return context.getSharedPreferences(PREFS,Context.MODE_PRIVATE)
            .getBoolean(ENABLED,false);
    }
    public static void setEnabled(Context context,boolean value) {
        context.getSharedPreferences(PREFS,Context.MODE_PRIVATE)
            .edit().putBoolean(ENABLED,value).apply();
        if(!value)stop();
    }
    public static String watchRequestPath(){return WATCH_REQUEST;}
    public static String watchReplyPath(){return WATCH_REPLY;}

    public static void send(Context context,String origin,String target,
                            String command,Callback done) {
        if(done==null)return;
        if(!enabled(context)) {
            done.finished(false,"Увімкни зв'язок пристроїв у JARVIS на Pixel.");
            return;
        }
        if(!("phone".equals(origin)||"watch".equals(origin)) ||
           !("computer".equals(target)||"watch".equals(target)||
             "phone".equals(target)) || origin.equals(target) ||
           command==null || command.isEmpty() || command.length()>250 ||
           command.indexOf('\n')>=0 || command.indexOf('\r')>=0 ||
           command.indexOf('\0')>=0) {
            done.finished(false,"Неправильний дистанційний запит.");return;
        }
        Context app=context.getApplicationContext();
        if("computer".equals(target) && JarvisSignedDeviceCommands.enabled(app)) {
            // Owner explicitly selected P-256 mode. Never fall back to bearer-only v1.
            JarvisSignedDeviceCommands.send(app,origin,command,done);
            return;
        }
        OUTBOUND.execute(()->{
            try {
                AiChatClient ai=new AiChatClient(app);
                String id=UUID.randomUUID().toString();
                JSONObject body=new JSONObject();
                body.put("id",id);body.put("origin",origin);
                body.put("target",target);body.put("command",command);
                ai.deviceRelayRequest("/device/v1/send","POST",body);
                for(int n=0;n<24;n++) {
                    Thread.sleep(1250L);
                    JSONObject reply=ai.deviceRelayRequest(
                        "/device/v1/result?id="+id+"&origin="+origin,
                        "GET",null);
                    if(reply.optBoolean("done",false)) {
                        String detail=reply.optString("detail","").trim();
                        boolean ok=reply.optBoolean("ok",false);
                        MAIN.post(()->done.finished(ok,detail));return;
                    }
                }
                MAIN.post(()->done.finished(false,
                    "Пристрій не підтвердив виконання за 30 с."));
            }catch(Exception error) {
                Log.w(TAG,"Could not send authenticated device command",error);
                MAIN.post(()->done.finished(false,
                    "Не вдалося передати команду. Перевір Cloudflare Relay і зв'язок."));
            }
        });
    }

    public static synchronized void startPhone(Context context) {
        if(context.getPackageManager().hasSystemFeature(PackageManager.FEATURE_WATCH) ||
           !enabled(context) || periodic!=null)return;
        Context app=context.getApplicationContext();
        periodic=Executors.newSingleThreadScheduledExecutor();
        periodic.scheduleWithFixedDelay(()->{
            if(!enabled(app))return;
            try {
                if(JarvisSignedDeviceCommands.enabled(app))
                    JarvisSignedDeviceCommands.heartbeat(app);
                poll(app,"phone",null);
                // A watch is online only when paired; do not advertise it
                // while disconnected or the companion has no Wear link.
                Wearable.getNodeClient(app).getConnectedNodes().addOnSuccessListener(
                    nodes->{
                        if(nodes==null || nodes.isEmpty())return;
                        // Wear Data Layer can connect over Wi-Fi as well as
                        // Bluetooth. Prefer a nearby paired watch, but don't
                        // silently disable the bridge on valid cloud/Wi-Fi
                        // Wear links that report isNearby()==false.
                        Node chosen=nodes.get(0);
                        for(Node node:nodes)
                            if(node.isNearby()){chosen=node;break;}
                        String recipient=chosen.getId();
                        IO.execute(()->poll(app,"watch",recipient));
                    });
            }catch(RuntimeException failure) {
                Log.w(TAG,"Device bridge poll interrupted",failure);
            }
        },500L,5500L,TimeUnit.MILLISECONDS);
    }

    public static synchronized void stop() {
        if(periodic!=null) {
            periodic.shutdownNow();periodic=null;
        }
    }

    private static void poll(Context app,String device,String watchNode) {
        if(!enabled(app))return;
        try {
            AiChatClient ai=new AiChatClient(app);
            JSONObject result=ai.deviceRelayRequest(
                "/device/v1/poll?device="+device,"GET",null);
            JSONArray items=result.optJSONArray("items");
            if(items==null)return;
            for(int i=0;i<items.length();i++) {
                JSONObject item=items.optJSONObject(i);
                if(item==null)continue;
                String id=item.optString("id","");
                String origin=item.optString("origin","");
                String command=item.optString("command","");
                if(!JarvisWearProtocol.validId(id) ||
                   command.length()>250 || command.isEmpty())continue;
                if(!claim(app,id)) {
                    String receipt=app.getSharedPreferences(PREFS,0)
                        .getString("result:"+id,null);
                    if(receipt!=null && receipt.length()>=2 &&
                       (receipt.startsWith("1:")||receipt.startsWith("0:")))
                        sendAck(app,id,device,receipt.startsWith("1:"),
                            receipt.substring(2));
                    continue;
                }
                if("phone".equals(device)) {
                    if(!"computer".equals(origin) &&
                       !"watch".equals(origin)) {
                        complete(app,id,"phone",false,"Невідоме джерело.");continue;
                    }
                    if(!JarvisDeviceRelayRules.permittedRemotePhone(command)) {
                        complete(app,id,"phone",false,
                            "Ця команда не дозволена дистанційно на Pixel.");continue;
                    }
                    // The established Wear dispatcher already owns Finance
                    // account deduplication, lock checks and local Spotify.
                    MAIN.post(()->JarvisWearPhoneAssistant.execute(app,
                        "device-relay:"+origin,command,(ok,detail)->
                        complete(app,id,"phone",ok,detail)));
                } else if("watch".equals(device) && watchNode!=null) {
                    if(!"computer".equals(origin)) {
                        complete(app,id,"watch",false,
                            "Watch7 приймає тільки запити з комп'ютера.");continue;
                    }
                    if(!JarvisDeviceRelayRules.permittedRemoteWatch(command)) {
                        complete(app,id,"watch",false,
                            "Дистанційну команду Watch7 не дозволено.");continue;
                    }
                    JSONObject request=new JSONObject();
                    request.put("id",id);
                    request.put("command",command);
                    byte[] encoded=request.toString().getBytes(StandardCharsets.UTF_8);
                    if(encoded.length>JarvisWearProtocol.MAX_BYTES) {
                        complete(app,id,"watch",false,"Дані завеликі.");continue;
                    }
                    Wearable.getMessageClient(app).sendMessage(
                        watchNode,WATCH_REQUEST,encoded).addOnFailureListener(
                        ex->complete(app,id,"watch",false,
                            "Не вдалося доставити команду на Watch7."));
                    // Do not ACK until the actual watch sends a result.
                }
            }
        }catch(Exception error){Log.w(TAG,"Phone bridge poll failed",error);}
    }

    /** Claim before side effects; server leases can be retried, but a crash
     * must not cause a finance entry or any other action to be executed twice. */
    private static synchronized boolean claim(Context app,String id) {
        if(pending.contains(id))return false;
        android.content.SharedPreferences prefs=app.getSharedPreferences(PREFS,0);
        long known=prefs.getLong("claimed:"+id,0);
        if(known>0 && System.currentTimeMillis()-known<5*60*1000L)
            return false;
        pending.add(id);
        prefs.edit().putLong("claimed:"+id,System.currentTimeMillis()).commit();
        return true;
    }

    public static void watchReply(Context phone,String id,boolean ok,String detail) {
        if(!JarvisWearProtocol.validId(id))return;
        complete(phone.getApplicationContext(),id,"watch",ok,detail);
    }

    private static void complete(Context app,String id,String device,
                                 boolean ok,String detail) {
        if(!JarvisWearProtocol.validId(id))return;
        // Never silently return a truncated balance or note list as though
        // it were complete; show an explicit on-phone follow-up instruction.
        final String safeDetail=detail==null?"":(
            detail.length()<=4500?detail:
            detail.substring(0,4350)+"\n… Відповідь скорочено. "+
            "Повний звіт або нотатки відкрий у JARVIS на Pixel.");
        // Persist ACK before network I/O. If the Worker lease expires before
        // the ACK, resend the same result, NEVER re-execute the action.
        app.getSharedPreferences(PREFS,0).edit().putString("result:"+id,
            (ok?"1:":"0:")+safeDetail).apply();
        synchronized(JarvisDeviceRelay.class){pending.remove(id);}
        sendAck(app,id,device,ok,safeDetail);
    }

    private static void sendAck(Context app,String id,String device,
                                boolean ok,String detail) {
        OUTBOUND.execute(()->{
            try {
                JSONObject reply=new JSONObject();
                reply.put("id",id);reply.put("device",device);
                reply.put("ok",ok);
                reply.put("detail",detail);
                AiChatClient ai=new AiChatClient(app);
                ai.deviceRelayRequest("/device/v1/ack","POST",reply);
            }catch(Exception error) {
                Log.w(TAG,"Relay ACK not delivered; command will not be replayed",error);
            }
        });
    }
}
