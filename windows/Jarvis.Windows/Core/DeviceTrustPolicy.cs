namespace Jarvis.Windows.Core;

/// <summary>
/// Version 2 remote command contract. Policy data only: pairing and cryptographic
/// sender authentication MUST be enforced before enabling this transport.
/// Existing legacy relay remains separately restricted by its own allowlist.
/// </summary>
public enum RemoteActionScope { ReadOnly, SafeAction, ApprovalRequired, ForbiddenRemote }
public sealed record DeviceCommandV2(
    int Version,Guid RequestId,string Origin,string Target,string Kind,
    Dictionary<string,string> Payload,DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,bool RequiresApproval,string ReplyTo);
public static class DeviceTrustPolicy {
    public static RemoteActionScope Scope(string kind)=>kind switch {
        "system.status" or "devices.list"=>RemoteActionScope.ReadOnly,
        "media.toggle" or "media.next" or "media.previous" or
        "volume.set" or "application.open" or "macro.run"=>RemoteActionScope.SafeAction,
        "windows.lock" or "monitor.off" or "wol.wake"=>RemoteActionScope.ApprovalRequired,
        _=>RemoteActionScope.ForbiddenRemote
    };
    public static bool IsValid(DeviceCommandV2? c,DateTimeOffset now) {
        if(c==null || c.Version!=2 || c.RequestId==Guid.Empty ||
           c.Origin is not ("phone" or "watch" or "computer") ||
           c.Target is not ("phone" or "watch" or "computer") ||
           c.Origin==c.Target || c.ReplyTo!=c.Origin ||
           c.Payload==null || c.Payload.Count>8 ||
           c.CreatedAt>now.AddSeconds(30) || c.CreatedAt<now.AddMinutes(-2) ||
           c.ExpiresAt<=now || c.ExpiresAt<=c.CreatedAt ||
           c.ExpiresAt-c.CreatedAt>TimeSpan.FromMinutes(2))
            return false;
        var scope=Scope(c.Kind);
        if(scope==RemoteActionScope.ForbiddenRemote||
           (scope==RemoteActionScope.ApprovalRequired&&!c.RequiresApproval))return false;
        if(c.Payload.Any(p=>p.Key.Length>32 || p.Value.Length>160 ||
           p.Value.Any(char.IsControl)))return false;
        if(c.Kind=="volume.set" && (!c.Payload.TryGetValue("value",out var number) ||
           !int.TryParse(number,out var volume)||volume<0||volume>100))return false;
        return true;
    }
}
