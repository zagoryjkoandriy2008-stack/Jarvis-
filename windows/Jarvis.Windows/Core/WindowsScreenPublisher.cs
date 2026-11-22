using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows.Forms;

namespace Jarvis.Windows.Core;

/// <summary>
/// Explicit user-consented view-only sharing: Windows captures primary monitor
/// into RAM only while a paired Pixel actively requests frames. It sends JPEG
/// snapshots to the user's private HTTPS Worker, never to AI and never to disk.
/// No capture while owner setting is off, no autonomous remote screen control.
/// </summary>
public sealed class WindowsScreenPublisher : IDisposable {
    private readonly JarvisSettings _settings;
    private readonly StateStore _store;
    private readonly HttpClient _http = new() {Timeout=TimeSpan.FromSeconds(10)};
    private bool _sharing;
    private string? _lastSession;
    private bool _lastPermission;
    public event Action<bool,string>? ShareStatus;
    private static readonly int[] CaptureWidths={960,800,640,480};
    private static readonly long[] Qualities={58,42,32};
    public bool IsSharing=>_sharing;

    public WindowsScreenPublisher(JarvisSettings settings,StateStore store) {
        _settings=settings;_store=store;
    }

    private (Uri Root,string Token) Connection() {
        if(!AiClient.ValidEndpoint(_settings.WorkerUrl))
            throw new InvalidOperationException("Спочатку налаштуй HTTPS Cloudflare Worker.");
        var token=_store.ReadWorkerToken();
        if(string.IsNullOrWhiteSpace(token)||token.Length<20)
            throw new InvalidOperationException("Немає коду доступу до Worker.");
        return (new Uri(new Uri(_settings.WorkerUrl).GetLeftPart(UriPartial.Authority)),token);
    }

    private void Report(bool streaming,string detail) {
        if(_sharing==streaming && (_lastPermission ||
            !_settings.EnableScreenShare))return;
        _sharing=streaming;
        _lastPermission=_settings.EnableScreenShare;
        ShareStatus?.Invoke(streaming,detail);
    }

    public async Task RunAsync(CancellationToken cancellation) {
        while(!cancellation.IsCancellationRequested) {
            try {
                if(!_settings.EnableDeviceRelay||!_settings.EnableScreenShare) {
                    if(_lastSession!=null)await RevokeAsync();
                    if(_sharing)Report(false,"Передавання екрана вимкнено.");
                    await Task.Delay(1500,cancellation);
                    continue;
                }
                var (root,token)=Connection();
                using(var stateRequest=new HttpRequestMessage(HttpMethod.Get,
                    new Uri(root,"/device/v1/screen/state"))) {
                    stateRequest.Headers.Authorization=
                        new AuthenticationHeaderValue("Bearer",token);
                    using var response=await _http.SendAsync(stateRequest,cancellation);
                    if(response.StatusCode==HttpStatusCode.ServiceUnavailable)
                        throw new InvalidOperationException(
                            "Cloudflare Screen Relay ще не розгорнуто.");
                    response.EnsureSuccessStatusCode();
                    using var json=JsonDocument.Parse(
                        await response.Content.ReadAsStringAsync(cancellation));
                    if(!json.RootElement.GetProperty("active").GetBoolean()) {
                        _lastSession=null;
                        if(_sharing)Report(false,"Телефон завершив перегляд.");
                        await Task.Delay(2500,cancellation);
                        continue;
                    }
                    var session=json.RootElement.GetProperty("session").GetString();
                    if(!Guid.TryParse(session,out _))
                        throw new InvalidDataException("Неправильна сесія перегляду.");
                    // Source consent is checked again immediately before
                    // capturing, including when changed in Windows Settings.
                    if(!_settings.EnableDeviceRelay||!_settings.EnableScreenShare)
                        continue;
                    _lastSession=session;
                    var jpeg=CapturePrimaryScreen();
                    if(!_settings.EnableDeviceRelay||!_settings.EnableScreenShare) {
                        await RevokeAsync();
                        continue;
                    }
                    using var upload=new HttpRequestMessage(HttpMethod.Post,
                        new Uri(root,"/device/v1/screen/frame?session="+session));
                    upload.Headers.Authorization=
                        new AuthenticationHeaderValue("Bearer",token);
                    upload.Content=new ByteArrayContent(jpeg);
                    upload.Content.Headers.ContentType=new MediaTypeHeaderValue("image/jpeg");
                    using var accepted=await _http.SendAsync(upload,cancellation);
                    if(accepted.IsSuccessStatusCode) {
                        Report(true,"🔴 Передаю екран Pixel через приватний JARVIS.");
                    } else {
                        if(_sharing)Report(false,"Перегляд зупинено.");
                    }
                }
            }catch(OperationCanceledException) {break;}
             catch(Exception error) {
                if(_sharing)Report(false,"Передавання екрана зупинено.");
                ShareStatus?.Invoke(false,"Екран Windows: "+error.Message);
            }
            try {await Task.Delay(1050,cancellation);}
            catch(OperationCanceledException) {break;}
        }
        if(_sharing)Report(false,"Трансляцію завершено.");
    }

    /** An owner local-stop purges Cloudflare's last frame immediately, not
     * after the five-second snapshot TTL. A disconnected client still expires. */
    public async Task RevokeAsync() {
        var session=Interlocked.Exchange(ref _lastSession,null);
        if(string.IsNullOrEmpty(session))return;
        try {
            var (root,token)=Connection();
            using var request=new HttpRequestMessage(HttpMethod.Post,
                new Uri(root,"/device/v1/screen/revoke"));
            request.Headers.Authorization=
                new AuthenticationHeaderValue("Bearer",token);
            request.Content=new StringContent(
                JsonSerializer.Serialize(new {session}),System.Text.Encoding.UTF8,
                "application/json");
            using var response=await _http.SendAsync(request);
            if(!response.IsSuccessStatusCode)
                ShareStatus?.Invoke(false,
                    "Екран вимкнено локально; Cloudflare очистить кадр після TTL.");
        } catch(Exception) {
            ShareStatus?.Invoke(false,
                "Екран вимкнено локально; кадр у Cloudflare автоматично застаріє.");
        }
    }

    private static byte[] CapturePrimaryScreen() {
        var screen=Screen.PrimaryScreen ??
            throw new InvalidOperationException("Не знайдено головний монітор.");
        var bounds=screen.Bounds;
        if(bounds.Width<100||bounds.Height<100||
           bounds.Width>12000||bounds.Height>12000)
            throw new InvalidOperationException("Непідтримувана роздільність екрана.");
        using var full=new Bitmap(bounds.Width,bounds.Height,
            PixelFormat.Format24bppRgb);
        using(var g=Graphics.FromImage(full)) {
            g.CopyFromScreen(bounds.Left,bounds.Top,0,0,
                new System.Drawing.Size(bounds.Width,bounds.Height));
        }
        var encoder=ImageCodecInfo.GetImageEncoders()
            .First(x=>x.MimeType=="image/jpeg");
        foreach(var targetWidth in CaptureWidths) {
            var width=Math.Min(bounds.Width,targetWidth);
            var height=Math.Max(1,(int)Math.Round(
                bounds.Height*((double)width/bounds.Width)));
            using var small=new Bitmap(width,height,PixelFormat.Format24bppRgb);
            using(var graphics=Graphics.FromImage(small)) {
                graphics.InterpolationMode=InterpolationMode.HighQualityBilinear;
                graphics.DrawImage(full,0,0,width,height);
            }
            foreach(var quality in Qualities) {
                using var buffer=new MemoryStream();
                using var parameters=new EncoderParameters(1);
                parameters.Param[0]=new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality,quality);
                small.Save(buffer,encoder,parameters);
                if(buffer.Length<=220_000) return buffer.ToArray();
            }
        }
        throw new InvalidOperationException(
            "Зображення екрана завелике для захищеного перегляду.");
    }
    public void Dispose()=>_http.Dispose();
}
