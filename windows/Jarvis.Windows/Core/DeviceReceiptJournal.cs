using System.IO;
using System.Text.Json;

namespace Jarvis.Windows.Core;

/// <summary>
/// At-most-once Windows remote side effects, even if the WPF app restarts
/// after a command but before Cloudflare receives its acknowledgement.
/// Persist the claim BEFORE executing any local side effect.
/// </summary>
public sealed class DeviceReceiptJournal {
    public sealed record Receipt(Guid Id, DateTimeOffset ReceivedAt,
        bool? Ok, string Detail);
    private readonly object _gate=new();
    private readonly string _file;
    private readonly Dictionary<Guid,Receipt> _entries=new();

    public DeviceReceiptJournal(string? file=null) {
        _file=file ?? Path.Combine(StateStore.DirectoryPath,"remote-receipts.json");
        if(!File.Exists(_file))return;
        try {
            var rows=JsonSerializer.Deserialize<List<Receipt>>(
                File.ReadAllText(_file)) ?? new();
            foreach(var entry in rows)
                if(DateTimeOffset.UtcNow-entry.ReceivedAt<TimeSpan.FromMinutes(10))
                    _entries[entry.Id]=entry;
        }catch(JsonException) {
            // Failing closed matters: do not replay unknown actions when the
            // receipt file is corrupted. Notify the app instead.
            throw new InvalidDataException(
                "Журнал прийнятих дистанційних команд пошкоджено. "+
                "Віддалене керування вимкнено до відновлення даних.");
        }
    }

    public bool TryClaim(Guid id,out Receipt? existing) {
        if(id==Guid.Empty)throw new ArgumentException("Invalid command ID.");
        lock(_gate) {
            if(_entries.TryGetValue(id,out existing))return false;
            foreach(var key in _entries.Where(x=>
                DateTimeOffset.UtcNow-x.Value.ReceivedAt>
                TimeSpan.FromMinutes(10)).Select(x=>x.Key).ToArray())
                _entries.Remove(key);
            // Durable claim precedes execution. On crash before completion,
            // report "unknown"; never execute it twice.
            var pending=new Receipt(id,DateTimeOffset.UtcNow,null,"");
            _entries[id]=pending;
            try { Save(); }
            catch { _entries.Remove(id);throw; }
            existing=null;
            return true;
        }
    }

    public void Complete(Guid id,bool ok,string detail) {
        lock(_gate) {
            if(!_entries.TryGetValue(id,out var previous))
                throw new InvalidOperationException("Command not claimed.");
            if(previous.Ok.HasValue)return;
            var done=previous with {Ok=ok,Detail=detail.Length<=1400
                ?detail:detail[..1400]};
            _entries[id]=done;
            Save();
        }
    }

    private void Save() {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var temp=_file+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(_entries.Values.ToArray()));
        File.Move(temp,_file,true);
    }
}
