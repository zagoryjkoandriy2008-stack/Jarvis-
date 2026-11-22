package com.andriy.jarvis;

import android.content.Context;
import android.os.Handler;
import android.os.Looper;
import org.json.JSONObject;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** Explicit opt-in: signed P-256 commands to Windows, delegated Watch→Pixel.
 * Never forward unrestricted voice input; the strict parser maps to types.
 * No fallback to shared-token v1 if secure mode is selected.
 */
public final class JarvisSignedDeviceCommands {
    private static final ExecutorService IO=Executors.newCachedThreadPool();
    private static final Handler MAIN=new Handler(Looper.getMainLooper());
    private static final String PREFS="jarvis_device_relay_v1";
    private static final String FLAG="signed_v2_enabled";
    private static final java.util.concurrent.atomic.AtomicLong LAST_HEARTBEAT=
        new java.util.concurrent.atomic.AtomicLong(0L);
    private JarvisSignedDeviceCommands(){}
    public static boolean enabled(Context c){
        return c.getSharedPreferences(PREFS,Context.MODE_PRIVATE).getBoolean(FLAG,false);
    }
    public static void setEnabled(Context c,boolean enable){
        c.getSharedPreferences(PREFS,Context.MODE_PRIVATE).edit()
            .putBoolean(FLAG,enable).apply();
    }
    /** At most once per 25 seconds; never runs without owner opt-in. */
    public static void heartbeat(Context context) {
        if(!enabled(context)||!JarvisDeviceRelay.enabled(context))return;
        long now=android.os.SystemClock.elapsedRealtime();
        long last=LAST_HEARTBEAT.get();
        if(last!=0L && now-last<25000L)return;
        if(!LAST_HEARTBEAT.compareAndSet(last,now))return;
        Context app=context.getApplicationContext();
        IO.execute(()->{
            try{
                JSONObject payload=new JSONObject();
                payload.put("device","phone");
                signed(new AiChatClient(app),"/device/v2/commands/heartbeat",payload);
            }catch(Exception ignored){
                // Offline status is expected. Do not log key material or retry rapidly.
            }
        });
    }
    public static void send(Context context,String origin,String spoken,
                            JarvisDeviceRelay.Callback callback){
        if(callback==null)return;
        if(!enabled(context)){
            callback.finished(false,"Підписаний Relay v2 не увімкнено.");return;
        }
        if(!"phone".equals(origin)&&!"watch".equals(origin)){
            callback.finished(false,"Недопустиме джерело команди.");return;
        }
        final JarvisSecureCommandMap.Action action=
            JarvisSecureCommandMap.fromVoice(spoken);
        if(action==null){
            callback.finished(false,"Ця команда не дозволена у захищеному режимі Windows. "+
                "Довільні дії, паролі, макроси й фінанси не передаю.");return;
        }
        Context app=context.getApplicationContext();
        IO.execute(()->{
            try {
                AiChatClient api=new AiChatClient(app);
                long now=System.currentTimeMillis();
                String requestId=UUID.randomUUID().toString();
                JSONObject payload=new JSONObject();
                if("volume.set".equals(action.kind))
                    payload.put("value",action.volume);
                if("application.open".equals(action.kind))
                    payload.put("name",action.app);
                JSONObject body=new JSONObject();
                body.put("version",2);body.put("requestId",requestId);
                body.put("origin",origin);body.put("target","computer");
                body.put("kind",action.kind);body.put("payload",payload);
                body.put("createdAt",now);body.put("expiresAt",now+60000L);
                body.put("requiresApproval",action.approval);body.put("replyTo",origin);
                JSONObject accepted=signed(api,"/device/v2/commands/send",body);
                if(!requestId.equals(accepted.optString("requestId")) ||
                   !"accepted".equals(accepted.optString("status")))
                    throw new IllegalStateException("Worker не підтвердив ідентифікатор команди.");
                JSONObject query=new JSONObject();query.put("requestId",requestId);
                for(int i=0;i<24;i++){
                    Thread.sleep(1250L);
                    JSONObject state=signed(api,"/device/v2/commands/result",query);
                    String phase=state.optString("status","unknown");
                    if("completed".equals(phase)||"failed".equals(phase)||
                       "unknown".equals(phase)){
                        final boolean success="completed".equals(phase);
                        final String status=phase;
                        final String detail=state.optString("detail","");
                        MAIN.post(()->callback.finished(success,
                            success?"✅ Windows: "+(detail.isEmpty()?"виконано":detail):
                            "⚠ Windows: "+("unknown".equals(status)?
                                "результат невідомий; не повторюю команду автоматично.":
                                (detail.isEmpty()?"не вдалося виконати.":detail))));
                        return;
                    }
                }
                MAIN.post(()->callback.finished(false,
                    "Результат команди Windows невідомий після 30 с. "+
                    "Повторну дію автоматично не запускаю."));
            }catch(InterruptedException e){
                Thread.currentThread().interrupt();
                MAIN.post(()->callback.finished(false,"Команду перервано, результат невідомий."));
            }catch(Exception error){
                MAIN.post(()->callback.finished(false,
                    "Підписаний канал Windows недоступний або доступ відкликаний. "+
                    "Перевір pairing і налаштування v2 на Windows та Pixel."));
            }
        });
    }
    private static JSONObject signed(AiChatClient ai,String route,JSONObject body)
        throws Exception {
        final String raw=body.toString();
        java.util.Map<String,String> headers=JarvisDeviceIdentity.signedHeaders(
            route,new JarvisDeviceIdentity.JSONObjectWrapper(raw));
        return ai.deviceRelayRequest(route,"POST",body,headers,raw);
    }
}
