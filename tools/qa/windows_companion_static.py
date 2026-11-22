#!/usr/bin/env python3
"""No-build source wiring checks for Windows/Pixel/Cloudflare companion.

Static checks cannot replace full Windows WPF / Android compilation or device QA.
"""
from pathlib import Path
from xml.etree import ElementTree as ET
import re

root = Path(__file__).resolve().parents[2]
win = root / "windows/Jarvis.Windows"
android = root / "android/app/src/main/java/com/andriy/jarvis"
worker = root / "server/jarvis-ai-worker/src"
read = lambda p: p.read_text(encoding="utf-8")
xaml = ET.parse(win / "MainWindow.xaml").getroot()
cs = read(win / "MainWindow.xaml.cs")
ns = "{http://schemas.microsoft.com/winfx/2006/xaml}"
names = {element.get(ns + "Name") for element in xaml.iter() if element.get(ns + "Name")}
handlers = [element.get("Click") for element in xaml.iter() if element.get("Click")]

for required in ["SystemStatusReport", "FileExchangeStatus",
                 "ControlRequestPanel", "ControlActivePanel"]:
    assert required in names, "Missing XAML element: " + required
for name in handlers:
    assert re.search(r"\b" + re.escape(name) + r"\s*\(", cs), (
        "Missing WPF click handler: " + name)
assert len(handlers) >= 12, "Suspiciously few Windows UI handlers"
assert "WindowsRemoteCommandPolicy.IsAllowed(command.Kind)" in cs
assert "if(_busy)return" in cs and "_busy=true;" in cs and "_busy=false;" in cs
assert "case CommandKind.AnalyzeScreen:" in cs
assert "MessageBoxButton.YesNo" in cs
assert "WindowsScreenPublisher.CaptureForAi()" in cs
assert "GetFromPixelAsync" in cs and "SendToPixelAsync" in cs
assert "new OpenFileDialog" in cs and "new SaveFileDialog" in cs
assert "HttpClient" in read(win / "Core/WindowsFileExchange.cs")
assert "ReadMemory()" in read(win / "Core/WindowsSystemMonitor.cs")
assert "OperatingSystem.IsWindows()" in read(win / "Core/WindowsSystemMonitor.cs")

manifest = ET.parse(root / "android/app/src/main/AndroidManifest.xml").getroot()
ANDROID_NS = "{http://schemas.android.com/apk/res/android}"
activities = [x for x in manifest.findall(".//activity")
              if x.get(ANDROID_NS + "name") == ".JarvisPcFileInboxActivity"]
assert len(activities) == 1
assert activities[0].get(ANDROID_NS + "exported") == "false"
route = read(android / "JarvisScreenViewCommands.java")
main = read(android / "MainActivity.java")
orb = read(android / "FloatingJarvisService.java")
inbox = read(android / "JarvisPcFileInboxActivity.java")
for content in [main, orb]:
    assert "JarvisScreenViewCommands.files(raw)" in content
    assert content.index("JarvisScreenViewCommands.files(raw)") < content.index(
        "JarvisScreenViewCommands.open(raw)")
assert "Intent.ACTION_OPEN_DOCUMENT" in inbox
assert "Intent.ACTION_CREATE_DOCUMENT" in inbox
assert "JarvisPcFileInboxActivity.class" in main
assert "JarvisPcFileInboxActivity.class" in orb
assert "FileExchangeStatus" in names
assert "FLAG_SECURE" in inbox
assert "MAX_FILE_BYTES=1_048_576" in inbox

relay = read(worker / "file-relay.mjs")
router = read(worker / "device-relay.mjs")
index = read(worker / "index.mjs")
assert "this.files=new Map()" in relay
assert "TTL=120_000" in relay
assert "MAX_FILE_BYTES=1_048_576" in relay
assert "boundedJson(request,1_500_000)" in relay
assert "boundedJson(request,4096)" in relay
assert "this.fileInbox.fetch(request)" in router
assert "fileUpload?" in index
assert "this.state.storage" not in relay
assert "storage.put" not in relay

print("PASS JARVIS companion source-only UI, voice, privacy and file contracts")
