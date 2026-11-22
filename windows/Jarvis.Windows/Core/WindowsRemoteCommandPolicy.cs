namespace Jarvis.Windows.Core;

/// <summary>Small, auditable allowlist for remote phone/watch voice requests.
/// Screen AI, arbitrary clicks, text input and file operations never pass
/// through the voice relay. Macro steps have a separate owner-only allowlist.
/// </summary>
public static class WindowsRemoteCommandPolicy {
    public static bool IsAllowed(CommandKind kind)=>kind is
        CommandKind.OpenApp or CommandKind.OpenSettings or
        CommandKind.Volume or CommandKind.MediaToggle or
        CommandKind.MediaNext or CommandKind.MediaPrevious or
        CommandKind.Speak or CommandKind.Timer or CommandKind.WebSearch or
        CommandKind.SpotifySearch or CommandKind.SpotifyPlaylist or
        CommandKind.SystemStatus;
}
