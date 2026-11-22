namespace Jarvis.Windows.Core;

/// <summary>
/// Independent Windows-side capabilities for signed device commands.
/// Fail closed when a grant has not been explicitly saved on this PC.
/// Never authorize remote shell, screen capture, AI input, clipboard or
/// finance commands through this transport.
/// </summary>
public static class SignedDeviceLocalPolicy {
    public static bool IsAllowed(JarvisSettings settings,string origin,
            string kind,bool requiresApproval) {
        if(settings==null || !settings.EnableDeviceRelay ||
           !settings.EnableSignedRelay)return false;
        if(origin is not ("phone" or "watch"))return false;
        if(origin=="watch"&&!settings.SignedWatchDelegation)return false;
        return kind switch {
            "system.status" or "devices.list"=>settings.SignedReadOnly,
            "media.toggle" or "media.next" or "media.previous" or
                "volume.set"=>settings.SignedMedia,
            "application.open"=>settings.SignedApps,
            "windows.lock" or "monitor.off"=>
                settings.SignedLocks && requiresApproval,
            _=>false
        };
    }
}
