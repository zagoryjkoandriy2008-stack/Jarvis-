package com.andriy.jarvis;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.os.BatteryManager;
import android.os.Bundle;
import android.graphics.Color;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import com.google.android.gms.wearable.Wearable;
import org.json.JSONObject;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** Device hub with signed PC/Pixel presence and honest Wear connection state.
 * No invented remote battery, temperature or microphone telemetry.
 */
public final class JarvisDeviceCenterActivity extends Activity {
    private final ExecutorService io=Executors.newSingleThreadExecutor();
    private TextView status,watchText;
    private JarvisWearPhoneLink watchLink;
    private void addButton(LinearLayout p,String text,Class<?> destination) {
        Button b=new Button(this);b.setText(text);
        b.setOnClickListener(v->startActivity(new Intent(this,destination)));
        p.addView(b);
    }
    @Override public void onCreate(Bundle saved){
        super.onCreate(saved);
        if(getPackageManager().hasSystemFeature(
           android.content.pm.PackageManager.FEATURE_WATCH)){
            showWatchCenter();return;
        }
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
        ScrollView scroll=new ScrollView(this);
        LinearLayout panel=new LinearLayout(this);
        panel.setOrientation(LinearLayout.VERTICAL);
        panel.setPadding(25,28,25,20);
        panel.setBackgroundColor(Color.rgb(12,25,44));
        scroll.addView(panel);
        TextView heading=new TextView(this);
        heading.setText("🖥 📱 ⌚ JARVIS · Центр пристроїв");
        heading.setTextSize(20);heading.setTextColor(Color.WHITE);
        panel.addView(heading);
        status=new TextView(this);
        status.setTextSize(16);status.setTextColor(Color.WHITE);
        status.setText("Перевірка ще не виконана.");
        panel.addView(status);
        watchText=new TextView(this);
        watchText.setTextColor(Color.WHITE);
        watchText.setTextSize(15);
        watchText.setText("⌚ Watch7: зв'язок ще не перевірений.");
        panel.addView(watchText);
        Button refresh=new Button(this);
        refresh.setText("⟳ Перевірити підписаний статус");
        refresh.setOnClickListener(v->refresh());
        panel.addView(refresh);
        addButton(panel,"🔒 Спарювання і перевірка ключа",JarvisDevicePairingActivity.class);
        addButton(panel,"🖥 Перегляд екрана Windows",JarvisWindowsScreenActivity.class);
        addButton(panel,"📂 Файли ПК",JarvisPcFileInboxActivity.class);
        addButton(panel,"⚡ Wake-on-LAN",JarvisWakeOnLanActivity.class);
        TextView disclaimer=new TextView(this);
        disclaimer.setText("Online — лише свіжий підписаний heartbeat (35 секунд). "+
            "Заряд ПК/Watch7 і температура: немає даних, якщо пристрій їх не передав. "+
            "Wear Data Layer показує підключення годинника, а не гарантує, "+
            "що JARVIS на ньому відкритий. Відкликання доступу — з Windows.");
        disclaimer.setTextColor(Color.LTGRAY);
        disclaimer.setTextSize(12);panel.addView(disclaimer);
        setContentView(scroll);
        refresh();
    }
    /** Wear OS device hub uses Pixel as the only authenticated cloud bridge. */
    private void showWatchCenter() {
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
        ScrollView scroll=new ScrollView(this);
        LinearLayout panel=new LinearLayout(this);
        panel.setOrientation(LinearLayout.VERTICAL);
        panel.setPadding(18,14,18,18);
        panel.setBackgroundColor(Color.rgb(12,25,44));
        scroll.addView(panel);
        TextView heading=new TextView(this);
        heading.setText("⌚ JARVIS · Пристрої");
        heading.setTextColor(Color.WHITE);
        heading.setTextSize(16);
        panel.addView(heading);
        TextView local=new TextView(this);
        local.setTextColor(Color.LTGRAY);
        local.setTextSize(13);
        local.setText("Watch7: заряд "+battery()+".");
        panel.addView(local);
        TextView linkStatus=new TextView(this);
        linkStatus.setTextSize(13);
        linkStatus.setTextColor(Color.WHITE);
        linkStatus.setText("Pixel: зв'язок ще не перевірено.");
        panel.addView(linkStatus);
        TextView pcStatus=new TextView(this);
        pcStatus.setTextSize(13);
        pcStatus.setTextColor(Color.WHITE);
        pcStatus.setText("Windows: ще не запитано.");
        panel.addView(pcStatus);
        Button refresh=new Button(this);
        refresh.setText("⟳ Перевірити");
        refresh.setOnClickListener(v->{
            pcStatus.setText("Запитую Windows через Pixel…");
            local.setText("Watch7: заряд "+battery()+".");
            Wearable.getNodeClient(this).getConnectedNodes()
                .addOnSuccessListener(nodes->{
                    if(isFinishing()||isDestroyed())return;
                    linkStatus.setText(nodes!=null&&!nodes.isEmpty()?
                        "Wear Data Layer: "+nodes.size()+
                        " вузол(ли); стан Pixel JARVIS ще невідомий.":
                        "Pixel: немає підключених вузлів Wear Data Layer.");
                }).addOnFailureListener(error->{
                    if(!isFinishing()&&!isDestroyed())
                        linkStatus.setText("Wear Data Layer: немає даних.");
                });
            if(watchLink==null)watchLink=new JarvisWearPhoneLink(this);
            watchLink.assistant("__jarvis_pc__:покажи пристрої",(ok,detail)->
                runOnUiThread(()->{
                    if(!isFinishing()&&!isDestroyed())pcStatus.setText(
                        (ok?"Windows: ":"Windows: не підтверджено. ")+
                        (detail==null?"Немає відповіді.":detail));
                }));
        });
        panel.addView(refresh);
        Button back=new Button(this);
        back.setText("← До JARVIS");
        back.setOnClickListener(v->finish());
        panel.addView(back);
        TextView note=new TextView(this);
        note.setTextColor(Color.LTGRAY);
        note.setTextSize(11);
        note.setText("Windows доступний через спарений Pixel. Закриття цієї панелі не вимикає JARVIS.");
        panel.addView(note);
        setContentView(scroll);
    }

    private String battery(){
        try {
            Intent i=registerReceiver(null,new IntentFilter(Intent.ACTION_BATTERY_CHANGED));
            if(i==null)return "немає даних";
            int v=i.getIntExtra(BatteryManager.EXTRA_LEVEL,-1);
            int scale=i.getIntExtra(BatteryManager.EXTRA_SCALE,-1);
            if(v<0||scale<=0)return "немає даних";
            return Math.round(100f*v/scale)+"%";
        }catch(Exception e){return "немає даних";}
    }
    private void refresh(){
        status.setText("📱 Pixel: JARVIS відкритий тут. Заряд: "+battery()+
            "\n🖥 Windows: перевірка захищеного Relay…");
        watchText.setText("⌚ Watch7: перевіряю Wear Data Layer…");
        Wearable.getNodeClient(this).getConnectedNodes()
            .addOnSuccessListener(nodes->{
                if(isFinishing()||isDestroyed())return;
                watchText.setText(nodes!=null && !nodes.isEmpty()?
                    "⌚ Wear Data Layer: підключено "+nodes.size()+
                    " пристрій(їв); статус застосунку JARVIS невідомий.":
                    "⌚ Wear Data Layer: підключених пристроїв немає.");
            })
            .addOnFailureListener(e->{
                if(!isFinishing()&&!isDestroyed())
                    watchText.setText("⌚ Wear Data Layer: немає даних.");
            });
        io.execute(()->{
            String result;
            try {
                AiChatClient api=new AiChatClient(this);
                JSONObject query=new JSONObject();query.put("device","computer");
                final String raw=query.toString();
                JSONObject data=api.deviceRelayRequest("/device/v2/commands/status",
                    "POST",query,JarvisDeviceIdentity.signedHeaders(
                        "/device/v2/commands/status",
                        new JarvisDeviceIdentity.JSONObjectWrapper(raw)),raw);
                boolean online=data.optBoolean("online",false);
                long heartbeat=data.optLong("lastSeenMs",0);
                String last=heartbeat>0?
                    new java.text.SimpleDateFormat("HH:mm:ss",java.util.Locale.ROOT)
                        .format(new java.util.Date(heartbeat)):"немає даних";
                result="🖥 Windows: "+(online?"online (підписано)":"offline/не підтверджено")+
                    "; останній зв'язок "+last+
                    "\n📱 Pixel: цей JARVIS активний; заряд "+battery()+
                    "\nПрава ПК змінюються у Windows, а не через старий спільний токен.";
            }catch(Exception ignored){
                result="🖥 Windows: немає підтверджених даних v2. "+
                    "Перевір pairing, дозвіл Relay та мережу."+
                    "\n📱 Pixel: цей JARVIS активний; заряд "+battery();
            }
            final String msg=result;
            runOnUiThread(()->{
                if(!isFinishing()&&!isDestroyed())status.setText(msg);
            });
        });
    }
    @Override protected void onDestroy(){
        if(watchLink!=null)watchLink.closePreservingMode();
        io.shutdownNow();
        super.onDestroy();
    }
}
