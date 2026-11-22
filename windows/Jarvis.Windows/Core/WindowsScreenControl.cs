using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Jarvis.Windows.Core;

/// <summary>
/// Local, explicit UI Automation for the foreground window only.
/// Never call from remote relay/AI text or from a macro. Does not click
/// by fixed screen coordinates; avoids Windows dialogs' fragile layout.
/// </summary>
public static class WindowsScreenControl {
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);

    private static AutomationElement ExternalForeground() {
        var hwnd=GetForegroundWindow();
        if(hwnd==IntPtr.Zero)
            throw new InvalidOperationException("Немає активного вікна.");
        GetWindowThreadProcessId(hwnd,out var process);
        if(process==(uint)Environment.ProcessId)
            throw new InvalidOperationException(
                "Вибери потрібну програму, не головне вікно JARVIS. "+
                "Плаваюче обличчя не повинно перекривати ціль.");
        var element=AutomationElement.FromHandle(hwnd);
        if(element==null)throw new InvalidOperationException(
            "Windows не надала доступу до елементів активної програми.");
        return element;
    }

    public static string ClickNamed(string spokenName) {
        var name=spokenName.Trim();
        if(name.Length is <1 or >100 || name.Any(char.IsControl))
            throw new ArgumentException("Назва кнопки має містити 1–100 символів.");
        var root=ExternalForeground();
        var matching=new PropertyCondition(
            AutomationElement.NameProperty,name,PropertyConditionFlags.IgnoreCase);
        AutomationElementCollection candidates;
        try { candidates=root.FindAll(TreeScope.Descendants,matching); }
        catch(ElementNotAvailableException) {
            throw new InvalidOperationException("Активне вікно змінилося. Повтори команду.");
        }
        // Read the actual accessibility name; do not scan/copy typed text
        // or interact with password/editable secrets.
        foreach(AutomationElement item in candidates) {
            try {
                if(!item.Current.IsEnabled || item.Current.IsPassword ||
                   item.Current.IsOffscreen)continue;
                var control=item.Current.ControlType;
                if(control==ControlType.Edit) {
                    item.SetFocus();
                    return "Активував поле: "+name;
                }
                if(control!=ControlType.Button && control!=ControlType.MenuItem &&
                   control!=ControlType.Hyperlink && control!=ControlType.TabItem &&
                   control!=ControlType.ListItem && control!=ControlType.CheckBox &&
                   control!=ControlType.RadioButton)continue;
                if(item.TryGetCurrentPattern(InvokePattern.Pattern,out var action)) {
                    ((InvokePattern)action).Invoke();
                    return "Натиснув: "+name;
                }
                if(item.TryGetCurrentPattern(SelectionItemPattern.Pattern,out var selected)) {
                    ((SelectionItemPattern)selected).Select();
                    return "Вибрав: "+name;
                }
                if(item.TryGetCurrentPattern(TogglePattern.Pattern,out var toggled)) {
                    ((TogglePattern)toggled).Toggle();
                    return "Перемкнув: "+name;
                }
            }catch(ElementNotAvailableException) {continue;}
             catch(InvalidOperationException) {continue;}
        }
        throw new InvalidOperationException(
            "Не знайшов доступної кнопки «"+name+"» в активній програмі. "+
            "Деякі програми не надають назви елементів Windows.");
    }

    public static string ScrollPage(bool down) {
        ExternalForeground();
        WindowsActions.MediaKey(down?(ushort)0x22:(ushort)0x21);
        return down?"Прокрутив сторінку вниз.":"Прокрутив сторінку вгору.";
    }
}
