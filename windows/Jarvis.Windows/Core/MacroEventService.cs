using System.Net.NetworkInformation;

namespace Jarvis.Windows.Core;

/// <summary>
/// Event-driven network availability triggers; never polls every second.
/// Local macros only, owner opt-in per macro. Caller marshals to WPF UI thread.
/// </summary>
public sealed class MacroEventService : IDisposable {
    private bool _running;
    public event Action<string>? Triggered;
    public void Start() {
        if(_running)return;
        _running=true;
        NetworkChange.NetworkAvailabilityChanged+=OnNetworkAvailabilityChanged;
    }
    private void OnNetworkAvailabilityChanged(object? sender,
            NetworkAvailabilityEventArgs e) {
        if(!_running)return;
        Triggered?.Invoke(e.IsAvailable?"network.online":"network.offline");
    }
    public void Dispose() {
        if(!_running)return;
        _running=false;
        NetworkChange.NetworkAvailabilityChanged-=OnNetworkAvailabilityChanged;
    }
}
