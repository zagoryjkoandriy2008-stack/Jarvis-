using System.Management;
using System.Text.RegularExpressions;

namespace Jarvis.Windows.Core;

/// <summary>
/// Windows on-demand process start macro trigger using WMI events.
/// Never polls process lists, reads command lines or file contents.
/// When WMI is unavailable, no process-start macro is executed.
/// </summary>
public sealed class MacroProcessStartService : IDisposable {
    private ManagementEventWatcher? _watcher;
    private bool _running;
    public event Action<string>? Triggered;
    public event Action<string>? Unavailable;

    public static bool TryEventName(string? processName,out string eventName) {
        eventName="";
        if(string.IsNullOrWhiteSpace(processName))return false;
        var name=processName.Trim().ToLowerInvariant();
        if(name.EndsWith(".exe",StringComparison.Ordinal))name=name[..^4];
        if(name is "jarvis.windows" or "jarvis")return false;
        if(!Regex.IsMatch(name,@"^[a-z0-9._-]{1,50}$"))return false;
        eventName="app.start:"+name;
        return true;
    }

    public void SetEnabled(bool enable) {
        if(!enable){Dispose();return;}
        if(_running)return;
        try {
            var watcher=new ManagementEventWatcher(new WqlEventQuery(
                "SELECT ProcessName FROM Win32_ProcessStartTrace"));
            watcher.EventArrived+=OnStarted;
            try{
                watcher.Start();
                _watcher=watcher;
                _running=true;
            }catch{
                watcher.EventArrived-=OnStarted;
                watcher.Dispose();
                throw;
            }
        }catch(Exception error){
            _running=false;
            _watcher=null;
            Unavailable?.Invoke("WMI не дозволяє відстежувати запуск програм: "+
                error.GetType().Name);
        }
    }
    private void OnStarted(object sender,EventArrivedEventArgs args) {
        if(!_running)return;
        try{
            var processName=args.NewEvent?.Properties["ProcessName"]?.Value as string;
            if(TryEventName(processName,out var name))Triggered?.Invoke(name);
        }catch(Exception){/* Disappearing or inaccessible process: no action. */}
    }
    public void Dispose() {
        _running=false;
        var watcher=_watcher;_watcher=null;
        if(watcher==null)return;
        watcher.EventArrived-=OnStarted;
        try{watcher.Stop();}catch(Exception){}
        watcher.Dispose();
    }
}
