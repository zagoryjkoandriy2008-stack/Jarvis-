using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Jarvis.Windows.Core;

/// <summary>
/// Event-driven local Windows macro triggers. Network availability and Win32
/// foreground-window hooks. No polling, screen capture or privileged input.
/// Foreground tracking activates only for explicitly configured app macros.
/// </summary>
public sealed class MacroEventService : IDisposable {
    private const uint EventSystemForeground=0x0003;
    private const uint WinEventOutOfContext=0;
    private const uint WinEventSkipOwnProcess=0x0002;
    private const string AppPrefix="app.active:";
    private bool _running;
    private IntPtr _hook;
    private string? _lastActiveProcess;
    private readonly WinEventProc _foregroundCallback;
    public event Action<string>? Triggered;
    public MacroEventService() {
        _foregroundCallback=OnForegroundChanged;
    }
    private delegate void WinEventProc(IntPtr hook,uint eventId,
        IntPtr hwnd,int objectId,int childId,uint eventThread,uint eventTime);

    [DllImport("user32.dll",SetLastError=true)]
    private static extern IntPtr SetWinEventHook(uint eventMin,uint eventMax,
        IntPtr module,WinEventProc callback,uint processId,uint threadId,uint flags);
    [DllImport("user32.dll",SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);

    private static string? ProcessForWindow(IntPtr hwnd) {
        if(hwnd==IntPtr.Zero)return null;
        try {
            GetWindowThreadProcessId(hwnd,out uint pid);
            if(pid==0)return null;
            using var process=Process.GetProcessById((int)pid);
            var name=process.ProcessName.ToLowerInvariant();
            // Restrict event payload; never emit titles, paths or window text.
            return System.Text.RegularExpressions.Regex.IsMatch(
                name,@"^[a-z0-9._-]{1,50}$")?name:null;
        } catch (Exception) {
            return null;
        }
    }

    public void Start(bool trackForeground=false) {
        if(_running)return;
        _running=true;
        NetworkChange.NetworkAvailabilityChanged+=OnNetworkAvailabilityChanged;
        SetForegroundTrackingEnabled(trackForeground);
    }

    public void SetForegroundTrackingEnabled(bool enable) {
        if(!_running)return;
        if(!enable) {
            if(_hook!=IntPtr.Zero) {
                UnhookWinEvent(_hook);
                _hook=IntPtr.Zero;
            }
            _lastActiveProcess=null;
            return;
        }
        if(_hook!=IntPtr.Zero)return;
        // Snapshot current app: opening JARVIS or enabling tracking must not
        // immediately run a macro before a *new* window transition.
        _lastActiveProcess=ProcessForWindow(GetForegroundWindow());
        _hook=SetWinEventHook(EventSystemForeground,EventSystemForeground,
            IntPtr.Zero,_foregroundCallback,0,0,
            WinEventOutOfContext|WinEventSkipOwnProcess);
        if(_hook==IntPtr.Zero)
            _lastActiveProcess=null; // Windows declined the hook: fail closed
    }

    private void OnForegroundChanged(IntPtr hook,uint eventId,IntPtr hwnd,
            int objectId,int childId,uint eventThread,uint eventTime) {
        if(!_running || hook!=_hook || eventId!=EventSystemForeground ||
           objectId!=0 || childId!=0)return;
        var current=ProcessForWindow(hwnd);
        if(current==null || current==_lastActiveProcess)return;
        _lastActiveProcess=current;
        Triggered?.Invoke(AppPrefix+current);
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
        if(_hook!=IntPtr.Zero) {
            UnhookWinEvent(_hook);
            _hook=IntPtr.Zero;
        }
        _lastActiveProcess=null;
    }
}
