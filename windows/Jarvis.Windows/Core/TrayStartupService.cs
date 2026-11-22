using Microsoft.Win32;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
namespace Jarvis.Windows.Core;

/// <summary>Interactive per-user startup and tray. Never installs a service or UAC bypass.</summary>
public sealed class TrayStartupService : IDisposable {
    private const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName="Jarvis.Windows";
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _pause;
    private readonly ToolStripMenuItem _listen;
    public event Action? ShowRequested;
    public event Action? ListenRequested;
    public event Action? DevicesRequested;
    public event Action<bool>? PauseChanged;
    public event Action? ExitRequested;

    public TrayStartupService() {
        var menu=new ContextMenuStrip();
        menu.Items.Add("Відкрити JARVIS",null,(_,_)=>ShowRequested?.Invoke());
        _listen=new ToolStripMenuItem("Почати/зупинити слухання",null,(_,_)=>ListenRequested?.Invoke());
        menu.Items.Add(_listen);
        menu.Items.Add("Статус пристроїв",null,(_,_)=>DevicesRequested?.Invoke());
        _pause=new ToolStripMenuItem("Призупинити автоматику");
        _pause.Click+=(_,_)=>{_pause.Checked=!_pause.Checked;PauseChanged?.Invoke(_pause.Checked);};
        menu.Items.Add(_pause);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Вихід",null,(_,_)=>ExitRequested?.Invoke());
        _icon=new NotifyIcon {
            Icon=SystemIcons.Application,
            Text="JARVIS Windows — активний",
            ContextMenuStrip=menu,
            Visible=true
        };
        _icon.DoubleClick+=(_,_)=>ShowRequested?.Invoke();
    }
    public void SetListening(bool value) =>
        _listen.Text=value?"Зупинити слухання":"Почати слухання";
    public static void ConfigureAutostart(bool enable) {
        using var key=Registry.CurrentUser.CreateSubKey(RunKey,true)
            ?? throw new IOException("Не вдалося відкрити HKCU Run.");
        if(!enable){key.DeleteValue(ValueName,false);return;}
        var exe=Environment.ProcessPath;
        if(string.IsNullOrWhiteSpace(exe)||
           !Path.GetFileName(exe).Equals("Jarvis.Windows.exe",StringComparison.OrdinalIgnoreCase)||
           !File.Exists(exe))
            throw new InvalidOperationException(
                "Автозапуск підтримується в портативній Windows-збірці Jarvis.Windows.exe.");
        key.SetValue(ValueName,"\""+exe+"\" --minimized",RegistryValueKind.String);
    }
    public void Dispose() {
        _icon.Visible=false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
