using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace Jarvis.Windows.Core;

public static class WindowsActions {
    [DllImport("user32.dll",SetLastError=true)]
    private static extern bool LockWorkStation();
    [DllImport("user32.dll",CharSet=CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
    public static void LockCurrentSession() {
        if(!LockWorkStation())
            throw new InvalidOperationException("Windows не дозволила заблокувати сеанс.");
    }
    public static void TurnOffMonitor() {
        const uint WM_SYSCOMMAND=0x0112;
        const int SC_MONITORPOWER=0xF170;
        SendMessage(new IntPtr(0xFFFF),WM_SYSCOMMAND,new IntPtr(SC_MONITORPOWER),new IntPtr(2));
    }

    private static readonly Dictionary<string,string> Applications=
        new(StringComparer.OrdinalIgnoreCase) {
            ["блокнот"]="notepad.exe",["notepad"]="notepad.exe",
            ["калькулятор"]="calc.exe",["calculator"]="calc.exe",
            ["провідник"]="explorer.exe",["explorer"]="explorer.exe",
            ["браузер"]="https://www.google.com",["browser"]="https://www.google.com",
            ["spotify"]="spotify:",["спотіфай"]="spotify:",["спотифай"]="spotify:",
            ["параметри"]="ms-settings:",["налаштування"]="ms-settings:",
            ["telegram"]="https://web.telegram.org/",["телеграм"]="https://web.telegram.org/"
        };
    public static void OpenKnownApp(string name) {
        if(!Applications.TryGetValue(name.Trim(),out var target))
            throw new InvalidOperationException(
                "Для безпеки запуск доступний лише для дозволених програм: блокнот, калькулятор, провідник, браузер, Spotify, Telegram.");
        OpenTarget(target);
    }
    public static void OpenTarget(string target) {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute=true });
    }
    public static void SpotifySearch(string title) {
        if(title.Length is <1 or >180)throw new ArgumentException("Неправильна назва.");
        // Spotify's official search URL. Do not claim a first track automatically played.
        OpenTarget("https://open.spotify.com/search/"+Uri.EscapeDataString(title));
    }
    public static void WebSearch(string query) {
        if(query.Length is <1 or >500)throw new ArgumentException("Неправильний запит.");
        OpenTarget("https://www.google.com/search?q="+Uri.EscapeDataString(query));
    }

    // A separate explicit voice instruction, no arbitrary AI-initiated keystrokes.
    [StructLayout(LayoutKind.Sequential)] private struct INPUT {
        public int type;
        public InputUnion u;
    }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT {
        public ushort wVk,wScan;
        public uint dwFlags,time;
        public IntPtr dwExtraInfo;
    }
    [DllImport("user32.dll",SetLastError=true)]
    private static extern uint SendInput(uint count,INPUT[] input,int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    private const uint KEYUP=0x0002, UNICODE=0x0004;

    public static void MediaKey(ushort key) {
        var sequence=new []{
            new INPUT {type=1,u=new InputUnion {ki=new KEYBDINPUT {wVk=key}}},
            new INPUT {type=1,u=new InputUnion {ki=new KEYBDINPUT {wVk=key,dwFlags=KEYUP}}}
        };
        if(SendInput(2,sequence,Marshal.SizeOf<INPUT>())!=2)
            throw new InvalidOperationException("Windows відхилила медіаклавішу.");
    }
    public const ushort PlayPause=0xB3, NextTrack=0xB0, PreviousTrack=0xB1;

    /** Never type into JARVIS itself, password dialogs, or unknown foregrounds.
     * This requires an intentionally focused separate window. */
    public static void TypeIntoForeground(string text) {
        if(string.IsNullOrEmpty(text)||text.Length>1000)
            throw new ArgumentException("Текст має бути 1–1000 символів.");
        var hwnd=GetForegroundWindow();
        if(hwnd==IntPtr.Zero)throw new InvalidOperationException("Немає активного вікна.");
        GetWindowThreadProcessId(hwnd,out var process);
        if(process==(uint)Environment.ProcessId)
            throw new InvalidOperationException(
                "Відкрий потрібне поле в іншій програмі й натисни Ctrl+Shift+J.");
        var inputs=new List<INPUT>();
        foreach(char ch in text) {
            inputs.Add(new INPUT {type=1,u=new InputUnion {
                ki=new KEYBDINPUT {wVk=0,wScan=ch,dwFlags=UNICODE}}});
            inputs.Add(new INPUT {type=1,u=new InputUnion {
                ki=new KEYBDINPUT {wVk=0,wScan=ch,dwFlags=UNICODE|KEYUP}}});
        }
        var sequence=inputs.ToArray();
        if(SendInput((uint)sequence.Length,sequence,Marshal.SizeOf<INPUT>())!=sequence.Length)
            throw new InvalidOperationException("Частину тексту Windows не ввела.");
    }
}
