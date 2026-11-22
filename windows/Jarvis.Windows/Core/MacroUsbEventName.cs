using System.Text.RegularExpressions;
namespace Jarvis.Windows.Core;

/// <summary>Strict user-configured USB hardware VID/PID event names.
/// VID/PID identify a USB product family, NOT an authenticated device.
/// These events can never grant permissions or unlock restricted actions.
/// </summary>
public static class MacroUsbEventName {
    private static readonly Regex EventPattern=new(
        @"^usb\.(attach|detach):([0-9a-f]{4}):([0-9a-f]{4})$",
        RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);
    private static readonly Regex PathPattern=new(
        @"(?:^|[#\\])VID_([0-9a-f]{4})&PID_([0-9a-f]{4})(?:[&#\\]|$)",
        RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);
    public static bool TryParse(string? text,out string action,out string vidPid){
        action="";vidPid="";
        if(string.IsNullOrWhiteSpace(text)||text.Length>64)return false;
        var match=EventPattern.Match(text.Trim());
        if(!match.Success)return false;
        action=match.Groups[1].Value.ToLowerInvariant();
        vidPid=match.Groups[2].Value.ToLowerInvariant()+":"+
            match.Groups[3].Value.ToLowerInvariant();
        return true;
    }
    public static bool TryFromInterfacePath(string? path,bool attached,
            out string eventName){
        eventName="";
        if(string.IsNullOrWhiteSpace(path)||path.Length>2048)return false;
        var match=PathPattern.Match(path);
        if(!match.Success)return false;
        eventName=(attached?"usb.attach:":"usb.detach:")+
            match.Groups[1].Value.ToLowerInvariant()+":"+
            match.Groups[2].Value.ToLowerInvariant();
        return TryParse(eventName,out _,out _);
    }
}
