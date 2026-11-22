using System.Management;
using System.Text.RegularExpressions;

namespace Jarvis.Windows.Core;

public static class MacroUsbEventName {
    private static readonly Regex Valid=new(
        @"^device\.(attach|detach):(vid_[a-f0-9]{4}&pid_[a-f0-9]{4})$",
        RegexOptions.Compiled|RegexOptions.CultureInvariant|RegexOptions.IgnoreCase);
    private static readonly Regex InterfaceName=new(
        @"(?:^|[#\\])VID_([0-9a-f]{4})&PID_([0-9a-f]{4})(?:[&#\\]|$)",
        RegexOptions.CultureInvariant|RegexOptions.IgnoreCase|RegexOptions.Compiled);
    public static bool TryParse(string? input,out string hardware,out bool attach) {
        hardware="";attach=false;
        if(string.IsNullOrWhiteSpace(input) || input.Length>64)return false;
        var match=Valid.Match(input);
        if(!match.Success)return false;
        hardware=match.Groups[2].Value.ToLowerInvariant();
        attach=match.Groups[1].Value=="attach";
        return true;
    }
    public static bool TryFromInterfacePath(string? path,bool attach,
            out string eventName) {
        eventName="";
        if(string.IsNullOrWhiteSpace(path) || path.Length>2048)return false;
        var found=InterfaceName.Match(path);
        if(!found.Success)return false;
        eventName="device."+(attach?"attach:":"detach:")+
            "vid_"+found.Groups[1].Value.ToLowerInvariant()+"&pid_"+
            found.Groups[2].Value.ToLowerInvariant();
        return TryParse(eventName,out _,out _);
    }
}
public sealed class MacroUsbTransitions {
    private readonly Dictionary<string,bool> _previous=new();
    public void Baseline(string device,bool present)=>_previous[device]=present;
    public void Clear()=>_previous.Clear();
    public string Observe(string device,bool present) {
        if(!_previous.TryGetValue(device,out var before)) {
            Baseline(device,present);return "";
        }
        if(before==present)return "";
        _previous[device]=present;
        return "device."+(present?"attach:":"detach:")+device;
    }
}

/// <summary>
/// User opt-in PnP hardware arrival/removal. Runs only after a native WMI
/// device-change event, with initial baseline and no continuous process/device
/// polling. Device IDs never leave Windows, and unsupported WMI fails closed.
/// </summary>
public sealed class MacroUsbEventService : IDisposable {
    private readonly object _sync=new();
    private readonly MacroUsbTransitions _transitions=new();
    private string[] _keys=Array.Empty<string>();
    private ManagementEventWatcher? _watcher;
    private bool _closed;
    public event Action<string>? Triggered;
    public event Action<string>? Status;

    private static HashSet<string> Present(string[] keys) {
        var foundKeys=new HashSet<string>(StringComparer.Ordinal);
        using var search=new ManagementObjectSearcher(
            "SELECT PNPDeviceID FROM Win32_PnPEntity WHERE ConfigManagerErrorCode = 0");
        using var rows=search.Get();
        foreach(ManagementBaseObject item in rows) {
            if(item["PNPDeviceID"] is not string id)continue;
            foreach(var key in keys) {
                if(id.Contains(key,StringComparison.OrdinalIgnoreCase))
                    foundKeys.Add(key);
            }
        }
        return foundKeys;
    }
    public void Configure(IEnumerable<string> enabledTriggers) {
        var keys=enabledTriggers.Select(x=>
            MacroUsbEventName.TryParse(x,out var id,out _)?id:"")
            .Where(x=>x.Length>0).Distinct().Take(12).ToArray();
        Stop();
        if(_closed||keys.Length==0)return;
        try {
            var baseline=Present(keys);
            var watcher=new ManagementEventWatcher(new WqlEventQuery(
                "SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2 OR EventType = 3"));
            watcher.EventArrived+=OnDeviceChange;
            try{watcher.Start();}
            catch{
                watcher.EventArrived-=OnDeviceChange;
                watcher.Dispose();throw;
            }
            lock(_sync){
                if(_closed) {
                    watcher.EventArrived-=OnDeviceChange;
                    try{watcher.Stop();}catch(Exception){}
                    watcher.Dispose();return;
                }
                _keys=keys;
                _transitions.Clear();
                foreach(var key in keys)_transitions.Baseline(key,baseline.Contains(key));
                _watcher=watcher;
            }
        }catch(Exception e) {
            Status?.Invoke("USB-макрос недоступний (WMI): "+e.GetType().Name);
        }
    }
    private void OnDeviceChange(object sender,EventArrivedEventArgs args) {
        string[] keys;
        lock(_sync){
            if(_closed || _watcher==null || !ReferenceEquals(_watcher,sender))return;
            keys=_keys;
        }
        try {
            var present=Present(keys);
            var emitted=new List<string>();
            lock(_sync) {
                if(_closed||_watcher==null||!ReferenceEquals(_watcher,sender))return;
                foreach(var key in keys) {
                    var e=_transitions.Observe(key,present.Contains(key));
                    if(e.Length>0)emitted.Add(e);
                }
            }
            foreach(var name in emitted)Triggered?.Invoke(name);
        }catch(Exception){
            // Failure to query is not equivalent to disconnecting a USB device.
        }
    }
    public void Stop() {
        ManagementEventWatcher? old;
        lock(_sync){
            old=_watcher;_watcher=null;
            _keys=Array.Empty<string>();_transitions.Clear();
        }
        if(old==null)return;
        old.EventArrived-=OnDeviceChange;
        try{old.Stop();}catch(Exception){}
        old.Dispose();
    }
    public void Dispose(){_closed=true;Stop();}
}
