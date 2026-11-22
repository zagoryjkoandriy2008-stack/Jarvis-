using Jarvis.Windows.Core;
using System.Diagnostics;

var assertions=0;
void Check(bool expected,string description) {
    assertions++;
    if(!expected)throw new Exception("TEST FAILURE: "+description);
}
var cases=new [] {
    ("джарвіс увімкни пісню Мама",CommandKind.SpotifySearch,"Мама"),
    ("увімкни пісню Пачка сигарет",CommandKind.SpotifySearch,"Пачка сигарет"),
    ("Включи песню Звезда по имени Солнце",CommandKind.SpotifySearch,"Звезда по имени Солнце"),
    ("увімкни плейлист Реп",CommandKind.SpotifyPlaylist,"Реп"),
    ("джарвіс увімкни плейлист Для дороги",CommandKind.SpotifyPlaylist,"Для дороги"),
    ("Знайди кав'ярні",CommandKind.WebSearch,"кав'ярні"),
    ("Відкрий блокнот",CommandKind.OpenApp,"блокнот"),
    ("постав гучність на 70",CommandKind.Volume,""),
    ("таймер 30 секунд",CommandKind.Timer,""),
    ("напиши привіт усім",CommandKind.TypeIntoActiveWindow,"привіт усім"),
    ("Наступна пісня",CommandKind.MediaNext,""),
    ("Заробив 200 готівка зарплата зміна",CommandKind.FinanceAdd,""),
    ("Витратив 150 готівка транспорт таксі",CommandKind.FinanceAdd,""),
    ("баланс рахунків",CommandKind.FinanceReport,"")
};
foreach(var (spoken,kind,arg) in cases) {
    var parsed=CommandRouter.Parse(spoken);
    Check(parsed.Kind==kind,spoken+" => "+kind+" instead "+parsed.Kind);
    if(arg.Length>0)Check(parsed.Argument.Equals(arg,StringComparison.OrdinalIgnoreCase),
        spoken+" argument "+parsed.Argument);
}
var expense=CommandRouter.Parse("витратив 150 готівка транспорт автобус").Finance;
Check(expense is {Income:false,Amount:150,Account:"готівка",Category:"транспорт",Note:"автобус"},
    "finance one-word category and rest as note");
var income=CommandRouter.Parse("заробив 200 карта моно зарплата робота").Finance;
Check(income is {Income:true,Amount:200,Account:"карта моно",Category:"зарплата",Note:"робота"},
    "card income parse");
Check(CommandRouter.Parse("витратив -5 готівка транспорт").Kind!=CommandKind.FinanceAdd,
    "reject negative finance");
Check(CommandRouter.Parse("гучність 101").Kind!=CommandKind.Volume,"reject unsafe volume");
Check(CommandRouter.Parse("відкрий powershell.exe").Kind==CommandKind.OpenApp,
    "parser allows name but allowlist refuses unapproved process");
Check(!AiClient.ValidEndpoint("http://example.com/chat"),"HTTPS required");
Check(AiClient.ValidEndpoint("https://my-worker.example.workers.dev/chat"),
    "private Cloudflare Worker endpoint supported");
Check(!AiClient.ValidEndpoint("https://example.com/chat?token=secret"),
    "no credentials or query parameters in endpoint");
var macro=MacroEngine.New("початок дня","відкрий блокнот\nгучність 35\nскажи доброго ранку");
Check(macro.Actions.Count==3,"macro actions");
var state=new JarvisState{Macros=new(){macro}};
Check(MacroEngine.Match(state,"джарвіс початок дня")!=null,"wake-word macro matching");
Check(MacroEngine.Match(state,"початок вечора")==null,"exact macro trigger");
foreach(var bad in new[]{"powershell -command whoami","напиши пароль",
    "витратив 100 готівка транспорт","джарвіс створити файл"}) {
    try { MacroEngine.New("тест",bad);throw new Exception("Unsafe macro allowed: "+bad); }
    catch(ArgumentException) { assertions++; }
}
foreach(var (voice,target) in new (string,JarvisDevice)[]{
    ("телефон",JarvisDevice.Phone),("Джарвіс телефон",JarvisDevice.Phone),
    ("годинник",JarvisDevice.Watch),("Дімон годинник",JarvisDevice.Watch),
    ("комп'ютер",JarvisDevice.Computer),("компютер",JarvisDevice.Computer),
    ("компьютер",JarvisDevice.Computer),("комп'ютер.",JarvisDevice.Computer),
    ("Jarvis computer",JarvisDevice.Computer)}) {
    Check(DeviceRouting.TargetChange(voice)==target,"three-device mode: "+voice);
}
Check(DeviceRouting.TargetChange("увімкни пісню мама")==null,
    "ordinary music must not switch device mode");
Check(DeviceRouting.TargetChange("дімон")==null,
    "wakeword alone does not select remote device");
Check(DeviceRouting.EffectiveTarget(JarvisDevice.Computer,CommandKind.FinanceAdd)
    ==JarvisDevice.Phone,"Pixel authoritative finance");
Check(DeviceRouting.EffectiveTarget(JarvisDevice.Watch,CommandKind.FinanceReport)
    ==JarvisDevice.Phone,"watch financial report must use Pixel");
Check(DeviceRouting.EffectiveTarget(JarvisDevice.Computer,CommandKind.Volume)
    ==JarvisDevice.Computer,"Windows-local system commands unchanged");
Check(DeviceRouting.EffectiveTarget(JarvisDevice.Watch,CommandKind.SpotifySearch)
    ==JarvisDevice.Watch,"watch Spotify target preserved");
// Cross-device notes and advanced finance must route to the Pixel authority,
// regardless of whether the user's current mode is PC or Watch.
foreach(var (utterance,kind) in new (string,CommandKind)[]{
    ("запиши нотатку Купити хліб",CommandKind.NoteAdd),
    ("створи нотатку Завтра зустріч",CommandKind.NoteAdd),
    ("додай нотатку Тренування",CommandKind.NoteAdd),
    ("нотатка Купити воду",CommandKind.NoteAdd),
    ("занотуй Кава о сьомій",CommandKind.NoteAdd),
    ("покажи нотатки",CommandKind.NoteRead),
    ("мої нотатки",CommandKind.NoteRead),
    ("прочитай нотатки",CommandKind.NoteRead),
    ("видали останню нотатку",CommandKind.NoteDelete)
}) {
    var parsed=CommandRouter.Parse(utterance);
    Check(parsed.Kind==kind,"notes command "+utterance);
    Check(DeviceRouting.EffectiveTarget(JarvisDevice.Computer,kind)==JarvisDevice.Phone,
        "Pixel is notes authority: "+utterance);
    Check(DeviceRouting.EffectiveTarget(JarvisDevice.Watch,kind)==JarvisDevice.Phone,
        "Notes from Watch mode still use Pixel: "+utterance);
}
Check(CommandRouter.Parse("нотатка купити каву").Argument=="купити каву",
    "notes content only, no redundant prefix");
Check(DeviceRouting.EffectiveTarget(JarvisDevice.Computer,
    CommandRouter.Parse("скільки витратив за місяць").Kind)==JarvisDevice.Phone,
    "Finance queries still on Pixel");
// A process crash or lost ACK may cause Cloudflare to redeliver the same
// remote request. The Windows side effect must not execute twice.
var receiptPath=System.IO.Path.Combine(System.IO.Path.GetTempPath(),
    "jarvis-receipt-test-"+Guid.NewGuid().ToString("N")+".json");
try {
    var first=new DeviceReceiptJournal(receiptPath);
    var commandId=Guid.NewGuid();
    Check(first.TryClaim(commandId,out var original)&&original==null,
        "first remote request claims once");
    var restartedBeforeAck=new DeviceReceiptJournal(receiptPath);
    Check(!restartedBeforeAck.TryClaim(commandId,out var pending)&&
        pending is {Ok:null},
        "unconfirmed command does not reexecute after restart");
    first.Complete(commandId,true,"secret-password-token-should-not-be-persisted");
    Check(!System.IO.File.ReadAllText(receiptPath).Contains(
        "secret-password-token-should-not-be-persisted"),
        "receipt journal never persists private command output");
    var restartedAfterAck=new DeviceReceiptJournal(receiptPath);
    Check(!restartedAfterAck.TryClaim(commandId,out var completed) &&
        completed is {Ok:true,Detail:"Команда вже виконана; повтор не запускаю."},
        "successful command reuses original ACK after restart");
    restartedAfterAck.Complete(commandId,false,"false duplicate");
    var finalJournal=new DeviceReceiptJournal(receiptPath);
    Check(!finalJournal.TryClaim(commandId,out var finalReceipt)&&
        finalReceipt?.Ok==true,
        "later failures must not overwrite original success");
} finally {
    if(System.IO.File.Exists(receiptPath))
        System.IO.File.Delete(receiptPath);
}
foreach(var (phrase,expected) in new (string,string)[]{
    ("увімкни реп","реп"),
    ("включи rap","rap"),
    ("увімкни українське","українське"),
    ("включи украинское","украинское")
}) {
    var parsed=CommandRouter.Parse(phrase);
    Check(parsed.Kind==CommandKind.SpotifyPlaylist && parsed.Argument==expected,
        "saved playlist alias recognized on Windows: "+phrase);
    Check(DeviceRouting.EffectiveTarget(JarvisDevice.Watch,parsed.Kind)
        ==JarvisDevice.Watch,"saved playlist remains on Watch when selected");
}
foreach(var (speech,kind,value,name) in new (string,CommandKind,int,string)[]{
    ("натисни кнопку Пошук",CommandKind.ClickNamed,0,"Пошук"),
    ("натисни на кнопку Відтворити",CommandKind.ClickNamed,0,"Відтворити"),
    ("клікни Нова вкладка",CommandKind.ClickNamed,0,"Нова вкладка"),
    ("прокрути вниз",CommandKind.ScrollPage,1,""),
    ("свайпни вгору",CommandKind.ScrollPage,0,""),
    ("пролистай вверх",CommandKind.ScrollPage,0,"")
}) {
    var parsed=CommandRouter.Parse(speech);
    Check(parsed.Kind==kind,"Windows UIA command parse: "+speech);
    Check(parsed.Value==value,"scroll direction: "+speech);
    if(name.Length>0)Check(parsed.Argument==name,
        "UIA control name must preserve exact text: "+speech);
}
foreach(var phrase in new[]{"стан комп’ютера","статус пк","комп'ютер статус"})
    Check(CommandRouter.Parse(phrase).Kind==CommandKind.SystemStatus,"health parser: "+phrase);
foreach(var phrase in new[]{"що на екрані","проаналізуй екран"})
    Check(CommandRouter.Parse(phrase).Kind==CommandKind.AnalyzeScreen,"screen AI parser: "+phrase);
Check(DeviceRouting.IsSensitive(CommandKind.AnalyzeScreen),"screen AI is sensitive and local only");
foreach(var command in new[]{
    CommandKind.SystemStatus,CommandKind.Volume,CommandKind.OpenApp,
    CommandKind.MediaNext,CommandKind.Timer
}) Check(WindowsRemoteCommandPolicy.IsAllowed(command),
    "safe command allowed from Pixel/Watch7: "+command);
foreach(var command in new[]{
    CommandKind.AnalyzeScreen,CommandKind.ClickNamed,CommandKind.ScrollPage,
    CommandKind.TypeIntoActiveWindow,CommandKind.Wait,CommandKind.Ai,
    CommandKind.FinanceAdd,CommandKind.FinanceRemote,CommandKind.NoteDelete
}) Check(!WindowsRemoteCommandPolicy.IsAllowed(command),
    "unsafe action rejected by remote Windows: "+command);

Check(CommandRouter.Parse("пауза 2 секунди").Kind==CommandKind.Wait &&
    CommandRouter.Parse("пауза 2 секунди").Value==2,"bounded macro pause");
Check(CommandRouter.Parse("пауза 11 секунд").Kind!=CommandKind.Wait,
    "reject unbounded macro delay");
Check(MacroEngine.New("робочий режим","відкрий блокнот\nпауза 2 секунди\nгучність 35").Actions.Count==3,
    "macro allows bounded wait between actions");
Check(DeviceRouting.EffectiveTarget(JarvisDevice.Computer,CommandKind.ClickNamed)
    ==JarvisDevice.Computer,"UI automation stays local on PC");
Check(WindowsFileExchange.MaxFileBytes==1048576,"file exchange explicit 1 MiB limit");
if(!OperatingSystem.IsWindows()) {
    try {
        await WindowsSystemMonitor.SnapshotAsync(CancellationToken.None);
        throw new Exception("system monitor should be Windows-only");
    }catch(PlatformNotSupportedException) { assertions++; }
}

var v2=MacroV2Engine.New("Робота","робота","09:30",
    "день=пн,вт\nінтернет=так",
    "якщо інтернет=так\nгучність 20\nінакше\nгучність 30\nкінець\nповтори 2\nскажи перевірка\nкінець");
Check(MacroV2Engine.Compile(v2.Steps).Count==2,"v2 conditional and repeat compilation");
Check(MacroV2Engine.EvaluateCondition("інтернет=так",DateTimeOffset.Now,true,"notepad"),
      "v2 internet condition true");
Check(!MacroV2Engine.EvaluateCondition("інтернет=так",DateTimeOffset.Now,false,"notepad"),
      "v2 internet condition false");
Check(MacroV2Engine.EvaluateCondition("процес=notepad",DateTimeOffset.Now,true,"notepad"),
      "v2 process exact match");
var performed=new List<ParsedCommand>();
await MacroV2Engine.RunAsync(v2,(cmd,deadline)=>{deadline.ThrowIfCancellationRequested();performed.Add(cmd);return Task.CompletedTask;},
    condition=>condition=="інтернет=так",CancellationToken.None);
Check(performed.Count==3 && performed[0].Kind==CommandKind.Volume &&
      performed[0].Value==20 && performed[1].Kind==CommandKind.Speak,
      "v2 executes only selected condition plus two repeats");
try {MacroV2Engine.New("Тест","тест","","",
    "повтори 5\nповтори 5\nповтори 5\nскажи ок\nкінець\nкінець\nкінець");
     throw new Exception("unbounded v2 macro accepted");}
catch(ArgumentException){assertions++;}
Check(!DeviceTrustPolicy.IsValid(new DeviceCommandV2(2,Guid.NewGuid(),
    "watch","computer","windows.lock",new(),DateTimeOffset.UtcNow,
    DateTimeOffset.UtcNow.AddMinutes(1),false,"watch"),DateTimeOffset.UtcNow),
    "v2 remote lock requires approval");

var clip=ClipboardBridge.Create("computer","Український текст",DateTimeOffset.UtcNow);
Check(ClipboardBridge.Accept(clip,DateTimeOffset.UtcNow,"phone",null),"explicit clipboard accepted");
Check(!ClipboardBridge.Accept(clip,DateTimeOffset.UtcNow,"phone",clip.Hash),"clipboard loop prevented");
Check(!ClipboardBridge.Accept(clip,DateTimeOffset.UtcNow.AddSeconds(60),"phone",null),"clipboard TTL");
try{ClipboardBridge.Create("computer","sk-proj-secret",DateTimeOffset.UtcNow);
    throw new Exception("clipboard secret permitted");}
catch(InvalidOperationException){assertions++;}
var sampleDirectory=Path.Combine(Path.GetTempPath(),"jarvis-chunks-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sampleDirectory);
try{
    var sourcePath=Path.Combine(sampleDirectory,"source.bin");
    var encryptedFolder=Path.Combine(sampleDirectory,"enc");
    var restoredPath=Path.Combine(sampleDirectory,"received.bin");
    var sourceData=new byte[LargeFileTransferService.ChunkBytes*2+27];
    System.Security.Cryptography.RandomNumberGenerator.Fill(sourceData);
    await File.WriteAllBytesAsync(sourcePath,sourceData);
    var key=new byte[32];
    System.Security.Cryptography.RandomNumberGenerator.Fill(key);
    var manifest=await LargeFileTransferService.EncryptToDirectoryAsync(
        sourcePath,encryptedFolder,key,null,CancellationToken.None);
    Check(manifest.Chunks==3 && manifest.TotalBytes==sourceData.LongLength,
        "large-file streaming creates correct chunk count");
    await LargeFileTransferService.RestoreAsync(manifest,encryptedFolder,
        restoredPath,key,null,CancellationToken.None);
    Check(File.ReadAllBytes(restoredPath).SequenceEqual(sourceData),
        "large-file AEAD restore passes byte-perfect comparison");
    var wrongKey=new byte[32];
    System.Security.Cryptography.RandomNumberGenerator.Fill(wrongKey);
    var tamperedDestination=Path.Combine(sampleDirectory,"should-not-exist.bin");
    try{
        await LargeFileTransferService.RestoreAsync(manifest,encryptedFolder,
            tamperedDestination,wrongKey,null,CancellationToken.None);
        throw new Exception("wrong AEAD key accepted");
    }catch(System.Security.Cryptography.CryptographicException){assertions++;}
    Check(!File.Exists(tamperedDestination),"failed decrypt cannot leave output");
    var chunkPath=Path.Combine(encryptedFolder,manifest.TransferId.ToString("N")+".00001.jfc");
    var part=File.ReadAllBytes(chunkPath);
    part[^1]^=0x10;
    await File.WriteAllBytesAsync(chunkPath,part);
    var tampered=Path.Combine(sampleDirectory,"tamper.bin");
    try{
        await LargeFileTransferService.RestoreAsync(manifest,encryptedFolder,
            tampered,key,null,CancellationToken.None);
        throw new Exception("corrupted transfer accepted");
    }catch(System.Security.Cryptography.CryptographicException){assertions++;}
    Check(!File.Exists(tampered),"tampered output deleted");
}finally{Directory.Delete(sampleDirectory,true);}

using(var pairingKey=System.Security.Cryptography.ECDsa.Create(
    System.Security.Cryptography.ECCurve.NamedCurves.nistP256)) {
    var raw="""{"pairingId":"00000000-0000-0000-0000-000000000000"}""";
    var canonical=DevicePairingService.CanonicalRequest("POST",
        "/device/v2/pair/pending","computer","1700000000000",
        "c1102abec5f138f973ea891300000000",raw);
    var signed=DevicePairingService.Sign(pairingKey,canonical);
    var valid=pairingKey.VerifyData(System.Text.Encoding.UTF8.GetBytes(canonical),
        Convert.FromBase64String(signed),
        System.Security.Cryptography.HashAlgorithmName.SHA256,
        System.Security.Cryptography.DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    Check(valid,"Windows P256 signature matches expected Worker P1363 format");
    Check(canonical.Split('\n').Length==6,
        "canonical P0 signature has six ordered fields");
}
var signedPolicy=new JarvisSettings {EnableDeviceRelay=true,EnableSignedRelay=true};
Check(SignedDeviceLocalPolicy.IsAllowed(signedPolicy,"phone","system.status",false),
    "v2 local policy read-only default");
foreach(var action in new[]{"media.next","volume.set","application.open",
    "windows.lock","monitor.off","macro.run","shell.run"}) {
    Check(!SignedDeviceLocalPolicy.IsAllowed(signedPolicy,"phone",action,true),
        "v2 remote scope denied until explicit local grant: "+action);
}
signedPolicy.SignedMedia=true;
Check(SignedDeviceLocalPolicy.IsAllowed(signedPolicy,"phone","media.next",false),
    "v2 media granted locally");
Check(!SignedDeviceLocalPolicy.IsAllowed(signedPolicy,"watch","media.next",false),
    "v2 Watch delegation separately denied");
signedPolicy.SignedWatchDelegation=true;
Check(SignedDeviceLocalPolicy.IsAllowed(signedPolicy,"watch","media.next",false),
    "v2 Watch delegation explicitly granted");
signedPolicy.SignedLocks=true;
Check(!SignedDeviceLocalPolicy.IsAllowed(signedPolicy,"phone","windows.lock",false),
    "v2 signed lock still needs Windows approval");
Check(SignedDeviceLocalPolicy.IsAllowed(signedPolicy,"phone","windows.lock",true),
    "v2 signed lock requires BOTH grant and approval flag");
signedPolicy.EnableSignedRelay=false;
Check(!SignedDeviceLocalPolicy.IsAllowed(signedPolicy,"phone","system.status",false),
    "v2 locally disabled blocks read-only too");

var nowMacro=DateTimeOffset.UtcNow;
var eventMacro=MacroV2Engine.New("Мережева подія","мережевий тест","","",
    "гучність 20","network.online",120);
Check(MacroV2Engine.EventDue(eventMacro,"network.online",nowMacro),
    "v2 event trigger fires once when first enabled");
Check(!MacroV2Engine.EventDue(eventMacro,"network.offline",nowMacro),
    "v2 unrelated network event does not fire");
eventMacro.LastRun=nowMacro;
Check(!MacroV2Engine.EventDue(eventMacro,"network.online",nowMacro.AddSeconds(40)),
    "v2 event cooldown blocks repeat");
Check(MacroV2Engine.EventDue(eventMacro,"network.online",nowMacro.AddSeconds(121)),
    "v2 event cooldown allows next event");
Check(!MacroV2Engine.CooldownElapsed(eventMacro,nowMacro.AddSeconds(-1)),
    "v2 clock rollback fails closed");
try {
    MacroV2Engine.New("Мережева подія","тест","","","гучність 20","network.unknown");
    throw new Exception("unknown macro event accepted");
}catch(ArgumentException){assertions++;}
try {
    MacroV2Engine.New("Мережева подія","тест","","","гучність 20","network.online",-1);
    throw new Exception("negative macro cooldown accepted");
}catch(ArgumentException){assertions++;}

var appMacro=MacroV2Engine.New("Блокнот активний","блокнот","","",
    "гучність 20","APP.ACTIVE:Notepad",120);
Check(appMacro.EventTrigger=="app.active:notepad",
    "v2 app process event normalized to lowercase");
Check(MacroV2Engine.EventDue(appMacro,"app.active:notepad",DateTimeOffset.UtcNow),
    "v2 app activation event matches exact process");
Check(!MacroV2Engine.EventDue(appMacro,"app.active:calc",DateTimeOffset.UtcNow),
    "v2 app activation does not match neighboring process");
try {
    MacroV2Engine.New("Злочинний тригер","хибна команда","","",
        "гучність 20","app.active:../../powershell");
    throw new Exception("v2 untrusted app path accepted");
} catch(ArgumentException){assertions++;}
try {
    MacroV2Engine.New("Злочинний тригер","хибна команда","","",
        "гучність 20","app.active:");
    throw new Exception("v2 blank app name accepted");
} catch(ArgumentException){assertions++;}


var foregroundGate=new MacroForegroundTransition();
foregroundGate.Baseline("chrome");
Check(!foregroundGate.TryObserve("chrome",out _),
    "foreground process unchanged must not trigger");
Check(foregroundGate.TryObserve("jarvis.windows",out var ownAppEvent)
      && ownAppEvent=="app.active:jarvis.windows",
    "switching into Jarvis updates foreground baseline");
Check(foregroundGate.TryObserve("chrome",out var resumedAppEvent)
      && resumedAppEvent=="app.active:chrome",
    "switching back to same app after Jarvis triggers again");
Check(!foregroundGate.TryObserve("chrome",out _),
    "same foreground app must not launch macro twice");
Check(!foregroundGate.TryObserve("../../cmd",out _),
    "unsafe process identifier rejected");
foregroundGate.Baseline("chrome");
Check(!foregroundGate.TryObserve(null,out _),
    "unreadable or protected foreground is not executed");
Check(foregroundGate.TryObserve("chrome",out _),
    "switch back after an unknown foreground is recognized");
foregroundGate.Reset();
Check(foregroundGate.TryObserve("notepad",out _),
    "foreground reset permits next explicit transition");

foreach(var template in MacroV2Engine.Templates) {
    var preview=MacroV2Engine.New(template.Name,template.Trigger,
        template.Schedule,template.Conditions,template.Steps,
        template.EventTrigger,template.CooldownSeconds);
    Check(preview.Name==template.Name && preview.Steps.Count>0,
        "macro v2 preview-only template validates: "+template.Name);
}
Check(MacroV2Engine.Templates.Count>=5,
    "macro v2 templates cover work, music, meeting, sleep and network");

var cancelledMacro=MacroV2Engine.New("Скасування","тест","","","гучність 20");
using(var stopMacro=new CancellationTokenSource()) {
    var actionStarted=false;
    try {
        await MacroV2Engine.RunAsync(cancelledMacro,async (cmd,deadline)=>{
            actionStarted=true;
            stopMacro.Cancel();
            await Task.Delay(TimeSpan.FromSeconds(5),deadline);
        },_=>true,stopMacro.Token);
        throw new Exception("Macro action ignored cancellation.");
    }catch(OperationCanceledException) {
        Check(actionStarted,"macro timeout token reaches in-flight action");
    }
}

var processMacro=MacroV2Engine.New("Старт Блокнота","блокнот запущено","","",
    "гучність 20","app.start:notepad",120);
Check(MacroV2Engine.EventDue(processMacro,"app.start:notepad",DateTimeOffset.UtcNow),
    "process-start trigger executes only matching process");
Check(!MacroV2Engine.EventDue(processMacro,"app.active:notepad",DateTimeOffset.UtcNow),
    "app-start and app-active are different triggers");
Check(MacroProcessStartService.TryEventName("Notepad.EXE",out var procEvent)
    && procEvent=="app.start:notepad",
    "Win32_ProcessStartTrace name normalization");
Check(!MacroProcessStartService.TryEventName(@"C:\Windows\cmd.exe",out _),
    "process-start event forbids full paths");
Check(!MacroProcessStartService.TryEventName("jarvis.windows.exe",out _),
    "process-start events do not recursively trigger JARVIS");
Check(!MacroProcessStartService.TryEventName("powershell.exe -enc abc",out _),
    "process-start event disallows arguments");
Check(!MacroProcessStartService.TryEventName(null,out _),
    "process-start event rejects missing WMI name");
try{
    MacroV2Engine.New("Не дозволено","команда","","",
        "гучність 20","app.start:../../cmd");
    throw new Exception("unsafe process-start trigger accepted");
}catch(ArgumentException){assertions++;}


Check(MacroUsbEventName.TryParse("device.attach:VID_046D&PID_C52B",
      out var usbHardware,out var usbAttachFlag)
      && usbHardware=="vid_046d&pid_c52b" && usbAttachFlag,
      "USB VID/PID event parser canonicalizes case");
Check(MacroUsbEventName.TryFromInterfacePath(
      @"\\?\USB#VID_046D&PID_C52B#product",true,out var usbAttach)
      && usbAttach=="device.attach:vid_046d&pid_c52b",
      "USB arrival derives bounded, canonical event from device interface");
Check(MacroUsbEventName.TryFromInterfacePath(
      @"\\?\USB#VID_046D&PID_C52B#product",false,out var usbDetach)
      && usbDetach=="device.detach:vid_046d&pid_c52b",
      "USB removal produces a different event");
Check(!MacroUsbEventName.TryParse("device.attach:../../cmd",out _,out _),
      "USB trigger path traversal rejected");
Check(!MacroUsbEventName.TryFromInterfacePath(
      @"\\?\USB#SERIAL_ABC123#private",true,out _),
      "unrecognized USB path has no event");
var usbMacro=MacroV2Engine.New("USB пристрій","USB увімкнений","","",
    "гучність 20","device.attach:vid_046d&pid_c52b");
Check(MacroV2Engine.EventDue(usbMacro,usbAttach,DateTimeOffset.UtcNow),
      "USB event triggers only matching enabled macro");
Check(!MacroV2Engine.EventDue(usbMacro,usbDetach,DateTimeOffset.UtcNow),
      "USB removal does not trigger insertion macro");
var loginNow=DateTimeOffset.UtcNow;
var loginMacro=MacroV2Engine.New("Старт Windows","після входу","","",
    "гучність 30","jarvis.login",0);
Check(MacroV2Engine.EventDue(loginMacro,"jarvis.login",loginNow),
    "explicit login trigger is enabled on first launch");
loginMacro.LastRun=loginNow;
Check(!MacroV2Engine.EventDue(loginMacro,"jarvis.login",loginNow.AddSeconds(15)),
    "login trigger cannot duplicate on rapid process restart even when cooldown 0");
Check(MacroV2Engine.EventDue(loginMacro,"jarvis.login",loginNow.AddSeconds(65)),
    "login trigger can fire after one-minute safety interval");
Check(!MacroV2Engine.EventDue(loginMacro,"network.online",loginNow.AddSeconds(65)),
    "login does not react to network events");
var powerGate=new WindowsPowerTransitionPolicy();
Check(!powerGate.IsSuspended,"startup begins active");
Check(!powerGate.Resume(),"unexpected resume ignored");
Check(powerGate.Suspend() && powerGate.IsSuspended,"first suspend blocks operations");
Check(!powerGate.Suspend(),"duplicate suspend must not revoke twice");
Check(powerGate.Resume() && !powerGate.IsSuspended,"resume restores passive listeners");
Check(!powerGate.Resume(),"duplicate resume ignored");
var auditDir=Path.Combine(Path.GetTempPath(),"jarvis-audit-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(auditDir);
try {
    var auditFile=Path.Combine(auditDir,"audit.json");
    var auditNow=DateTimeOffset.UtcNow;
    var audit=new DeviceAuditJournal(auditFile,()=>auditNow,3);
    var first=Guid.NewGuid();
    audit.Record(first,"watch","media.next","completed",123);
    audit.Record(first,"watch","media.next","completed",123);
    Check(audit.Recent().Count==1,"audit deduplicates re-delivery");
    try {
        audit.Record(Guid.NewGuid(),"phone","media.next\\nsecret","failed",20);
        throw new Exception("audit permitted injection");
    }catch(ArgumentException){assertions++;}
    audit.Record(Guid.NewGuid(),"phone","system.status","failed",999,"DEVICE_REVOKED");
    audit.Record(Guid.NewGuid(),"watch","volume.set","unknown",250,"RESULT_UNKNOWN");
    audit.Record(Guid.NewGuid(),"phone","media.toggle","completed",100);
    Check(audit.Recent().Count==3,"audit bounded capacity");
    Check(audit.Recent()[0].ActionType=="media.toggle","audit most recent first");
    var exported=Path.Combine(auditDir,"export.json");
    audit.Export(exported);
    var auditData=File.ReadAllText(exported);
    Check(auditData.Contains("DEVICE_REVOKED") &&
        !auditData.Contains("secret") && !auditData.Contains("clipboard"),
        "audit export contains status only, not sensitive input");
    auditNow=auditNow.AddDays(8);
    Check(audit.Recent().Count==0,"audit retention expires old records");
} finally {Directory.Delete(auditDir,true);}
Console.WriteLine($"PASS JARVIS Windows pure contracts: {assertions} assertions.");
