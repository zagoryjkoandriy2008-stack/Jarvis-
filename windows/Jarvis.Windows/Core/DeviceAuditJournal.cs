using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jarvis.Windows.Core;

/// <summary>Privacy-preserving bounded audit of cross-device execution.
/// Never stores spoken text, notes, screenshots, clipboard, files or API keys.
/// </summary>
public sealed record DeviceAuditEntry(DateTimeOffset Timestamp,Guid RequestId,
    string Origin,string ActionType,string Status,long DurationMs,string ErrorCode);

public sealed class DeviceAuditJournal {
    private readonly object _gate=new();
    private readonly string _path;
    private readonly Func<DateTimeOffset> _clock;
    private readonly int _capacity;
    private static readonly TimeSpan Retention=TimeSpan.FromDays(7);
    private static readonly Regex ActionPattern=new(
        @"^[a-z][a-z0-9.]{0,39}$",RegexOptions.CultureInvariant);
    private static readonly Regex CodePattern=new(
        @"^[A-Z][A-Z0-9_]{0,39}$",RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Statuses=new(
        StringComparer.Ordinal){"accepted","running","completed","failed","unknown"};
    private static readonly HashSet<string> Origins=new(
        StringComparer.Ordinal){"phone","watch","computer"};
    public DeviceAuditJournal(string? file=null,Func<DateTimeOffset>? clock=null,
            int capacity=512) {
        if(capacity is <1 or >4096)throw new ArgumentOutOfRangeException(nameof(capacity));
        _path=file??Path.Combine(StateStore.DirectoryPath,"devices-audit.json");
        _clock=clock??(()=>DateTimeOffset.UtcNow);
        _capacity=capacity;
    }
    private List<DeviceAuditEntry> ReadUnsafe() {
        if(!File.Exists(_path))return new();
        var raw=File.ReadAllText(_path);
        var data=JsonSerializer.Deserialize<List<DeviceAuditEntry>>(raw);
        if(data==null)throw new InvalidDataException("Audit log JSON invalid.");
        return data;
    }
    private List<DeviceAuditEntry> Retain(IEnumerable<DeviceAuditEntry> input) {
        var cutoff=_clock()-Retention;
        return input.Where(x=>x.Timestamp>=cutoff&&
            x.Timestamp<=_clock().AddMinutes(1) &&
            x.RequestId!=Guid.Empty &&
            Origins.Contains(x.Origin)&&Statuses.Contains(x.Status)&&
            ActionPattern.IsMatch(x.ActionType)&&
            (x.ErrorCode==""||CodePattern.IsMatch(x.ErrorCode)) &&
            x.DurationMs is >=0 and <=120000)
            .OrderBy(x=>x.Timestamp).TakeLast(_capacity).ToList();
    }
    public void Record(Guid requestId,string origin,string actionType,string status,
        long durationMs,string errorCode="") {
        if(requestId==Guid.Empty||!Origins.Contains(origin) ||
           !ActionPattern.IsMatch(actionType)||!Statuses.Contains(status) ||
           durationMs is <0 or >120000 ||
           (errorCode!=""&&!CodePattern.IsMatch(errorCode)))
            throw new ArgumentException("Unsafe audit metadata.");
        lock(_gate) {
            var entries=Retain(ReadUnsafe());
            // Re-polling the same terminal request must not bloat history.
            if(entries.Any(x=>x.RequestId==requestId&&x.Status==status))return;
            entries.Add(new(_clock(),requestId,origin,actionType,status,
                durationMs,errorCode));
            entries=Retain(entries);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp=_path+".tmp";
            File.WriteAllText(temp,JsonSerializer.Serialize(entries),Encoding.UTF8);
            File.Move(temp,_path,true);
        }
    }
    public IReadOnlyList<DeviceAuditEntry> Recent(int max=50) {
        if(max is <1 or >512)throw new ArgumentOutOfRangeException(nameof(max));
        lock(_gate)return Retain(ReadUnsafe()).TakeLast(max)
            .Reverse().ToArray();
    }
    public void Export(string destination) {
        if(string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException("Destination required.");
        IReadOnlyList<DeviceAuditEntry> data=Recent(512);
        // Explicit user-selected destination. Only the same structured metadata.
        File.WriteAllText(destination,JsonSerializer.Serialize(data,
            new JsonSerializerOptions {WriteIndented=true}),Encoding.UTF8);
    }
}
