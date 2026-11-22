using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace Jarvis.Windows.Core;

/// <summary>Opt-in USB attach/detach notifications through WM_DEVICECHANGE.
/// Does not poll WMI, enumerate device files or retain serial numbers/paths.
/// VID/PID are spoofable and are never used for authorization.
/// </summary>
public sealed class MacroUsbDeviceService : IDisposable {
    private const int WM_DEVICECHANGE=0x0219;
    private const int DBT_DEVICEARRIVAL=0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE=0x8004;
    private const int DBT_DEVTYP_DEVICEINTERFACE=5;
    private const uint DEVICE_NOTIFY_WINDOW_HANDLE=0;
    private static readonly Guid UsbInterfaceId=new(
        "A5DCBF10-6530-11D2-901F-00C04FB951ED");
    private HwndSource? _source;
    private IntPtr _notification;
    private readonly HwndSourceHook _hook;
    private readonly Dictionary<string,DateTimeOffset> _recent=new();
    public event Action<string>? Triggered;
    public event Action<string>? Unavailable;

    [StructLayout(LayoutKind.Sequential)]
    private struct InterfaceFilter {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public Guid ClassGuid;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern IntPtr RegisterDeviceNotification(
        IntPtr receiver,IntPtr filter,uint flags);
    [DllImport("user32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);

    public MacroUsbDeviceService() {_hook=HandleMessage;}

    public void SetEnabled(HwndSource? source,bool enabled) {
        if(!enabled || source==null) {
            Dispose();return;
        }
        if(_notification!=IntPtr.Zero&&ReferenceEquals(_source,source))return;
        Dispose();
        IntPtr filterPtr=IntPtr.Zero;
        try {
            var filter=new InterfaceFilter {
                Size=Marshal.SizeOf<InterfaceFilter>(),
                DeviceType=DBT_DEVTYP_DEVICEINTERFACE,
                ClassGuid=UsbInterfaceId
            };
            filterPtr=Marshal.AllocHGlobal(filter.Size);
            Marshal.StructureToPtr(filter,filterPtr,false);
            var handle=RegisterDeviceNotification(source.Handle,
                filterPtr,DEVICE_NOTIFY_WINDOW_HANDLE);
            if(handle==IntPtr.Zero) {
                Unavailable?.Invoke("Windows відмовила у відстеженні USB-підключень.");
                return;
            }
            _source=source;
            _notification=handle;
            source.AddHook(_hook);
        }catch(Exception e) {
            Dispose();
            Unavailable?.Invoke("USB-тригери неактивні: "+e.GetType().Name);
        }finally {
            if(filterPtr!=IntPtr.Zero)Marshal.FreeHGlobal(filterPtr);
        }
    }

    private IntPtr HandleMessage(IntPtr hwnd,int msg,IntPtr wParam,
            IntPtr lParam,ref bool handled) {
        if(msg!=WM_DEVICECHANGE || _notification==IntPtr.Zero ||
           lParam==IntPtr.Zero)return IntPtr.Zero;
        var eventId=wParam.ToInt32();
        if(eventId is not (DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE))
            return IntPtr.Zero;
        try {
            var size=Marshal.ReadInt32(lParam);
            if(size<30||size>4096 ||
               Marshal.ReadInt32(lParam,4)!=DBT_DEVTYP_DEVICEINTERFACE)
                return IntPtr.Zero;
            var guid=Marshal.PtrToStructure<Guid>(IntPtr.Add(lParam,12));
            if(guid!=UsbInterfaceId)return IntPtr.Zero;
            var bytes=new byte[size-28];
            Marshal.Copy(IntPtr.Add(lParam,28),bytes,0,bytes.Length);
            var fullPath=Encoding.Unicode.GetString(bytes).Split('\0')[0];
            if(!MacroUsbEventName.TryFromInterfacePath(
               fullPath,eventId==DBT_DEVICEARRIVAL,out var eventName))
                return IntPtr.Zero;
            // Some hubs deliver duplicate notifications in rapid succession.
            // Keep only bounded, non-sensitive VID/PID/time records.
            var now=DateTimeOffset.UtcNow;
            if(_recent.TryGetValue(eventName,out var previous) &&
               now-previous<TimeSpan.FromSeconds(2))return IntPtr.Zero;
            if(_recent.Count>64)_recent.Clear();
            _recent[eventName]=now;
            Triggered?.Invoke(eventName);
        }catch(Exception){/* Device unplugged or inaccessible; do not execute. */}
        return IntPtr.Zero;
    }

    public void Dispose() {
        if(_source!=null) {
            try{_source.RemoveHook(_hook);}catch(InvalidOperationException){}
        }
        if(_notification!=IntPtr.Zero) {
            UnregisterDeviceNotification(_notification);
            _notification=IntPtr.Zero;
        }
        _source=null;
        _recent.Clear();
    }
}
