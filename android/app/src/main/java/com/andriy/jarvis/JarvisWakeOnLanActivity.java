package com.andriy.jarvis;

import android.app.Activity;
import android.os.Bundle;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.text.InputType;

/** Local explicit WoL setup. Nothing is exposed to other applications. */
public final class JarvisWakeOnLanActivity extends Activity {
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        if(getPackageManager().hasSystemFeature(PackageManager.FEATURE_WATCH)){finish();return;}
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
        LinearLayout panel=new LinearLayout(this);
        panel.setOrientation(LinearLayout.VERTICAL);
        panel.setPadding(28,30,28,24);
        panel.setBackgroundColor(Color.rgb(12,25,44));
        TextView label=new TextView(this);
        label.setText("⚡ Wake-on-LAN\nВведи MAC мережевого адаптера Windows і broadcast домашньої Wi-Fi мережі. "+
            "Доступне тільки з Pixel у цій Wi-Fi мережі. Після відправки сигналу статус ПК перевіряється окремо.\n");
        label.setTextColor(Color.WHITE);label.setTextSize(17);panel.addView(label);
        EditText mac=new EditText(this);
        mac.setSingleLine(true);mac.setTextColor(Color.WHITE);mac.setHint("AA:BB:CC:DD:EE:FF");
        mac.setText(JarvisWakeOnLan.savedMac(this));panel.addView(mac);
        EditText ip=new EditText(this);
        ip.setSingleLine(true);ip.setInputType(InputType.TYPE_CLASS_PHONE);
        ip.setTextColor(Color.WHITE);ip.setHint("192.168.1.255");
        ip.setText(JarvisWakeOnLan.savedBroadcast(this));panel.addView(ip);
        TextView status=new TextView(this);status.setTextColor(Color.WHITE);
        status.setText("WoL залежить від BIOS, NIC, режиму сну та Ethernet.");
        panel.addView(status);
        Button save=new Button(this);save.setText("💾 Зберегти адресу ПК");
        save.setOnClickListener(v->{
            try{
                JarvisWakeOnLan.configure(this,mac.getText().toString().trim(),
                    ip.getText().toString().trim());
                status.setText("Налаштування збережено на Pixel.");
            }catch(Exception e){status.setText(e.getMessage());}
        });
        panel.addView(save);
        Button test=new Button(this);test.setText("⚡ Надіслати тестовий Magic Packet");
        test.setOnClickListener(v->{
            status.setText("Перевіряю домашню мережу та відправляю сигнал…");
            JarvisWakeOnLan.send(this,(sent,detail)->status.setText(detail));
        });
        panel.addView(test);
        setContentView(panel);
    }
}
