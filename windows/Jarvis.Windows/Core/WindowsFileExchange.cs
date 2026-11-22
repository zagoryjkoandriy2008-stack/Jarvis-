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
    public sealed record ReceivedFile(Guid Id,string Name,byte[] Bytes);
    /** Read only the owner's one-file inbox. User decides where to save. */
    public async Task<ReceivedFile?> GetFromPixelAsync(JarvisSettings settings,
        StateStore store,CancellationToken cancel) {
        if(!settings.EnableDeviceRelay || !AiClient.ValidEndpoint(settings.WorkerUrl))
            throw new InvalidOperationException("Увімкни зв'язок пристроїв і налаштуй HTTPS Worker.");
        var token=store.ReadWorkerToken();
        if(string.IsNullOrWhiteSpace(token)||token.Length<20)
            throw new InvalidOperationException("Немає коду Worker.");
        var root=new Uri(new Uri(settings.WorkerUrl).GetLeftPart(UriPartial.Authority));
        using var request=new HttpRequestMessage(HttpMethod.Get,
            new Uri(root,"/device/v1/files/latest?target=computer"));
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancel);
        response.EnsureSuccessStatusCode();
        await using var input=await response.Content.ReadAsStreamAsync(cancel);
        using var bounded=new MemoryStream();
        var chunk=new byte[8192];
        int read;
        while((read=await input.ReadAsync(chunk,cancel))>0) {
            if(bounded.Length+read>1_500_000)
                throw new InvalidDataException("Відповідь Relay перевищила ліміт.");
            bounded.Write(chunk,0,read);
        }
        using var doc=JsonDocument.Parse(bounded.ToArray());
        var json=doc.RootElement;
        if(!json.GetProperty("available").GetBoolean())return null;
        var name=json.GetProperty("name").GetString()??"";
        if(name.Length is <1 or >100 || name=="." || name==".." ||
           name.Any(c=>char.IsControl(c)||Path.GetInvalidFileNameChars().Contains(c)))
            throw new InvalidDataException("Недопустиме ім'я отриманого файла.");
        if(!Guid.TryParse(json.GetProperty("id").GetString(),out var id))
            throw new InvalidDataException("Невідомий ID файла.");
        var encoded=json.GetProperty("data").GetString()??"";
        if(encoded.Length<4 || encoded.Length>1_398_104)
            throw new InvalidDataException("Файл перевищив ліміт.");
        var bytes=Convert.FromBase64String(encoded);
        if(bytes.Length is <1 or >MaxFileBytes)
            throw new InvalidDataException("Файл перевищив ліміт.");
        return new ReceivedFile(id,name,bytes);
    }
    public async Task ConfirmSavedAsync(Guid id,JarvisSettings settings,
        StateStore store,CancellationToken cancel) {
        var token=store.ReadWorkerToken();
        if(string.IsNullOrWhiteSpace(token) || !AiClient.ValidEndpoint(settings.WorkerUrl))
            throw new InvalidOperationException("Relay не налаштований.");
        var root=new Uri(new Uri(settings.WorkerUrl).GetLeftPart(UriPartial.Authority));
        using var request=new HttpRequestMessage(HttpMethod.Post,
            new Uri(root,"/device/v1/files/ack"));
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        request.Content=new StringContent(JsonSerializer.Serialize(new {
            id=id.ToString(),target="computer"
        }),Encoding.UTF8,"application/json");
        using var response=await _http.SendAsync(request,cancel);
        response.EnsureSuccessStatusCode();
    }
    public void Dispose()=>_http.Dispose();
}
