using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Jarvis.Windows.Core;

/** Shared Cloudflare JARVIS Worker protocol; never expose an OpenAI API key. */
public sealed class AiClient : IDisposable {
    private readonly HttpClient _http=new() { Timeout=TimeSpan.FromSeconds(95) };
    private readonly List<(string Role,string Content)> _history=new();
    private int _generation;

    public void ClearHistory() { _generation++;_history.Clear(); }

    public static bool ValidEndpoint(string? raw) {
        return Uri.TryCreate(raw,UriKind.Absolute,out var u) &&
            u.Scheme==Uri.UriSchemeHttps && u.AbsolutePath=="/chat" &&
            string.IsNullOrEmpty(u.Query) && string.IsNullOrEmpty(u.Fragment) &&
            u.UserInfo.Length==0 && !u.IsLoopback;
    }
    public async Task<string> AskAsync(string endpoint,string token,string question,
            string mode="chat",CancellationToken cancel=default,
            byte[]? jpegScreenshot=null,bool ephemeral=false) {
        if(!ValidEndpoint(endpoint))throw new InvalidOperationException("Потрібен HTTPS /chat сервер.");
        if(token.Length<20)throw new InvalidOperationException("Налаштуй код доступу до Worker.");
        if(string.IsNullOrWhiteSpace(question) || question.Length>4000)
            throw new ArgumentException("Текст має бути від 1 до 4000 символів.");
        if(mode is not ("chat" or "web" or "image" or "analysis"))
            throw new ArgumentException("Невідомий режим ШІ.");
        if(jpegScreenshot!=null && (jpegScreenshot.Length is <100 or >220000 ||
           jpegScreenshot[0]!=0xFF || jpegScreenshot[1]!=0xD8 ||
           jpegScreenshot[^2]!=0xFF || jpegScreenshot[^1]!=0xD9))
            throw new ArgumentException("Неправильний або завеликий JPEG екрана.");
        if(jpegScreenshot!=null && mode!="analysis")
            throw new ArgumentException("Знімок екрана надсилається лише в режимі аналізу.");
        var oldGeneration=_generation;
        var previous=(ephemeral?new List<(string Role,string Content)>():_history).TakeLast(14)
            .Select(t=>new {role=t.Role,content=t.Content}).ToArray();
        var payload=JsonSerializer.Serialize(new {
            message=question, mode, effort="medium", history=previous,
            attachment=jpegScreenshot==null?null:new {
                name="windows-screen.jpg",mime="image/jpeg",
                data=Convert.ToBase64String(jpegScreenshot)
            }
        });
        using var request=new HttpRequestMessage(HttpMethod.Post,endpoint);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        request.Content=new StringContent(payload,Encoding.UTF8,"application/json");
        using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancel);
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Неправильний код доступу до Worker.");
        if((int)response.StatusCode==429)
            throw new InvalidOperationException("Ліміт API вичерпано; перевір баланс на сервері.");
        response.EnsureSuccessStatusCode();
        await using var stream=await response.Content.ReadAsStreamAsync(cancel);
        using var doc=await JsonDocument.ParseAsync(stream,new JsonDocumentOptions {
            MaxDepth=32
        },cancel);
        string reply=doc.RootElement.TryGetProperty("reply",out var result)
            ?result.GetString()??"": "";
        if(reply.Length==0 || reply.Length>12000)
            throw new InvalidDataException("Сервер повернув порожню або надмірну відповідь.");
        if(!ephemeral && _generation==oldGeneration) {
            _history.Add(("user",question));
            _history.Add(("assistant",reply[..Math.Min(5000,reply.Length)]));
            if(_history.Count>14)_history.RemoveRange(0,_history.Count-14);
        }
        return reply;
    }
    public void Dispose()=>_http.Dispose();
}
