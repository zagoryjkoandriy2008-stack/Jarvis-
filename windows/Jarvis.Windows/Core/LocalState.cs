using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jarvis.Windows.Core;

public sealed class JarvisSettings {
    public string WorkerUrl { get; set; } = "";
    public string VoiceName { get; set; } = "";
    public string RecognizerCulture { get; set; } = "";
    public bool SpeakReplies { get; set; } = true;
    public bool OnTop { get; set; }
    public bool EnableDeviceRelay { get; set; } // user opt-in, default off
    public bool ShowFloatingOrb { get; set; } = true;
}
public sealed class MoneyEntry {
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public bool Income { get; set; }
    public decimal Amount { get; set; }
    public string Account { get; set; } = "готівка";
    public string Category { get; set; } = "";
    public string Note { get; set; } = "";
}
public sealed class MacroDefinition {
    public string Trigger { get; set; } = "";
    public List<string> Actions { get; set; } = new();
    public bool Enabled { get; set; } = true;
}
public sealed class JarvisState {
    public JarvisSettings Settings { get; set; } = new();
    public string Notes { get; set; } = "";
    public List<MoneyEntry> Money { get; set; } = new();
    public List<MacroDefinition> Macros { get; set; } = new();
}

public sealed class StateStore {
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Jarvis.Windows");
    private readonly string _stateFile=Path.Combine(DirectoryPath,"state.json");
    private readonly string _secretFile=Path.Combine(DirectoryPath,"worker-token.dpapi");
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        WriteIndented = true, PropertyNameCaseInsensitive = true
    };

    public JarvisState Load() {
        Directory.CreateDirectory(DirectoryPath);
        if(!File.Exists(_stateFile))return new();
        try {
            var state=JsonSerializer.Deserialize<JarvisState>(
                File.ReadAllText(_stateFile),SerializerOptions) ?? new();
            state.Settings ??= new();
            state.Money ??= new();
            state.Macros ??= new();
            return state;
        }catch(JsonException) {
            // Never overwrite a damaged file: a human must decide how to restore.
            throw new InvalidDataException("Файл JARVIS Windows пошкоджений. Копію збережено; віднови state.json вручну.");
        }
    }
    public void Save(JarvisState state) {
        Directory.CreateDirectory(DirectoryPath);
        var text=JsonSerializer.Serialize(state,SerializerOptions);
        var temp=_stateFile+".tmp";
        File.WriteAllText(temp,text,Encoding.UTF8);
        File.Move(temp,_stateFile,true);
    }
    public string? ReadWorkerToken() {
        if(!File.Exists(_secretFile))return null;
        var protectedBytes=File.ReadAllBytes(_secretFile);
        var bytes=ProtectedData.Unprotect(protectedBytes,null,DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
    public void SaveWorkerToken(string token) {
        if(token.Length<20 || token.Length>1000)
            throw new ArgumentException("Код Worker має містити щонайменше 20 символів.");
        Directory.CreateDirectory(DirectoryPath);
        var protectedBytes=ProtectedData.Protect(Encoding.UTF8.GetBytes(token),
            null,DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_secretFile+".tmp",protectedBytes);
        File.Move(_secretFile+".tmp",_secretFile,true);
    }
    public void ClearWorkerToken() {
        if(File.Exists(_secretFile))File.Delete(_secretFile);
    }
}
