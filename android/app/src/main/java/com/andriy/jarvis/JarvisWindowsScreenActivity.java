package com.andriy.jarvis;

import android.app.Activity;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.HorizontalScrollView;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

import org.json.JSONObject;

import java.util.UUID;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** Owner-authorized Windows desktop viewer and remote input on Pixel.
 * Mouse/keyboard are blocked until a physical Windows-owner approval.
 * No remote process execution, file access, or AI-authored actions.
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
    private LinearLayout controlTools;
    private Button requestControl;
    private EditText remoteText;
    private final ExecutorService remoteInput=Executors.newSingleThreadExecutor();
    private final AtomicInteger queuedInput=new AtomicInteger();
    private volatile boolean controlling;
    private volatile long freshFrameAt;
    private float touchStartX,touchStartY,scrollStartY,scrollEndY;
    private boolean twoFingerScroll;
    private long lastTapAt;
    private JarvisRemoteTouchMapper.Point lastTap;
    private Runnable pendingTap;
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
        screen.setOnTouchListener((v,event)->handleTouch(event));
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
        LinearLayout bottom=new LinearLayout(this);
        bottom.setOrientation(LinearLayout.VERTICAL);
        bottom.setPadding(dp(9),dp(4),dp(9),dp(8));
        bottom.setBackgroundColor(0xee0B1729);
        LinearLayout actions=new LinearLayout(this);
        actions.setOrientation(LinearLayout.HORIZONTAL);
        requestControl=new Button(this);
        requestControl.setText("🖱 Попросити керування");
        requestControl.setOnClickListener(v->requestControl());
        actions.addView(requestControl,new LinearLayout.LayoutParams(0,dp(50),1));
        Button stop=new Button(this);
        stop.setText("■ Завершити");
        stop.setTextColor(Color.WHITE);
        stop.setBackgroundTintList(android.content.res.ColorStateList.valueOf(
            Color.rgb(176,54,67)));
        stop.setOnClickListener(v->finish());
        actions.addView(stop,new LinearLayout.LayoutParams(0,dp(50),1));
        bottom.addView(actions);

        // Keyboard and scroll are shown ONLY after Windows approves this
        // particular screen session on its own local UI.
        controlTools=new LinearLayout(this);
        controlTools.setOrientation(LinearLayout.VERTICAL);
        controlTools.setVisibility(View.GONE);
        LinearLayout line=new LinearLayout(this);
        line.setOrientation(LinearLayout.HORIZONTAL);
        remoteText=new EditText(this);
        remoteText.setSingleLine(true);
        remoteText.setTextColor(Color.WHITE);
        remoteText.setHintTextColor(0xff9DB7C9);
        remoteText.setHint("Введи текст для ПК");
        remoteText.setTextSize(14);
        line.addView(remoteText,new LinearLayout.LayoutParams(0,dp(46),1));
        Button sendText=new Button(this);
        sendText.setText("Написати");
        sendText.setOnClickListener(v->{
            String value=remoteText.getText().toString();
            if(value.isEmpty()||value.length()>160 || hasControlChars(value)) {
                status.setText("Текст має бути від 1 до 160 символів.");
                return;
            }
            try {
                JSONObject data=new JSONObject();
                data.put("text",value);
                submitInput("text",data);
            }catch(Exception error){status.setText("Помилка текстової команди.");}
        });
        line.addView(sendText);
        controlTools.addView(line);
        HorizontalScrollView keys=new HorizontalScrollView(this);
        LinearLayout keyRow=new LinearLayout(this);
        keyRow.setOrientation(LinearLayout.HORIZONTAL);
        for(String[] entry:new String[][] {
            {"↵","ENTER"},{"Esc","ESC"},{"⌫","BACKSPACE"},
            {"Tab","TAB"},{"↑","UP"},{"↓","DOWN"},
            {"←","LEFT"},{"→","RIGHT"},{"⌦","DELETE"},
            {"Ctrl+A","CTRL_A"},{"Ctrl+C","CTRL_C"},
            {"Ctrl+V","CTRL_V"},{"Pg↑","PAGEUP"},{"Pg↓","PAGEDOWN"}
        }) {
            Button key=new Button(this);
            key.setText(entry[0]);
            key.setTextSize(12);
            key.setOnClickListener(v->sendKey(entry[1]));
            keyRow.addView(key,new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT,dp(44)));
        }
        keys.addView(keyRow);
        controlTools.addView(keys);
        LinearLayout scrollButtons=new LinearLayout(this);
        Button up=new Button(this);
        up.setText("Прокрутити ↑");
        up.setOnClickListener(v->sendScroll(3));
        scrollButtons.addView(up,new LinearLayout.LayoutParams(0,dp(43),1));
        Button down=new Button(this);
        down.setText("Прокрутити ↓");
        down.setOnClickListener(v->sendScroll(-3));
        scrollButtons.addView(down,new LinearLayout.LayoutParams(0,dp(43),1));
        controlTools.addView(scrollButtons);
        Button leaveControl=new Button(this);
        leaveControl.setText("■ Завершити керування, залишити перегляд");
        leaveControl.setTextSize(12);
        leaveControl.setOnClickListener(v->stopControl());
        controlTools.addView(leaveControl,new LinearLayout.LayoutParams(
            -1,dp(44)));
        bottom.addView(controlTools);
        FrameLayout.LayoutParams bottomParams=new FrameLayout.LayoutParams(
            -1,ViewGroup.LayoutParams.WRAP_CONTENT,Gravity.BOTTOM);
        outer.addView(bottom,bottomParams);
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
        controlling=false;
        freshFrameAt=0;
        requestControl.setEnabled(true);
        requestControl.setText("🖱 Попросити керування");
        requestControl.setVisibility(View.VISIBLE);
        controlTools.setVisibility(View.GONE);
        lastTap=null;
        lastTapAt=0;
        visible=true;
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        final String session=UUID.randomUUID().toString();
        activeSession=session;
        stream.execute(()->receiveFrames(session));
    }

    private void receiveFrames(String session) {
        AiChatClient client=new AiChatClient(this);
        long nextHeartbeat=0;
        long nextControlCheck=0;
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
                if(now>=nextControlCheck) {
                    JSONObject state=client.deviceRelayRequest(
                        "/device/v1/screen/control/state?session="+session,
                        "GET",null);
                    boolean approved=state.optBoolean("controlApproved",false);
                    boolean denied=state.optBoolean("controlDenied",false);
                    ui.post(()->{
                        if(!visible||!session.equals(activeSession))return;
                        controlling=approved;
                        controlTools.setVisibility(approved?View.VISIBLE:View.GONE);
                        requestControl.setVisibility(
                            approved?View.GONE:View.VISIBLE);
                        if(denied) {
                            requestControl.setEnabled(false);
                            requestControl.setText("Керування відхилено");
                            status.setText("Windows відхилив керування. "+
                                "Закрий перегляд і відкрий заново.");
                        }else if(approved) {
                            status.setText("🖱 Керування дозволено: торкнись "+
                                "для кліку, двічі — подвійний, довго — правий клік.");
                        }
                    });
                    nextControlCheck=now+1650;
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
                    freshFrameAt=lastFrame;
                    Bitmap ready=bitmap;
                    ui.post(()->{
                        if(visible && session.equals(activeSession)) {
                            showBitmap(ready);
                            if(controlling)status.setText(
                                "🖱 Ти керуєш Windows · дотики, клавіші, текст.");
                            else if(requestControl.isEnabled())
                                status.setText("● Windows онлайн · "+
                                    "натисни «Попросити керування» для керування ПК.");
                        }else ready.recycle();
                    });
                } else if(System.currentTimeMillis()-lastFrame>5000) {
                    // Never leave an old screenshot looking live after the
                    // desktop stops publishing or the phone loses transport.
                    freshFrameAt=0;
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

    private static boolean hasControlChars(String value) {
        for(int i=0;i<value.length();i++)
            if(Character.isISOControl(value.charAt(i)))return true;
        return false;
    }
    private void stopControl() {
        String session=activeSession;
        if(!visible||session==null)return;
        controlling=false;
        controlTools.setVisibility(View.GONE);
        status.setText("Дистанційне керування вимкнено на Pixel.");
        remoteInput.execute(()->{
            try {
                new AiChatClient(getApplicationContext()).deviceRelayRequest(
                    "/device/v1/screen/control/disable?session="+session,
                    "POST",new JSONObject());
            }catch(Exception error) {
                showStatus(session,"Керування вимкнено на Pixel. "+
                    "Windows завершить його після втрати зв'язку.");
            }
        });
    }
    private void requestControl() {
        String session=activeSession;
        if(!visible||session==null)return;
        requestControl.setEnabled(false);
        status.setText("Надсилаю запит на Windows. "+
            "На комп'ютері натисни «Дозволити».");
        remoteInput.execute(()->{
            try {
                new AiChatClient(getApplicationContext()).deviceRelayRequest(
                    "/device/v1/screen/control/request?session="+session,
                    "POST",new JSONObject());
                showStatus(session,"Запит на керування передано. "+
                    "Підтверди його на екрані Windows JARVIS.");
            }catch(Exception error){
                showStatus(session,"Windows не прийняв запит. "+
                    "Перевір опцію «Дозволяти запити на керування» на ПК.");
                ui.post(()->{
                    if(visible&&session.equals(activeSession))
                        requestControl.setEnabled(true);
                });
            }
        });
    }

    private void sendKey(String key) {
        try {
            JSONObject args=new JSONObject();
            args.put("key",key);
            submitInput("key",args);
        }catch(Exception error){status.setText("Не вдалося передати клавішу.");}
    }
    private void sendScroll(int turns) {
        try {
            JSONObject args=new JSONObject();
            args.put("delta",turns);
            submitInput("scroll",args);
        }catch(Exception error){status.setText("Не вдалося прокрутити.");}
    }
    private void submitInput(String kind,JSONObject args) {
        String session=activeSession;
        if(!visible||!controlling||session==null||freshFrameAt==0||
           System.currentTimeMillis()-freshFrameAt>3500) {
            if(status!=null)status.setText(
                "Керування недоступне або кадр екрана застарів.");
            return;
        }
        if(queuedInput.incrementAndGet()>18) {
            queuedInput.decrementAndGet();
            return;
        }
        try {
            args.put("id",UUID.randomUUID().toString());
            args.put("kind",kind);
            remoteInput.execute(()->{
                try {
                    if(!visible||!controlling||!session.equals(activeSession))return;
                    new AiChatClient(getApplicationContext()).deviceRelayRequest(
                        "/device/v1/screen/control/input?session="+session,
                        "POST",args);
                }catch(Exception error){
                    showStatus(session,"Windows не прийняв дію: "+
                        error.getMessage());
                }finally {queuedInput.decrementAndGet();}
            });
        }catch(Exception error){
            queuedInput.decrementAndGet();
        }
    }

    private JarvisRemoteTouchMapper.Point touchPoint(float x,float y) {
        Bitmap bitmap=displayedBitmap;
        if(bitmap==null||bitmap.isRecycled())return null;
        return JarvisRemoteTouchMapper.map(x,y,
            screen.getWidth(),screen.getHeight(),
            bitmap.getWidth(),bitmap.getHeight());
    }

    private void tapOrDouble(JarvisRemoteTouchMapper.Point mapped) {
        if(mapped==null)return;
        long now=android.os.SystemClock.uptimeMillis();
        if(pendingTap!=null && now-lastTapAt<350 && lastTap!=null &&
           Math.abs(mapped.x-lastTap.x)<0.055 &&
           Math.abs(mapped.y-lastTap.y)<0.055) {
            ui.removeCallbacks(pendingTap);
            pendingTap=null;
            sendPointer("double",mapped,null);
        } else {
            Runnable deferred=()->{
                pendingTap=null;
                sendPointer("tap",mapped,null);
            };
            pendingTap=deferred;
            ui.postDelayed(deferred,240);
        }
        lastTapAt=now;
        lastTap=mapped;
    }

    private void sendPointer(String kind,JarvisRemoteTouchMapper.Point start,
                             JarvisRemoteTouchMapper.Point end) {
        if(start==null)return;
        try {
            JSONObject data=new JSONObject();
            data.put("x",start.x);data.put("y",start.y);
            if(end!=null) {
                data.put("x2",end.x);data.put("y2",end.y);
            }
            submitInput(kind,data);
        }catch(Exception error){
            status.setText("Помилка дистанційного натискання.");
        }
    }

    /** Touches on black FIT_CENTER margins are rejected. One short tap is
     * a click; two taps are a double click; one-finger drag moves an icon;
     * long press right-clicks; two-finger swipe scrolls the PC page. */
    private boolean handleTouch(MotionEvent event) {
        if(!controlling || displayedBitmap==null ||
           System.currentTimeMillis()-freshFrameAt>3500)return true;
        int action=event.getActionMasked();
        switch(action) {
            case MotionEvent.ACTION_DOWN:
                touchStartX=event.getX();touchStartY=event.getY();
                twoFingerScroll=false;
                return true;
            case MotionEvent.ACTION_POINTER_DOWN:
                twoFingerScroll=true;
                scrollStartY=event.getY(0);
                scrollEndY=scrollStartY;
                return true;
            case MotionEvent.ACTION_MOVE:
                if(twoFingerScroll && event.getPointerCount()>=2)
                    scrollEndY=event.getY(0);
                return true;
            case MotionEvent.ACTION_UP:
                if(twoFingerScroll) {
                    double travel=scrollEndY-scrollStartY;
                    if(Math.abs(travel)>dp(10)) {
                        int direction=(int)Math.round(travel/Math.max(30,dp(40)));
                        if(direction==0)direction=travel>0?1:-1;
                        sendScroll(Math.max(-8,Math.min(8,direction)));
                    }
                    twoFingerScroll=false;
                    return true;
                }
                JarvisRemoteTouchMapper.Point start=
                    touchPoint(touchStartX,touchStartY);
                JarvisRemoteTouchMapper.Point end=
                    touchPoint(event.getX(),event.getY());
                if(start==null||end==null)return true;
                if(Math.hypot(event.getX()-touchStartX,
                              event.getY()-touchStartY)>dp(15)) {
                    if(pendingTap!=null)ui.removeCallbacks(pendingTap);
                    pendingTap=null;
                    sendPointer("drag",start,end);
                }else if(event.getEventTime()-event.getDownTime()>=550L) {
                    if(pendingTap!=null)ui.removeCallbacks(pendingTap);
                    pendingTap=null;
                    sendPointer("right",end,null);
                }else tapOrDouble(end);
                return true;
            case MotionEvent.ACTION_CANCEL:
                twoFingerScroll=false;
                return true;
            default:return true;
        }
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
        controlling=false;
        freshFrameAt=0;
        if(pendingTap!=null)ui.removeCallbacks(pendingTap);
        pendingTap=null;
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
        remoteInput.shutdown();
        stopTransport.shutdown();
        super.onDestroy();
    }
    private int dp(int d) {
        return Math.round(getResources().getDisplayMetrics().density*d);
    }
}
