using System.IO;
using System.Runtime.InteropServices;

namespace Jarvis.Windows.Core;

/// <summary>Read-only Windows health snapshot; no background telemetry.</summary>
public static class WindowsSystemMonitor {
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus {
        public uint Length, Load;
        public ulong TotalPhys, FreePhys, TotalPage, FreePage;
        public ulong TotalVirtual, FreeVirtual, FreeExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    // Win32 FILETIME is 64-bit; kernel time includes idle time.
    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle,out long kernel,out long user);

    private static string ReadMemory() {
        var status=new MemoryStatus{Length=(uint)Marshal.SizeOf<MemoryStatus>()};
        if(!GlobalMemoryStatusEx(ref status))
            throw new InvalidOperationException("Windows не повернув використання пам'яті.");
        return $"RAM {status.Load}% (вільно {status.FreePhys/1073741824.0:0.0} ГБ із {status.TotalPhys/1073741824.0:0.0} ГБ)";
    }
    private static string ReadSystemDisk() {
        var root=Path.GetPathRoot(Environment.SystemDirectory);
        if(string.IsNullOrEmpty(root))return "Диск: недоступний";
        var drive=new DriveInfo(root);
        if(!drive.IsReady)return "Диск: недоступний";
        return $"Диск {drive.Name} вільно {drive.AvailableFreeSpace/1073741824.0:0.0} ГБ із {drive.TotalSize/1073741824.0:0.0} ГБ";
    }
    public static async Task<string> SnapshotAsync(CancellationToken cancellation) {
        if(!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Моніторинг працює у Windows.");
        if(!GetSystemTimes(out var idle1,out var kernel1,out var user1))
            throw new InvalidOperationException("Немає даних CPU.");
        await Task.Delay(600,cancellation);
        if(!GetSystemTimes(out var idle2,out var kernel2,out var user2))
            throw new InvalidOperationException("Немає даних CPU.");
        double total=(double)(kernel2-kernel1)+(user2-user1);
        double cpu=total>0?Math.Clamp(100.0*(1.0-(idle2-idle1)/total),0,100):0;
        return $"🖥 CPU {cpu:0}% · {ReadMemory()} · {ReadSystemDisk()}";
    }
}
