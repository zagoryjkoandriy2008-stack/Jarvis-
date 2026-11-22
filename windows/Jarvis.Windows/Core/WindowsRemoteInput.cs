using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace Jarvis.Windows.Core;

/// <summary>
/// Approved owner-assisted remote mouse and keyboard via Windows SendInput.
/// Commands may arrive ONLY from the active user-approved screen session.
/// Never invoke from AI output, a phone macro, or unauthenticated HTTP.
/// No shell, process execution, file access, clipboard reading, or unlock.
/// </summary>
public static class WindowsRemoteInput {
    private const uint InputMouse=0,InputKeyboard=1;
    private const uint MouseMove=0x0001,MouseLeftDown=0x0002,MouseLeftUp=0x0004;
    private const uint MouseRightDown=0x0008,MouseRightUp=0x0010;
    private const uint MouseWheel=0x0800,MouseAbsolute=0x8000,MouseVirtual=0x4000;
    private const uint KeyUp=0x0002,KeyUnicode=0x0004;
    private const uint VkControl=0x11;
    private const int MaxText=160;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput {public uint Type;public InputUnion Data;}
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion {
        [FieldOffset(0)]public MouseInput Mouse;
        [FieldOffset(0)]public KeyInput Key;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput {
        public int X,Y;
        public uint Data,Flags,Time;
        public IntPtr Extra;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyInput {
        public ushort Vk,Scan;
        public uint Flags,Time;
        public IntPtr Extra;
    }
    [DllImport("user32.dll",SetLastError=true)]
    private static extern uint SendInput(uint count,
        [In] NativeInput[] inputs,int inputSize);

    private static void Send(params NativeInput[] inputs) {
        if(SendInput((uint)inputs.Length,inputs,
           Marshal.SizeOf<NativeInput>())!=(uint)inputs.Length)
            throw new InvalidOperationException(
                "Windows відхилив дистанційне введення. "+
                "Можливо, активне вікно має вищі права.");
    }
    private static NativeInput Mouse(uint flags,int x=0,int y=0,uint data=0) =>
        new() {Type=InputMouse,Data=new InputUnion {
            Mouse=new MouseInput {X=x,Y=y,Data=data,Flags=flags}
        }};
    private static NativeInput Key(ushort vk,uint flags=0,ushort scan=0) =>
        new() {Type=InputKeyboard,Data=new InputUnion {
            Key=new KeyInput {Vk=vk,Scan=scan,Flags=flags}
        }};
    private static double Number(JsonElement e,string prop) {
        if(!e.TryGetProperty(prop,out var v)||
           v.ValueKind!=JsonValueKind.Number||!v.TryGetDouble(out var value)||
           !double.IsFinite(value)||value<0||value>1)
            throw new InvalidDataException("Координати поза екраном.");
        return value;
    }
    private static ushort VirtualKey(string key)=>key switch {
        "ENTER"=>0x0D,"ESC"=>0x1B,"TAB"=>0x09,"BACKSPACE"=>0x08,
        "DELETE"=>0x2E,"SPACE"=>0x20,"UP"=>0x26,"DOWN"=>0x28,
        "LEFT"=>0x25,"RIGHT"=>0x27,"HOME"=>0x24,"END"=>0x23,
        "PAGEUP"=>0x21,"PAGEDOWN"=>0x22,"CTRL_A"=>0x41,
        "CTRL_C"=>0x43,"CTRL_V"=>0x56,
        _=>throw new InvalidDataException("Ця клавіша не дозволена.")
    };
    private static void Move(double x,double y) {
        var primary=Screen.PrimaryScreen?.Bounds ??
            throw new InvalidOperationException("Немає головного монітора.");
        var virt=SystemInformation.VirtualScreen;
        if(primary.Width<2 || primary.Height<2 || virt.Width<2 ||virt.Height<2)
            throw new InvalidOperationException("Невірна геометрія монітора.");
        // Touch coordinates are normalized to displayed FIT_CENTER JPEG;
        // Windows maps them to the actual primary monitor, even when a
        // second monitor has negative coordinates (virtual desktop).
        double screenX=primary.Left+x*(primary.Width-1);
        double screenY=primary.Top+y*(primary.Height-1);
        int absoluteX=(int)Math.Round(
            (screenX-virt.Left)*65535.0/(virt.Width-1));
        int absoluteY=(int)Math.Round(
            (screenY-virt.Top)*65535.0/(virt.Height-1));
        Send(Mouse(MouseMove|MouseAbsolute|MouseVirtual,
            Math.Clamp(absoluteX,0,65535),Math.Clamp(absoluteY,0,65535)));
    }
    private static void Tap(double x,double y,bool twice) {
        Move(x,y);
        Send(Mouse(MouseLeftDown),Mouse(MouseLeftUp));
        if(twice) {
            Thread.Sleep(75);
            Send(Mouse(MouseLeftDown),Mouse(MouseLeftUp));
        }
    }
    private static void RightClick(double x,double y) {
        Move(x,y);
        Send(Mouse(MouseRightDown),Mouse(MouseRightUp));
    }
    private static void Drag(double x,double y,double x2,double y2) {
        Move(x,y);
        Send(Mouse(MouseLeftDown));
        try {
            for(int i=1;i<=12;i++) {
                double p=i/12.0;
                Move(x+(x2-x)*p,y+(y2-y)*p);
                Thread.Sleep(12);
            }
        } finally {
            // Never leave mouse button logically held after an error.
            Send(Mouse(MouseLeftUp));
        }
    }
    private static void Keyboard(string code) {
        ushort vk=VirtualKey(code);
        bool ctrl=code.StartsWith("CTRL_",StringComparison.Ordinal);
        if(ctrl)Send(Key((ushort)VkControl));
        try {Send(Key(vk),Key(vk,KeyUp));}
        finally {if(ctrl)Send(Key((ushort)VkControl,KeyUp));}
    }
    private static void TextInput(string text) {
        if(string.IsNullOrEmpty(text)||text.Length>MaxText||
           text.Any(char.IsControl))
            throw new InvalidDataException("Текст до 160 символів без переносів.");
        // KEYEVENTF_UNICODE handles Ukrainian, Cyrillic and supplementary
        // characters without depending on the Windows keyboard layout.
        foreach(char ch in text) {
            Send(Key(0,KeyUnicode,ch),
                 Key(0,KeyUnicode|KeyUp,ch));
        }
    }

    public static void Execute(JsonElement e) {
        if(e.ValueKind!=JsonValueKind.Object)
            throw new InvalidDataException("Невірна дистанційна дія.");
        if(!e.TryGetProperty("kind",out var kindField)||
           kindField.ValueKind!=JsonValueKind.String)
            throw new InvalidDataException("Відсутній тип дії.");
        var kind=kindField.GetString();
        switch(kind) {
            case "tap":
                Tap(Number(e,"x"),Number(e,"y"),false);
                break;
            case "double":
                Tap(Number(e,"x"),Number(e,"y"),true);
                break;
            case "right":
                RightClick(Number(e,"x"),Number(e,"y"));
                break;
            case "drag":
                Drag(Number(e,"x"),Number(e,"y"),
                     Number(e,"x2"),Number(e,"y2"));
                break;
            case "scroll":
                if(!e.TryGetProperty("delta",out var delta) ||
                   !delta.TryGetInt32(out int steps)||
                   steps is < -8 or > 8 or 0)
                    throw new InvalidDataException("Неправильний скрол.");
                Send(Mouse(MouseWheel,data:unchecked((uint)(steps*120))));
                break;
            case "key":
                if(!e.TryGetProperty("key",out var k) ||
                   k.ValueKind!=JsonValueKind.String)
                    throw new InvalidDataException("Немає клавіші.");
                Keyboard(k.GetString()??"");
                break;
            case "text":
                if(!e.TryGetProperty("text",out var t) ||
                   t.ValueKind!=JsonValueKind.String)
                    throw new InvalidDataException("Немає тексту.");
                TextInput(t.GetString()??"");
                break;
            default:throw new InvalidDataException(
                "Непідтримувана дистанційна дія.");
        }
    }
}
