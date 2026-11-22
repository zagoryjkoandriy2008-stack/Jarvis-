using System.Text.RegularExpressions;
namespace Jarvis.Windows.Core;

public enum JarvisDevice { Computer, Phone, Watch }
public static class DeviceRouting {
    public static string Label(JarvisDevice device) => device switch {
        JarvisDevice.Phone => "📱 Телефон",
        JarvisDevice.Watch => "⌚ Годинник",
        _ => "🖥 Комп'ютер"
    };
    public static string WireId(JarvisDevice device) => device switch {
        JarvisDevice.Phone=>"phone",JarvisDevice.Watch=>"watch",_=>"computer"
    };
    public static JarvisDevice? TargetChange(string? utterance) {
        if(string.IsNullOrWhiteSpace(utterance))return null;
        var normalized=utterance.Trim().ToLowerInvariant();
        normalized=Regex.Replace(normalized,@"[.,!?;:—]+$","").Trim();
        normalized=Regex.Replace(normalized,@"^(?:(?:джарвіс|джарвис|jarvis|дімон|димон|dimon)\s+)", "");
        normalized=Regex.Replace(normalized,@"\s+"," ");
        return normalized switch {
            "телефон" or "режим телефон" or "phone" => JarvisDevice.Phone,
            "годинник" or "режим годинник" or "watch" => JarvisDevice.Watch,
            "комп'ютер" or "комп’ютер" or "компютер" or "комп'ютор" or
            "компьютер" or "режим комп'ютер" or "computer" or "пк" => JarvisDevice.Computer,
            _=>null
        };
    }
    /** The phone owns the single authoritative finance journal. */
    public static JarvisDevice EffectiveTarget(JarvisDevice selected,
        CommandKind kind) => kind is
        CommandKind.FinanceAdd or CommandKind.FinanceReport or CommandKind.FinanceRemote or
        CommandKind.NoteAdd or CommandKind.NoteRead or CommandKind.NoteDelete
        ? JarvisDevice.Phone : selected;
    public static bool IsSensitive(CommandKind kind) => kind is
        CommandKind.TypeIntoActiveWindow or CommandKind.AnalyzeScreen;
}
