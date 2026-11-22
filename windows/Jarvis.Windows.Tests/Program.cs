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
    first.Complete(commandId,true,"команду виконано");
    var restartedAfterAck=new DeviceReceiptJournal(receiptPath);
    Check(!restartedAfterAck.TryClaim(commandId,out var completed) &&
        completed is {Ok:true,Detail:"команду виконано"},
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
Console.WriteLine($"PASS JARVIS Windows pure contracts: {assertions} assertions.");
