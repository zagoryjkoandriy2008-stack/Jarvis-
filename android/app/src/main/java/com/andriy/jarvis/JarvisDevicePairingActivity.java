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
import org.json.JSONObject;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** Pixel owner enters one-time Windows pairing ID and code, compares fingerprints. */
public final class JarvisDevicePairingActivity extends Activity {
    private final ExecutorService io=Executors.newSingleThreadExecutor();
    private TextView status;
    private void output(String s){runOnUiThread(()->{
        if(!isFinishing()&&!isDestroyed())status.setText(s);
    });}
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        if(getPackageManager().hasSystemFeature(PackageManager.FEATURE_WATCH)){finish();return;}
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
        LinearLayout panel=new LinearLayout(this);
        panel.setOrientation(LinearLayout.VERTICAL);
        panel.setPadding(28,30,28,24);
        panel.setBackgroundColor(Color.rgb(12,25,44));
        TextView label=new TextView(this);
        label.setText("🔒 Спарювання Pixel ↔ Windows\n"+
            "На ПК у вкладці «Пристрої» натисни «Створити код». Введи ID і 7-значний код. "+
            "Порівняй відбитки ключів перед підтвердженням на ПК.\n");
        label.setTextSize(17);label.setTextColor(Color.WHITE);panel.addView(label);
        EditText id=new EditText(this);
        id.setSingleLine(true);id.setTextColor(Color.WHITE);
        id.setHint("ID з Windows, формат UUID");panel.addView(id);
        EditText code=new EditText(this);
        code.setSingleLine(true);code.setTextColor(Color.WHITE);
        code.setHint("7-значний одноразовий код");panel.addView(code);
        status=new TextView(this);status.setTextColor(Color.WHITE);
        status.setText("Ключ Pixel зберігається в Android Keystore.");
        panel.addView(status);
        Button pair=new Button(this);pair.setText("Надіслати запит на спарювання");
        pair.setOnClickListener(v->{
            String session=id.getText().toString().trim();
            String pin=code.getText().toString().trim();
            try{UUID.fromString(session);
                if(!pin.matches("^\\d{7}$"))throw new Exception("Код повинен містити 7 цифр.");
            }catch(Exception e){output("Перевір ID та 7-значний код.");return;}
            output("Створюю ключ Pixel у Keystore та надсилаю запит…");
            io.execute(()->{
                try {
                    String publicKey=JarvisDeviceIdentity.publicKeyBase64();
                    JSONObject body=new JSONObject();
                    body.put("pairingId",session);
                    body.put("code",pin);
                    body.put("device","phone");
                    body.put("publicKey",publicKey);
                    JSONObject result=new AiChatClient(this)
                        .deviceRelayRequest("/device/v2/pair/request","POST",body);
                    output("Запит надіслано. Стан: "+result.optString("state")+
                        "\nВідбиток ключа Pixel: "+JarvisDeviceIdentity.fingerprint()+
                        "\nНа ПК натисни «Перевірити запит», звір 16 символів і «Підтвердити».");
                }catch(Exception e){output("Не вдалося спарувати: "+e.getMessage());}
            });
        });
        panel.addView(pair);
        Button verify=new Button(this);verify.setText("Перевірити криптографічне підтвердження");
        verify.setOnClickListener(v->io.execute(()->{
            try{
                JSONObject body=new JSONObject();
                java.util.Map<String,String> headers=JarvisDeviceIdentity.signedHeaders(
                    "/device/v2/pair/verify",
                    new JarvisDeviceIdentity.JSONObjectWrapper(body.toString()));
                JSONObject result=new AiChatClient(this).deviceRelayRequest(
                    "/device/v2/pair/verify","POST",body,headers);
                output(result.optBoolean("verified")?
                    "✅ Сервер перевірив підпис ключа Pixel. "+
                    "Розширені дії через Relay v2 ще не увімкнені.":
                    "Підтвердження не отримано.");
            }catch(Exception e){output("Ключ не підтверджено: "+e.getMessage());}
        }));
        panel.addView(verify);
        setContentView(panel);
    }
    @Override protected void onDestroy(){io.shutdownNow();super.onDestroy();}
}
