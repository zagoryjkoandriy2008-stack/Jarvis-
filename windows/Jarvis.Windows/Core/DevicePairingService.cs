using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jarvis.Windows.Core;

/// <summary>
/// Owner-approved P-256 device identity. Private key is encrypted using
/// Windows DPAPI, never transmitted to Cloudflare or a phone.
/// Pairing does NOT automatically upgrade legacy /device/v1 commands.
/// </summary>
public sealed class DevicePairingService : IDisposable {
    private readonly HttpClient _http=new() {Timeout=TimeSpan.FromSeconds(20)};
    private readonly StateStore _store;
    private readonly JarvisSettings _settings;
    private readonly string _identityFile=Path.Combine(StateStore.DirectoryPath,"device-identity-v2.dpapi");
    public DevicePairingService(StateStore store,JarvisSettings settings){
        _store=store;_settings=settings;
    }
    private ECDsa OpenIdentity() {
        Directory.CreateDirectory(StateStore.DirectoryPath);
        if(File.Exists(_identityFile)) {
            byte[] privateKey=ProtectedData.Unprotect(File.ReadAllBytes(_identityFile),
                null,DataProtectionScope.CurrentUser);
            try{
                var key=ECDsa.Create();
                key.ImportPkcs8PrivateKey(privateKey,out _);
                return key;
            }finally{CryptographicOperations.ZeroMemory(privateKey);}
        }
        using var created=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bytes=created.ExportPkcs8PrivateKey();
        try{
            var encrypted=ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser);
            var temp=_identityFile+".tmp";
            using(var fs=new FileStream(temp,FileMode.CreateNew,
                FileAccess.Write,FileShare.None))fs.Write(encrypted);
            File.Move(temp,_identityFile,false);
            var loaded=ECDsa.Create();
            loaded.ImportPkcs8PrivateKey(bytes,out _);
            return loaded;
        }finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    public static string CanonicalRequest(string method,string path,string device,
            string timestamp,string nonce,string rawJson) =>
        string.Join("\n",method,path,device,timestamp,nonce,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJson)))
                .ToLowerInvariant());
    public static string Sign(ECDsa key,string canonical) =>
        Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(canonical),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    public static string PublicKeyBase64(ECDsa key) =>
        Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    private async Task<JsonDocument> PostAsync(string path,object payload,
            bool signed,CancellationToken cancel) {
        if(!AiClient.ValidEndpoint(_settings.WorkerUrl))
            throw new InvalidOperationException("Потрібна налаштована адреса HTTPS /chat.");
        var bearer=_store.ReadWorkerToken();
        if(string.IsNullOrWhiteSpace(bearer))
            throw new InvalidOperationException("Немає Worker-токена.");
        var root=new Uri(new Uri(_settings.WorkerUrl).GetLeftPart(UriPartial.Authority));
        var raw=JsonSerializer.Serialize(payload);
        using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(root,path));
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
        request.Content=new StringContent(raw,Encoding.UTF8,"application/json");
        if(signed) {
            using var key=OpenIdentity();
            var timestamp=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            var nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var canonical=CanonicalRequest("POST",path,"computer",timestamp,nonce,raw);
            request.Headers.TryAddWithoutValidation("X-Jarvis-Device","computer");
            request.Headers.TryAddWithoutValidation("X-Jarvis-Timestamp",timestamp);
            request.Headers.TryAddWithoutValidation("X-Jarvis-Nonce",nonce);
            request.Headers.TryAddWithoutValidation("X-Jarvis-Signature",Sign(key,canonical));
        }
        using var response=await _http.SendAsync(request,cancel);
        if(!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Pairing Worker відхилив запит: HTTP "+(int)response.StatusCode);
        await using var stream=await response.Content.ReadAsStreamAsync(cancel);
        return await JsonDocument.ParseAsync(stream,cancellationToken:cancel);
    }
    public Task<JsonDocument> SignedPostAsync(string path,object body,CancellationToken cancel) =>
        PostAsync(path,body,signed:true,cancel);
    public sealed record PhoneGrants(bool ReadOnly,bool Media,bool Apps,
        bool Locks,bool WatchDelegation);
    private static PhoneGrants ParseGrants(JsonElement root) {
        var v=root.GetProperty("permissions");
        return new(v.GetProperty("readOnly").GetBoolean(),
            v.GetProperty("media").GetBoolean(),v.GetProperty("apps").GetBoolean(),
            v.GetProperty("locks").GetBoolean(),
            v.GetProperty("watchDelegation").GetBoolean());
    }
    public async Task<PhoneGrants> GetGrantsAsync(CancellationToken cancel) {
        using var data=await PostAsync("/device/v2/pair/grants/get",
            new {device="phone"},signed:true,cancel);
        return ParseGrants(data.RootElement);
    }
    public async Task<PhoneGrants> SetGrantsAsync(PhoneGrants grants,CancellationToken cancel) {
        using var data=await PostAsync("/device/v2/pair/grants/set",
            new {device="phone",permissions=new {
                readOnly=grants.ReadOnly,media=grants.Media,apps=grants.Apps,
                locks=grants.Locks,watchDelegation=grants.WatchDelegation
            }},signed:true,cancel);
        return ParseGrants(data.RootElement);
    }
    public sealed record PairingStart(Guid Id,string Code,int ExpiresMs);
    public sealed record PendingState(string State,string? PhoneFingerprint,int ExpiresMs);
    public async Task<PairingStart> BeginAsync(CancellationToken cancel) {
        using var key=OpenIdentity();
        using var data=await PostAsync("/device/v2/pair/begin",new {
            device="computer",publicKey=PublicKeyBase64(key)
        },signed:true,cancel);
        var root=data.RootElement;
        return new(Guid.Parse(root.GetProperty("pairingId").GetString()!),
            root.GetProperty("code").GetString()??"",
            root.GetProperty("expiresInMs").GetInt32());
    }
    public async Task<PendingState> PendingAsync(Guid id,CancellationToken cancel) {
        using var data=await PostAsync("/device/v2/pair/pending",
            new {pairingId=id.ToString()},signed:true,cancel);
        var r=data.RootElement;
        return new(r.GetProperty("state").GetString()??"",
            r.GetProperty("phoneFingerprint").ValueKind==JsonValueKind.Null?
                null:r.GetProperty("phoneFingerprint").GetString(),
            r.GetProperty("expiresInMs").GetInt32());
    }
    public async Task<bool> ApproveAsync(Guid id,bool approve,CancellationToken cancel) {
        using var data=await PostAsync("/device/v2/pair/approve",
            new {pairingId=id.ToString(),approve},signed:true,cancel);
        return data.RootElement.TryGetProperty("approved",out var v)&&v.GetBoolean();
    }
    public async Task<bool> RevokePhoneAsync(CancellationToken cancel) {
        using var data=await PostAsync("/device/v2/pair/revoke",
            new {device="phone"},signed:true,cancel);
        return data.RootElement.GetProperty("revoked").GetBoolean();
    }
    public async Task<string> ListAsync(CancellationToken cancel) {
        using var data=await PostAsync("/device/v2/pair/list",
            new {},signed:true,cancel);
        var devices=data.RootElement.GetProperty("devices");
        return string.Join("\n",devices.EnumerateArray().Select(d=>
            d.GetProperty("name").GetString()+" · "+
            (d.GetProperty("enabled").GetBoolean()?"дозволено":"відкликано")));
    }
    public void Dispose()=>_http.Dispose();
}
