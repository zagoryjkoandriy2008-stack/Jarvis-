using System.Security.Cryptography;
using System.Text;
namespace Jarvis.Windows.Core;
/// <summary>
/// Explicit short-lived clipboard text packet. Not a background recorder.
/// Sender-side filters are only a safety net, not a reliable password detector.
/// Pairing and user consent are mandatory before any transport is enabled.
/// </summary>
public static class ClipboardBridge {
    public sealed record TextPacket(Guid Id,string Origin,string Text,string Hash,
        DateTimeOffset ExpiresAt);
    public static TextPacket Create(string origin,string text,DateTimeOffset now) {
        if(origin is not ("computer" or "phone")||string.IsNullOrWhiteSpace(text)||
           text.Length>4096||text.Any(x=>x=='\0'))
            throw new ArgumentException("Неприпустимий текст буфера.");
        var lower=text.Trim().ToLowerInvariant();
        if(lower.StartsWith("otpauth:") || lower.StartsWith("password:") ||
           lower.StartsWith("bearer ") || lower.StartsWith("sk-proj-"))
            throw new InvalidOperationException("Схоже на секрет. Текст не передано.");
        var bytes=Encoding.UTF8.GetBytes(text);
        if(bytes.Length>8192)throw new ArgumentException("Буфер перевищує 8 КіБ.");
        return new(Guid.NewGuid(),origin,text,
            Convert.ToHexString(SHA256.HashData(bytes)),now.AddSeconds(45));
    }
    public static bool Accept(TextPacket? packet,DateTimeOffset now,
            string receivingDevice,string? lastHash) =>
        packet is not null && packet.Id!=Guid.Empty &&
        packet.Origin is "computer" or "phone" &&
        packet.Origin!=receivingDevice && packet.ExpiresAt>now &&
        packet.ExpiresAt<=now.AddSeconds(45) &&
        packet.Text.Length is >0 and <=4096 &&
        packet.Hash.Length==64 && packet.Hash.All(Uri.IsHexDigit) &&
        packet.Hash.Equals(Convert.ToHexString(SHA256.HashData(
          Encoding.UTF8.GetBytes(packet.Text))),StringComparison.OrdinalIgnoreCase) &&
        !packet.Hash.Equals(lastHash,StringComparison.OrdinalIgnoreCase);
}
