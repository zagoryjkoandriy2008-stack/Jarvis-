package com.andriy.jarvis;

import android.app.Activity;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.Gravity;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

import org.json.JSONObject;

import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** View-only Windows desktop inside Pixel JARVIS.
 * It cannot send clicks, keyboard input, mouse movement or files.
 * A fresh viewer session must be explicitly opened by the owner, and
 * closes automatically when the Activity loses foreground visibility.
 * Frames stay in ImageView memory; never saved to Gallery or disk.
 */
public final class JarvisWindowsScreenActivity extends Activity {
    private static final int BG=Color.rgb(9,20,37);
    private static final int CYAN=Color.rgb(83,215,234);
    private final Handler ui=new Handler(Looper.getMainLooper());
    private final ExecutorService stream=Executors.newSingleThreadExecutor();
    private final ExecutorService stopTransport=Executors.newSingleThreadExecutor();
    private ImageView screen;
    /** Main-thread only, bounded: never retain every JPEG frame in memory. */
    private Bitmap displayedBitmap;
    private TextView status;
    private volatile boolean visible;
    private volatile String activeSession;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        if(getPackageManager().hasSystemFeature(
            android.content.pm.PackageManager.FEATURE_WATCH)) {
            finish();
            return;
        }
        getWindow().setStatusBarColor(BG);
        getWindow().setNavigationBarColor(BG);
        // Prevent screen thumbnails/sensitive content from appearing in the
        // Android recent-apps preview or inadvertent Pixel screenshots.
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
        FrameLayout outer=new FrameLayout(this);
        outer.setBackgroundColor(BG);
        screen=new ImageView(this);
        screen.setScaleType(ImageView.ScaleType.FIT_CENTER);
        screen.setBackgroundColor(Color.BLACK);
        outer.addView(screen,new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT,
            ViewGroup.LayoutParams.MATCH_PARENT));

        LinearLayout top=new LinearLayout(this);
        top.setOrientation(LinearLayout.VERTICAL);
        top.setPadding(dp(14),dp(10),dp(14),dp(12));
        top.setBackgroundColor(0xeF0B1729);
        TextView title=new TextView(this);
        title.setText("🖥 Екран комп'ютера · JARVIS");
        title.setTextColor(CYAN);
        title.setTextSize(18);
        title.setGravity(Gravity.CENTER_VERTICAL);
        top.addView(title);
        status=new TextView(this);
        status.setText("Підключення до Windows…");
        status.setTextColor(Color.WHITE);
        status.setTextSize(13);
        status.setPadding(0,dp(6),0,0);
        top.addView(status);
        FrameLayout.LayoutParams topParams=new FrameLayout.LayoutParams(
            -1,ViewGroup.LayoutParams.WRAP_CONTENT,Gravity.TOP);
        outer.addView(top,topParams);
        Button stop=new Button(this);
        stop.setText("■ Завершити перегляд");
        stop.setTextColor(Color.WHITE);
        stop.setBackgroundTintList(android.content.res.ColorStateList.valueOf(
            Color.rgb(176,54,67)));
        stop.setOnClickListener(v->finish());
        FrameLayout.LayoutParams stopParams=new FrameLayout.LayoutParams(
            -1,dp(60),Gravity.BOTTOM);
        stopParams.setMargins(dp(25),0,dp(25),dp(12));
        outer.addView(stop,stopParams);
        setContentView(outer);
    }

    @Override protected void onResume() {
        super.onResume();
        if(screen==null)return;
        if(visible)return;
        if(!JarvisDeviceRelay.enabled(this)) {
            status.setText("Увімкни 🔗 зв'язок пристроїв у JARVIS на Pixel.");
            return;
        }
        AiChatClient client=new AiChatClient(this);
        if(!client.isConfigured()) {
            status.setText("Налаштуй HTTPS Cloudflare Worker у JARVIS на Pixel.");
            return;
        }
        visible=true;
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        final String session=UUID.randomUUID().toString();
        activeSession=session;
        stream.execute(()->receiveFrames(session));
    }

    private void receiveFrames(String session) {
        AiChatClient client=new AiChatClient(this);
        long nextHeartbeat=0;
        long lastFrame=System.currentTimeMillis();
        boolean displayed=false;
        int errors=0;
        while(visible && session.equals(activeSession)) {
            try {
                long now=System.currentTimeMillis();
                if(now>=nextHeartbeat) {
                    JSONObject request=new JSONObject();
                    request.put("session",session);
                    request.put("device","phone");
                    client.deviceRelayRequest("/device/v1/screen/view",
                        "POST",request);
                    nextHeartbeat=now+2600;
                }
                byte[] jpeg=client.screenFrame(session);
                if(jpeg!=null) {
                    // Check dimensions BEFORE allocating bitmap memory:
                    // JPEG headers are untrusted network input even with
                    // a strict 240 KB compressed-body limit.
                    BitmapFactory.Options options=new BitmapFactory.Options();
                    options.inJustDecodeBounds=true;
                    BitmapFactory.decodeByteArray(jpeg,0,jpeg.length,options);
                    if(options.outWidth<1 || options.outHeight<1 ||
                       options.outWidth>2048 || options.outHeight>2048 ||
                       ((long)options.outWidth*options.outHeight)>3_000_000L)
                        throw new IllegalStateException(
                            "Кадр екрана має неприпустимі розміри.");
                    options.inJustDecodeBounds=false;
                    options.inPreferredConfig=Bitmap.Config.RGB_565;
                    Bitmap bitmap=BitmapFactory.decodeByteArray(
                        jpeg,0,jpeg.length,options);
                    if(bitmap==null)throw new IllegalStateException(
                        "Не вдалося прочитати кадр екрана.");
                    displayed=true;
                    lastFrame=System.currentTimeMillis();
                    Bitmap ready=bitmap;
                    ui.post(()->{
                        if(visible && session.equals(activeSession)) {
                            showBitmap(ready);
                            status.setText("● Windows онлайн · тільки перегляд "+
                                "· зображення оновлюється автоматично");
                        }else ready.recycle();
                    });
                } else if(System.currentTimeMillis()-lastFrame>5000) {
                    // Never leave an old screenshot looking live after the
                    // desktop stops publishing or the phone loses transport.
                    clearFrame(session);
                    showStatus(session,"Немає нових кадрів. Перегляд призупинено. "+
                        "На Windows перевір дозвіл передачі екрана.");
                    lastFrame=System.currentTimeMillis();
                }
                errors=0;
                Thread.sleep(950);
            }catch(InterruptedException cancelled) {
                Thread.currentThread().interrupt();
                break;
            }catch(Exception failure) {
                if(!visible||!session.equals(activeSession))break;
                if(failure instanceof IllegalStateException &&
                   failure.getMessage()!=null &&
                   failure.getMessage().contains("Сеанс перегляду завершився")) {
                    clearFrame(session);
                    showStatus(session,"Windows або Pixel завершив перегляд. "+
                        "Для нового сеансу закрий і відкрий екран знову.");
                    break;
                }
                errors++;
                if(errors>=4) {
                    clearFrame(session);
                    showStatus(session,"Зв'язок з екраном перервано: "+
                        failure.getMessage()+". Закрий і відкрий перегляд повторно.");
                    break;
                }
                showStatus(session,"Повторно підключаюсь до екрана Windows…");
                try {Thread.sleep(1350);}
                catch(InterruptedException cancelled) {
                    Thread.currentThread().interrupt();
                    break;
                }
            }
        }
        if(!displayed && visible && session.equals(activeSession))
            showStatus(session,"Немає зображення. Перевір дозвіл на Windows "+
                "та налаштування приватного Cloudflare Relay.");
    }

    private void showBitmap(Bitmap next) {
        // UI thread only. Recycle the replaced frame after changing ImageView.
        Bitmap previous=displayedBitmap;
        displayedBitmap=next;
        if(screen!=null) {
            if(next==null)screen.setImageDrawable(null);
            else screen.setImageBitmap(next);
        }
        if(previous!=null && previous!=next && !previous.isRecycled())
            previous.recycle();
    }
    private void clearFrame(String session) {
        ui.post(()->{
            if(visible && session.equals(activeSession))
                showBitmap(null);
        });
    }

    private void showStatus(String session,String message) {
        ui.post(()->{
            if(visible && session.equals(activeSession) && status!=null)
                status.setText(message);
        });
    }

    @Override protected void onPause() {
        // Revoke viewer on HOME, lock, switching apps or closing via button.
        visible=false;
        showBitmap(null);
        getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        String finishedSession=activeSession;
        activeSession=null;
        if(finishedSession!=null) {
            stopTransport.execute(()->{
                try {
                    JSONObject body=new JSONObject();
                    body.put("session",finishedSession);
                    new AiChatClient(getApplicationContext()).deviceRelayRequest(
                        "/device/v1/screen/stop","POST",body);
                }catch(Exception ignored) {
                    // Without an explicit stop, Worker expires this session
                    // automatically after at most eight seconds.
                }
            });
        }
        super.onPause();
    }
    @Override protected void onDestroy() {
        showBitmap(null);
        stream.shutdown();
        stopTransport.shutdown();
        super.onDestroy();
    }
    private int dp(int d) {
        return Math.round(getResources().getDisplayMetrics().density*d);
    }
}
