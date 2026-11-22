namespace Jarvis.Windows.Core;

/** Explicit local macros. No PowerShell, shell strings, AI code, or recursion. */
public static class MacroEngine {
    private static readonly HashSet<CommandKind> Allowed = new() {
        CommandKind.OpenApp, CommandKind.OpenSettings, CommandKind.WebSearch,
        CommandKind.Volume, CommandKind.Speak, CommandKind.MediaToggle,
        CommandKind.MediaNext, CommandKind.MediaPrevious, CommandKind.Timer
    };
    public static MacroDefinition New(string trigger,string actionLines) {
        var steps=actionLines.Split(new[]{'\r','\n'},
            StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(trigger.Trim().Length is <2 or >100 || steps.Length is <1 or >8)
            throw new ArgumentException("Тригер: 2–100 символів; дії: 1–8.");
        foreach(var step in steps) {
            if(step.Length>200 || !Allowed.Contains(CommandRouter.Parse(step).Kind))
                throw new ArgumentException("Недопустима дія: "+step+
                    ". Макрос підтримує лише безпечні локальні команди.");
        }
        return new MacroDefinition {Trigger=trigger.Trim(),Actions=steps.ToList()};
    }
    public static MacroDefinition? Match(JarvisState state,string utterance) {
        var key=CommandRouter.StripWake(utterance);
        return state.Macros.FirstOrDefault(m=>m.Enabled &&
            m.Trigger.Equals(key,StringComparison.OrdinalIgnoreCase));
    }
    public static async Task RunAsync(MacroDefinition macro,
            Func<ParsedCommand,Task> execute,CancellationToken cancellationToken) {
        if(macro.Actions.Count is <1 or >8)
            throw new InvalidOperationException("Некоректна кількість дій макросу.");
        foreach(var line in macro.Actions) {
            cancellationToken.ThrowIfCancellationRequested();
            var command=CommandRouter.Parse(line);
            if(!Allowed.Contains(command.Kind))
                throw new InvalidOperationException("Дія макросу заборонена.");
            await execute(command);
            await Task.Delay(250,cancellationToken);
        }
    }
}
