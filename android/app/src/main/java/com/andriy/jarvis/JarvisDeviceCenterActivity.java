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
    private void addButton(LinearLayout p,String text,Class<?> destination) {
        Button b=new Button(this);b.setText(text);
        b.setOnClickListener(v->startActivity(new Intent(this,destination)));
        p.addView(b);
    }
    @Override public void onCreate(Bundle saved){
        super.onCreate(saved);
        if(getPackageManager().hasSystemFeature(
           android.content.pm.PackageManager.FEATURE_WATCH)){
            finish();return;
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
                JSONObject data=api.deviceRelayRequest("/device/v2/commands/status",
                    "POST",query,JarvisDeviceIdentity.signedHeaders(
                        "/device/v2/commands/status",
                        new JarvisDeviceIdentity.JSONObjectWrapper(query.toString())));
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
    @Override protected void onDestroy(){io.shutdownNow();super.onDestroy();}
}
