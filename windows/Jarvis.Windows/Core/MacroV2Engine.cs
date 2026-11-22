using System.Globalization;
namespace Jarvis.Windows.Core;

public sealed class MacroDefinitionV2 {
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Trigger { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string ScheduleLocal { get; set; } = ""; // HH:mm in Windows timezone, empty=voice only
    public string EventTrigger { get; set; } = ""; // opt-in network.online/offline or app.active:process
    public List<string> Conditions { get; set; } = new();
    public List<string> Steps { get; set; } = new();
    public DateTimeOffset? LastRun { get; set; }
    public int CooldownSeconds { get; set; } = 60;
    // Disabled until authenticated pairing is implemented. Legacy bearer alone
    // never grants remote access to the richer macro runner.
    public bool AllowRemote { get; set; }
}
/// <summary>Preview-only templates: cannot run until saved manually.</summary>
public sealed record MacroTemplateV2(string Name,string Trigger,string Schedule,
    string EventTrigger,int CooldownSeconds,string Conditions,string Steps);

public abstract record MacroNodeV2;
public sealed record MacroActionV2(ParsedCommand Command):MacroNodeV2;
public sealed record MacroIfV2(string Condition,List<MacroNodeV2> Then,List<MacroNodeV2> Else):MacroNodeV2;
public sealed record MacroRepeatV2(int Count,List<MacroNodeV2> Body):MacroNodeV2;

/// <summary>Bounded, typed, user-authored macros. No shell, AI code or recursion.</summary>
public static class MacroV2Engine {
    public static readonly IReadOnlyList<MacroTemplateV2> Templates=
        new List<MacroTemplateV2> {
            new("Робочий режим","робота","","",60,"",
                "відкрий блокнот\nгучність 40\nскажи починаю роботу"),
            new("Музика","музика на пк","","",90,"",
                "відкрий спотіфай\nгучність 45"),
            new("Зустріч","зустріч","","",120,"",
                "гучність 10\nскажи тихий режим"),
            new("Перед сном","перед сном","","",180,"",
                "гучність 15\nскажи час відпочивати"),
            new("Мережа відновлена","відновлення зв'язку","","network.online",600,"",
                "скажи інтернет знову доступний")
        }.AsReadOnly();

    private static readonly HashSet<CommandKind> Allowed=new() {
        CommandKind.OpenApp,CommandKind.OpenSettings,CommandKind.Volume,
        CommandKind.MediaToggle,CommandKind.MediaNext,CommandKind.MediaPrevious,
        CommandKind.Wait,CommandKind.Speak,CommandKind.Timer
    };
    public static MacroDefinitionV2 New(string name,string trigger,string schedule,
            string conditionLines,string actionLines,string eventTrigger="",int cooldownSeconds=60) {
        var m=new MacroDefinitionV2 {
            Name=name.Trim(),Trigger=trigger.Trim(),ScheduleLocal=schedule.Trim(),
            EventTrigger=eventTrigger.Trim().ToLowerInvariant(),CooldownSeconds=cooldownSeconds,
            Conditions=conditionLines.Split(new[]{'\r','\n'},
                StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).ToList(),
            Steps=actionLines.Split(new[]{'\r','\n'},
                StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).ToList()
        };
        Validate(m);
        return m;
    }
    public static void Validate(MacroDefinitionV2 macro) {
        if(macro.Name.Length is <2 or >90 || macro.Trigger.Length is <2 or >100 ||
           macro.Steps.Count is <1 or >40 || macro.Conditions.Count>8 ||
           macro.CooldownSeconds is <0 or >86400)
            throw new ArgumentException("Некоректна назва, тригер, умови або кроки макросу v2.");
        if(macro.ScheduleLocal.Length>0 && !TimeOnly.TryParseExact(
                macro.ScheduleLocal,"HH:mm",CultureInfo.InvariantCulture,
                DateTimeStyles.None,out _))
            throw new ArgumentException("Розклад: HH:mm або порожнє поле.");
        if(macro.EventTrigger is not ("" or "network.online" or "network.offline" or "jarvis.login") &&
           !System.Text.RegularExpressions.Regex.IsMatch(macro.EventTrigger,
              @"^app\.(?:active|start):[a-z0-9._-]{1,50}$") &&
           !MacroUsbEventName.TryParse(macro.EventTrigger,out _,out _))
            throw new ArgumentException(
                "Подія: network.online, network.offline або app.active:ім'я_процесу.");
        foreach(var condition in macro.Conditions)ValidateCondition(condition);
        var root=Compile(macro.Steps);
        if(ExpandedCount(root)>30)throw new ArgumentException("Макрос має максимум 30 фактичних дій.");
    }
    public static void ValidateCondition(string value) {
        var s=value.Trim();
        if(s is "інтернет=так" or "інтернет=ні")return;
        if(s.StartsWith("день=",StringComparison.OrdinalIgnoreCase) &&
           s[5..].Split(',').All(x=>new[]{
               "пн","вт","ср","чт","пт","сб","нд"
           }.Contains(x.Trim().ToLowerInvariant())))return;
        if(s.StartsWith("час=",StringComparison.OrdinalIgnoreCase)) {
            var parts=s[4..].Split('-');
            if(parts.Length==2 && parts.All(x=>TimeOnly.TryParseExact(
               x,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _)))return;
        }
        if(s.StartsWith("процес=",StringComparison.OrdinalIgnoreCase) &&
           System.Text.RegularExpressions.Regex.IsMatch(s[7..],@"^[a-zA-Z0-9._-]{1,50}$"))return;
        throw new ArgumentException("Непідтримувана умова v2: "+value);
    }
    public static bool EvaluateCondition(string condition,DateTimeOffset now,
            bool internet,string? activeProcess) {
        ValidateCondition(condition);
        var s=condition.Trim().ToLowerInvariant();
        if(s=="інтернет=так")return internet;
        if(s=="інтернет=ні")return !internet;
        if(s.StartsWith("процес="))return string.Equals(activeProcess,s[7..],
            StringComparison.OrdinalIgnoreCase);
        if(s.StartsWith("день=")) {
            var current=now.DayOfWeek switch {
                DayOfWeek.Monday=>"пн",DayOfWeek.Tuesday=>"вт",
                DayOfWeek.Wednesday=>"ср",DayOfWeek.Thursday=>"чт",
                DayOfWeek.Friday=>"пт",DayOfWeek.Saturday=>"сб",_=>"нд"
            };
            return s[5..].Split(',').Select(x=>x.Trim()).Contains(current);
        }
        var range=s[4..].Split('-');
        var start=TimeOnly.ParseExact(range[0],"HH:mm",CultureInfo.InvariantCulture);
        var end=TimeOnly.ParseExact(range[1],"HH:mm",CultureInfo.InvariantCulture);
        var currentTime=TimeOnly.FromDateTime(now.LocalDateTime);
        return start<=end ? currentTime>=start && currentTime<=end
            : currentTime>=start || currentTime<=end;
    }
    public static MacroDefinitionV2? Match(JarvisState state,string spoken,bool remote=false) {
        if(remote)return null; // v2 remote disabled until per-device pairing
        var key=CommandRouter.StripWake(spoken);
        if(key.StartsWith("запусти макрос ",StringComparison.OrdinalIgnoreCase))
            key=key[14..].Trim();
        return state.MacrosV2.FirstOrDefault(m=>m.Enabled &&
            (m.Trigger.Equals(key,StringComparison.OrdinalIgnoreCase) ||
             m.Name.Equals(key,StringComparison.OrdinalIgnoreCase)));
    }
    /** Fail closed after restart, clock rollback or closely spaced events.
     * LastRun is persisted before side effects to prevent accidental replay.
     */
    public static bool CooldownElapsed(MacroDefinitionV2 macro,DateTimeOffset now) {
        if(!macro.Enabled)return false;
        if(!macro.LastRun.HasValue)return true;
        var elapsed=now-macro.LastRun.Value;
        return elapsed>=TimeSpan.Zero &&
            elapsed>=TimeSpan.FromSeconds(macro.CooldownSeconds);
    }
    public static bool EventDue(MacroDefinitionV2 macro,string eventName,
            DateTimeOffset now) =>
        macro.Enabled &&
        macro.EventTrigger.Length>0 &&
        macro.EventTrigger.Equals(eventName,StringComparison.OrdinalIgnoreCase) &&
        CooldownElapsed(macro,now) &&
        (eventName!="jarvis.login" ||
         !macro.LastRun.HasValue ||
         now-macro.LastRun.Value>=TimeSpan.FromMinutes(1));

    public static bool ScheduleDue(MacroDefinitionV2 m,DateTimeOffset now) {
        if(!m.Enabled || m.ScheduleLocal.Length==0)return false;
        if(!TimeOnly.TryParseExact(m.ScheduleLocal,"HH:mm",CultureInfo.InvariantCulture,
           DateTimeStyles.None,out var requested))return false;
        var actual=TimeOnly.FromDateTime(now.LocalDateTime);
        var elapsed=actual.ToTimeSpan()-requested.ToTimeSpan();
        if(elapsed<TimeSpan.Zero||elapsed>=TimeSpan.FromMinutes(2))return false;
        return !m.LastRun.HasValue || m.LastRun.Value.ToLocalTime().Date!=now.ToLocalTime().Date;
    }
    public static List<MacroNodeV2> Compile(IReadOnlyList<string> lines) {
        var position=0;
        var nodes=ReadBlock(lines,ref position,0);
        if(position!=lines.Count)throw new ArgumentException("Зайве «кінець» або «інакше».");
        return nodes;
    }
    private static List<MacroNodeV2> ReadBlock(IReadOnlyList<string> lines,
            ref int index,int depth) {
        if(depth>3)throw new ArgumentException("Занадто глибока вкладеність.");
        var output=new List<MacroNodeV2>();
        while(index<lines.Count) {
            var line=lines[index].Trim();
            if(line=="кінець"||line=="інакше")break;
            if(line.StartsWith("якщо ",StringComparison.OrdinalIgnoreCase)) {
                var condition=line[5..].Trim();ValidateCondition(condition);
                index++;
                var yes=ReadBlock(lines,ref index,depth+1);
                var no=new List<MacroNodeV2>();
                if(index<lines.Count && lines[index].Trim()=="інакше") {
                    index++;no=ReadBlock(lines,ref index,depth+1);
                }
                if(index>=lines.Count || lines[index].Trim()!="кінець")
                    throw new ArgumentException("Після «якщо» потрібне «кінець».");
                index++;
                output.Add(new MacroIfV2(condition,yes,no));
            } else if(line.StartsWith("повтори ",StringComparison.OrdinalIgnoreCase)) {
                if(!int.TryParse(line[8..].Trim(),out var count)||count is <1 or >5)
                    throw new ArgumentException("Повтори: від 1 до 5.");
                index++;
                var body=ReadBlock(lines,ref index,depth+1);
                if(index>=lines.Count || lines[index].Trim()!="кінець")
                    throw new ArgumentException("Після «повтори» потрібне «кінець».");
                index++;
                output.Add(new MacroRepeatV2(count,body));
            } else {
                if(line.Length>200)throw new ArgumentException("Дія занадто довга.");
                var action=CommandRouter.Parse(line);
                if(!Allowed.Contains(action.Kind))
                    throw new ArgumentException("Заборонена дія макросу v2: "+line);
                output.Add(new MacroActionV2(action));index++;
            }
        }
        return output;
    }
    private static int ExpandedCount(List<MacroNodeV2> nodes) =>
        nodes.Sum(n=>n switch {
            MacroActionV2=>1,
            MacroIfV2 i=>Math.Max(ExpandedCount(i.Then),ExpandedCount(i.Else)),
            MacroRepeatV2 r=>r.Count*ExpandedCount(r.Body),
            _=>0
        });
    public static async Task RunAsync(MacroDefinitionV2 m,
        Func<ParsedCommand,CancellationToken,Task> perform,Func<string,bool> matches,
        CancellationToken cancel) {
        Validate(m);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var nodes=Compile(m.Steps);
        var steps=0;
        async Task Walk(IEnumerable<MacroNodeV2> block) {
            foreach(var node in block) {
                deadline.Token.ThrowIfCancellationRequested();
                switch(node) {
                    case MacroActionV2 a:
                        if(++steps>30)throw new InvalidOperationException("Ліміт дій вичерпано.");
                        if(a.Command.Kind==CommandKind.Wait)
                            await Task.Delay(TimeSpan.FromSeconds(a.Command.Value),deadline.Token);
                        else {
                            // Pass the macro's own two-minute deadline INTO the
                            // action. An action cannot ignore the expiry while
                            // waiting for API/UI/file operations.
                            await perform(a.Command,deadline.Token);
                            deadline.Token.ThrowIfCancellationRequested();
                        }
                        break;
                    case MacroIfV2 i: await Walk(matches(i.Condition)?i.Then:i.Else);break;
                    case MacroRepeatV2 repeat:
                        for(int j=0;j<repeat.Count;j++)await Walk(repeat.Body);
                        break;
                }
            }
        }
        await Walk(nodes);
    }
}
