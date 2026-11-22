using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Jarvis.Windows.Core;

/// <summary>Explicit file choice to Pixel, with a 120-second in-memory inbox.
/// No folder crawling or background upload; not suitable for large videos.
/// </summary>
public sealed class WindowsFileExchange : IDisposable {
    public const int MaxFileBytes=1_048_576;
    private readonly HttpClient _http=new(){Timeout=TimeSpan.FromSeconds(20)};
    public async Task<string> SendToPixelAsync(string selectedPath,
        JarvisSettings settings,StateStore store,CancellationToken cancel) {
        if(!settings.EnableDeviceRelay)
            throw new InvalidOperationException("Спочатку увімкни приватний зв'язок пристроїв.");
        if(!AiClient.ValidEndpoint(settings.WorkerUrl))
            throw new InvalidOperationException("Потрібна адреса приватного HTTPS Worker.");
        string? key=store.ReadWorkerToken();
        if(string.IsNullOrWhiteSpace(key)||key.Length<20)
            throw new InvalidOperationException("Немає коду Worker.");
        string filename=Path.GetFileName(selectedPath);
        if(filename.Length is <1 or >100 || filename=="." || filename==".." ||
           filename.Any(c=>char.IsControl(c)||Path.GetInvalidFileNameChars().Contains(c)))
            throw new InvalidOperationException("Недопустиме ім'я файла.");
        var file=new FileInfo(selectedPath);
        if(!file.Exists || file.Length is <1 or >MaxFileBytes)
            throw new InvalidOperationException("Для тимчасового обміну вибери файл до 1 МіБ.");
        byte[] bytes=await File.ReadAllBytesAsync(selectedPath,cancel);
        if(bytes.Length is <1 or >MaxFileBytes)
            throw new InvalidOperationException("Файл змінився або завеликий.");
        string mime=Path.GetExtension(filename).ToLowerInvariant() switch {
            ".txt" or ".md" or ".csv" => "text/plain",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
        var root=new Uri(new Uri(settings.WorkerUrl).GetLeftPart(UriPartial.Authority));
        using var request=new HttpRequestMessage(HttpMethod.Post,
            new Uri(root,"/device/v1/files/send"));
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        request.Content=new StringContent(JsonSerializer.Serialize(new {
            id=Guid.NewGuid().ToString(),source="computer",target="phone",
            name=filename,mime,data=Convert.ToBase64String(bytes)
        }),Encoding.UTF8,"application/json");
        using var response=await _http.SendAsync(request,cancel);
        if(response.StatusCode==System.Net.HttpStatusCode.Conflict)
            throw new InvalidOperationException("На Pixel уже очікує файл. Отримай його або зачекай 2 хвилини.");
        if(!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Relay не прийняв файл (HTTP "+(int)response.StatusCode+").");
        return "📤 Файл «"+filename+"» тимчасово доступний на Pixel протягом 2 хвилин. "+
            "Це підтвердження зберігання, а не завантаження на телефон.";
    }
    public void Dispose()=>_http.Dispose();
}
