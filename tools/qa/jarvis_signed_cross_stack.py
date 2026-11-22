#!/usr/bin/env python3
"""Signed device relay security regression contracts, no APK/EXE or deployment."""
from pathlib import Path
import re
from xml.etree import ElementTree as ET

root=Path(__file__).resolve().parents[2]
read=lambda p:(root / p).read_text(encoding="utf-8")
worker=read("server/jarvis-ai-worker/src/device-relay.mjs")
pair=read("server/jarvis-ai-worker/src/device-pairing-v2.mjs")
broker=read("server/jarvis-ai-worker/src/device-command-broker-v2.mjs")
protocol=read("server/jarvis-ai-worker/src/device-protocol-v2.mjs")
pc=read("windows/Jarvis.Windows/Core/DeviceRelayClient.cs")
pc_grants=read("windows/Jarvis.Windows/Core/SignedDeviceLocalPolicy.cs")
pc_keys=read("windows/Jarvis.Windows/Core/DevicePairingService.cs")
android_relay=read("android/app/src/main/java/com/andriy/jarvis/JarvisDeviceRelay.java")
android_signed=read("android/app/src/main/java/com/andriy/jarvis/JarvisSignedDeviceCommands.java")
android_keys=read("android/app/src/main/java/com/andriy/jarvis/JarvisDeviceIdentity.java")
android_http=read("android/app/src/main/java/com/andriy/jarvis/AiChatClient.java")
app=read("android/app/src/main/java/com/andriy/jarvis/MainActivity.java")
wpf=read("windows/Jarvis.Windows/MainWindow.xaml.cs")
settings=read("windows/Jarvis.Windows/Core/LocalState.cs")

for required in ["/device/v2/commands/", "this.v2Commands.fetch(request)",
                 "v2:legacy-disabled", "Legacy Relay revoked"]:
    assert required in worker, f"Worker missing: {required}"
assert "verifySignedRequest" in broker and "this._auth(request,raw,now)" in broker
assert "validateDeviceRequest(body,now)" in broker
assert "authenticatedBy:device" in broker
assert "Device permission not granted" in broker
assert "DEVICE_REVOKED" in broker
assert "/device/v2/commands/authorize" in broker and "PERMISSION_REVOKED" in broker
assert '"/device/v2/commands/authorize"' in pc
assert "watchDelegation" in broker and "DEFAULT_DEVICE_GRANTS" in pair
assert "legacy-disabled" in pair and "grants:phone" in pair
assert "await store.delete('v2:grants:phone')" in pair
assert "await store.delete('v2:live:phone')" in pair
assert "NONCE_TTL" in pair and "PAIR_TTL" in pair
assert "Math.abs(now-Number(stamped))>SIGN_TTL" in pair
assert "SignedDeviceLocalPolicy.IsAllowed" in pc
assert "DeviceReceiptJournal _receipts=new()" in pc
assert "status=\"unknown\"" in pc
assert "signed:true" in pc_keys and "DataProtectionScope.CurrentUser" in pc_keys
assert "EnableSignedRelay" in settings and "SignedWatchDelegation" in settings
assert "SignedLocks && requiresApproval" in pc_grants
assert 'settings.EnableSignedRelay' in pc_grants
assert "JarvisSignedDeviceCommands.send" in android_relay
assert '"computer".equals(target) && JarvisSignedDeviceCommands.enabled(app)' in android_relay
assert "JarvisSecureCommandMap.fromVoice" in android_signed
assert "JarvisDeviceIdentity.signedHeaders" in android_signed
assert "new JarvisDeviceIdentity.JSONObjectWrapper(raw)" in android_signed
assert 'deviceRelayRequest(route,"POST",body,headers,raw)' in android_signed
assert "String rawJson" in android_http and "rawJson!=null?rawJson" in android_http
android_pairing=read("android/app/src/main/java/com/andriy/jarvis/JarvisDevicePairingActivity.java")
android_center=read("android/app/src/main/java/com/andriy/jarvis/JarvisDeviceCenterActivity.java")
assert '"/device/v2/pair/verify","POST",body,headers,raw' in android_pairing
assert "new JarvisDeviceIdentity.JSONObjectWrapper(raw)),raw" in android_center
assert "v2/commands/send" in android_signed and "v2/commands/result" in android_signed
assert "Не повторюю" not in android_signed or "автоматично не запускаю" in android_signed
assert 'private static final String FLAG="signed_v2_enabled"' in android_signed
assert "AndroidKeyStore" in android_keys
assert "JarvisDeviceV2Paths.httpAllowed(route)" in android_http
android_routes=read("android/app/src/main/java/com/andriy/jarvis/JarvisDeviceV2Paths.java")
assert "/device/v2/" in android_routes
assert "commands/(?:send|result|heartbeat|status)" in android_routes
assert "JarvisDeviceV2Paths.signedPhonePath(path)" in android_keys
assert "JarvisSignedDeviceCommands.setEnabled" in app
assert "PairGrantsSave_Click" in wpf and "PairRevoke_Click" in wpf
assert "DeviceReceiptJournal" in pc
assert '"shell.run"' not in pc
assert "requestId" in protocol and "expiresAt" in protocol
manifest=ET.parse(root/"android/app/src/main/AndroidManifest.xml").getroot()
android_ns="{http://schemas.android.com/apk/res/android}"
for name in [".JarvisDevicePairingActivity",".JarvisDeviceCenterActivity",
             ".JarvisWakeOnLanActivity"]:
    entries=[a for a in manifest.findall(".//activity")
             if a.get(android_ns+"name")==name]
    assert len(entries)==1 and entries[0].get(android_ns+"exported")=="false",name

packet=read("android/app/src/main/java/com/andriy/jarvis/WakeOnLanPacket.java")
wol=read("android/app/src/main/java/com/andriy/jarvis/JarvisWakeOnLan.java")
assert "matchesSubnet" in packet and "matchesSubnet" in wol
assert "network.bindSocket(socket)" in wol
print("PASS signed JARVIS Windows/Pixel/Watch source contracts (no builds)")
