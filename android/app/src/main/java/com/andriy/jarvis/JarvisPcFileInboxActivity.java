package com.andriy.jarvis;

import android.app.Activity;
import android.content.Intent;
import android.database.Cursor;
import android.provider.OpenableColumns;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.net.Uri;
import android.os.Bundle;
import android.os.Looper;
import android.util.Base64;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;
import org.json.JSONObject;
import java.io.OutputStream;
import java.io.InputStream;
import java.io.ByteArrayOutputStream;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** Explicit Pixel download of one temporary owner-selected Windows file.
 * Never installs or opens received binaries. The file is kept in RAM
 * until Android's ACTION_CREATE_DOCUMENT completes, then Worker is ACKed.
 */
public final class JarvisPcFileInboxActivity extends Activity {
    private static final int SAVE_FILE=5701;
    private static final int PICK_FILE=5702;
    private static final int MAX_FILE_BYTES=1_048_576;
    private final ExecutorService io=Executors.newSingleThreadExecutor();
    private TextView status;
    private Button save;
    private volatile byte[] pendingBytes;
    private String pendingName="",pendingMime="application/octet-stream",pendingId="";
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        if(getPackageManager().hasSystemFeature(PackageManager.FEATURE_WATCH)) {
            finish();return;
        }
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
        LinearLayout layout=new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setPadding(20,28,20,20);
        layout.setBackgroundColor(Color.rgb(11,23,41));
        status=new TextView(this);
        status.setTextColor(Color.WHITE);
        status.setTextSize(16);
        status.setText("📂 Файли з Windows\nДані доступні до 2 хвилин. Ліміт 1 МіБ.");
        layout.addView(status);
        Button refresh=new Button(this);
        refresh.setText("🔄 Перевірити файл");
        refresh.setOnClickListener(v->load());
        layout.addView(refresh);
        save=new Button(this);
        save.setText("💾 Зберегти вибраний файл");
        save.setEnabled(false);
        save.setOnClickListener(v->chooseLocation());
        layout.addView(save);
        Button upload=new Button(this);
        upload.setText("📤 Вибрати файл і передати на Windows");
        upload.setOnClickListener(v->{
            Intent pick=new Intent(Intent.ACTION_OPEN_DOCUMENT);
            pick.addCategory(Intent.CATEGORY_OPENABLE);
            pick.setType("*/*");
            startActivityForResult(pick,PICK_FILE);
        });
        layout.addView(upload);
        setContentView(layout);
        load();
    }
    private void load() {
        if(Looper.myLooper()!=Looper.getMainLooper())return;
        status.setText("Шукаю файл у приватному JARVIS Relay…");
        save.setEnabled(false);
        pendingBytes=null;pendingId="";
        io.execute(()->{
            try {
                JSONObject response=new AiChatClient(getApplicationContext())
                    .deviceRelayRequest("/device/v1/files/latest","GET",null);
                if(!response.optBoolean("available",false)) {
                    runOnUiThread(()->status.setText(
                        "Немає нового файла. На ПК вибери «Передати на Pixel»."));
                    return;
                }
                String id=response.optString("id","");
                String filename=response.optString("name","");
                String mime=response.optString("mime","application/octet-stream");
                String encoded=response.optString("data","");
                byte[] bytes=Base64.decode(encoded,Base64.DEFAULT);
                if(!JarvisWearProtocol.validId(id) || filename.isEmpty() ||
                   filename.length()>100 || bytes.length<1 ||
                   bytes.length>MAX_FILE_BYTES)
                    throw new IllegalArgumentException("Невірний файл від Relay.");
                runOnUiThread(()->{
                    if(isFinishing()||isDestroyed())return;
                    pendingId=id;pendingName=filename;
                    pendingMime=mime.matches("[a-z0-9.+-]+/[a-z0-9.+-]+")
                        ?mime:"application/octet-stream";
                    pendingBytes=bytes;
                    status.setText("📄 "+filename+" · "+bytes.length+
                        " байтів\nВибери, куди зберегти файл на Pixel.");
                    save.setEnabled(true);
                });
            }catch(Exception error) {
                runOnUiThread(()->status.setText(
                    "Файл недоступний. Перевір підключення до Worker та повтори."));
            }
        });
    }
    private void chooseLocation() {
        if(pendingBytes==null || pendingId.isEmpty())return;
        Intent choose=new Intent(Intent.ACTION_CREATE_DOCUMENT);
        choose.addCategory(Intent.CATEGORY_OPENABLE);
        choose.setType(pendingMime);
        choose.putExtra(Intent.EXTRA_TITLE,pendingName);
        startActivityForResult(choose,SAVE_FILE);
    }
    @Override protected void onActivityResult(int request,int result,Intent data) {
        super.onActivityResult(request,result,data);
        if(request==PICK_FILE) {
            if(result==RESULT_OK && data!=null && data.getData()!=null)
                sendToWindows(data.getData());
            return;
        }
        if(request!=SAVE_FILE || result!=RESULT_OK || data==null ||
           data.getData()==null || pendingBytes==null)return;
        Uri selected=data.getData();
        final byte[] bytes=pendingBytes;
        final String id=pendingId;
        save.setEnabled(false);
        status.setText("Зберігаю файл…");
        io.execute(()->{
            try {
                try(OutputStream output=getContentResolver().openOutputStream(selected,"w")) {
                    if(output==null)throw new IllegalStateException("Не вдалося відкрити файл.");
                    output.write(bytes);
                    output.flush();
                }
                // ACK only AFTER successful write: don't lose file on picker cancel.
                try {
                    new AiChatClient(getApplicationContext()).deviceRelayRequest(
                        "/device/v1/files/ack","POST",new JSONObject().put("id",id));
                }catch(Exception ignored) {
                    // Saved locally; relay still deletes it when its TTL expires.
                }
                runOnUiThread(()->{
                    pendingBytes=null;pendingId="";
                    status.setText("✅ Файл збережено на Pixel.");
                });
            }catch(Exception error) {
                runOnUiThread(()->{
                    save.setEnabled(pendingBytes!=null);
                    status.setText("Не вдалося зберегти файл. Вибери іншу папку.");
                });
            }
        });
    }
    /** Pixel document picker, explicit user action; no automatic file scan. */
    private void sendToWindows(Uri uri) {
        status.setText("Передаю вибраний файл у тимчасовий Relay…");
        io.execute(()->{
            try {
                String name="pixel-file";
                try(Cursor cursor=getContentResolver().query(uri,
                    new String[]{OpenableColumns.DISPLAY_NAME},null,null,null)) {
                    if(cursor!=null && cursor.moveToFirst())
                        name=cursor.getString(0);
                }
                if(name==null || name.length()<1 || name.length()>100 ||
                   name.equals(".") || name.equals("..") ||
                   name.matches(".*[\\\\/:*?\"<>|\\p{Cntrl}].*"))
                    throw new IllegalArgumentException("Недопустиме ім'я файла.");
                ByteArrayOutputStream bytes=new ByteArrayOutputStream();
                try(InputStream in=getContentResolver().openInputStream(uri)) {
                    if(in==null)throw new IllegalStateException("Файл недоступний.");
                    byte[] chunk=new byte[8192];int count;
                    while((count=in.read(chunk))!=-1) {
                        if(bytes.size()+count>MAX_FILE_BYTES)
                            throw new IllegalArgumentException("Файл більший за 1 МіБ.");
                        bytes.write(chunk,0,count);
                    }
                }
                if(bytes.size()==0)throw new IllegalArgumentException("Порожній файл.");
                String mime=getContentResolver().getType(uri);
                if(mime==null || !mime.matches("[a-z0-9.+-]+/[a-z0-9.+-]+"))
                    mime="application/octet-stream";
                JSONObject body=new JSONObject();
                body.put("id",UUID.randomUUID().toString());
                body.put("source","phone");body.put("target","computer");
                body.put("name",name);body.put("mime",mime);
                body.put("data",Base64.encodeToString(bytes.toByteArray(),Base64.NO_WRAP));
                new AiChatClient(getApplicationContext()).deviceRelayRequest(
                    "/device/v1/files/send","POST",body);
                final String selectedName=name;
                runOnUiThread(()->status.setText(
                    "📤 Файл «"+selectedName+"» доступний у JARVIS Windows до 2 хвилин. Відкрий вкладку «Обмін файлами» на ПК."));
            }catch(Exception error) {
                runOnUiThread(()->status.setText(
                    "Передавання не виконано: файл завеликий, зв'язок відсутній або папка ПК зайнята."));
            }
        });
    }
    @Override protected void onDestroy() {
        pendingBytes=null;
        io.shutdown();
        super.onDestroy();
    }
}
