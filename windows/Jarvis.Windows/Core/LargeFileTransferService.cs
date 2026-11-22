using System.Security.Cryptography;
using System.Text;
namespace Jarvis.Windows.Core;

/// <summary>
/// 1 MiB AEAD chunk codec for future R2/local transport.
/// The 32-byte AES key MUST come from authenticated per-device pairing.
/// No network upload is offered before that protocol is implemented.
/// </summary>
public static class LargeFileTransferService {
    public const int ChunkBytes=1_048_576;
    public const long MaxBytes=2L*1024*1024*1024;
    public sealed record Manifest(Guid TransferId,string FileName,long TotalBytes,
        int Chunks,string Sha256);
    public static bool SafeName(string? name) =>
        name is {Length:>0 and <=100} && name is not ("." or "..") &&
        !name.Any(x=>char.IsControl(x)||Path.GetInvalidFileNameChars().Contains(x));
    private static void ValidateKey(byte[] key) {
        if(key==null||key.Length!=32)
            throw new ArgumentException("Потрібен 256-бітний сеансовий ключ від pairing.");
    }
    private static string ChunkPath(string directory,Guid id,int index)=>
        Path.Combine(directory,$"{id:N}.{index:D5}.jfc");
    private static byte[] AAD(Guid id,int index,int length) {
        var aad=new byte[24];
        id.TryWriteBytes(aad.AsSpan(0,16));
        BitConverter.TryWriteBytes(aad.AsSpan(16,4),index);
        BitConverter.TryWriteBytes(aad.AsSpan(20,4),length);
        return aad;
    }
    public static async Task<Manifest> EncryptToDirectoryAsync(
            string source,string directory,byte[] pairingKey,
            IProgress<long>? progress,CancellationToken cancel) {
        ValidateKey(pairingKey);
        var name=Path.GetFileName(source);
        if(!SafeName(name))throw new InvalidDataException("Небезпечне ім'я файла.");
        var info=new FileInfo(source);
        if(!info.Exists||info.Length>MaxBytes)
            throw new InvalidDataException("Файл відсутній або перевищує 2 ГіБ.");
        Directory.CreateDirectory(directory);
        var id=Guid.NewGuid();
        var inputBytes=new byte[ChunkBytes];
        var encrypted=new byte[ChunkBytes];
        var nonce=new byte[12];var tag=new byte[16];
        var count=0;long done=0;
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try {
            await using var input=new FileStream(source,FileMode.Open,FileAccess.Read,
                FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.SequentialScan);
            using var aes=new AesGcm(pairingKey,16);
            while(true) {
                cancel.ThrowIfCancellationRequested();
                var filled=0;
                while(filled<inputBytes.Length) {
                    var read=await input.ReadAsync(inputBytes.AsMemory(filled),cancel);
                    if(read==0)break;
                    filled+=read;
                }
                if(filled==0)break;
                hash.AppendData(inputBytes.AsSpan(0,filled));
                RandomNumberGenerator.Fill(nonce);
                var aad=AAD(id,count,filled);
                aes.Encrypt(nonce,inputBytes.AsSpan(0,filled),
                    encrypted.AsSpan(0,filled),tag,aad);
                await using(var output=new FileStream(
                    ChunkPath(directory,id,count),FileMode.CreateNew,FileAccess.Write,
                    FileShare.None,65536,FileOptions.Asynchronous)) {
                    await output.WriteAsync(nonce,cancel);
                    await output.WriteAsync(tag,cancel);
                    await output.WriteAsync(encrypted.AsMemory(0,filled),cancel);
                    await output.FlushAsync(cancel);
                }
                count++;done+=filled;progress?.Report(done);
                CryptographicOperations.ZeroMemory(inputBytes.AsSpan(0,filled));
                CryptographicOperations.ZeroMemory(encrypted.AsSpan(0,filled));
            }
            // If the file changes while reading, reject a partial source snapshot.
            if(done!=info.Length)throw new IOException("Розмір вихідного файла змінився.");
            return new(id,name,done,count,Convert.ToHexString(hash.GetHashAndReset()));
        }catch {
            for(var j=0;j<=count;j++){
                var path=ChunkPath(directory,id,j);
                if(File.Exists(path))File.Delete(path);
            }
            throw;
        }finally {
            CryptographicOperations.ZeroMemory(inputBytes);
            CryptographicOperations.ZeroMemory(encrypted);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(tag);
        }
    }
    public static async Task RestoreAsync(Manifest manifest,string chunksFolder,
            string outputFile,byte[] pairingKey,IProgress<long>? progress,
            CancellationToken cancel) {
        ValidateKey(pairingKey);
        if(manifest.TransferId==Guid.Empty||!SafeName(manifest.FileName)||
           manifest.TotalBytes<0||manifest.TotalBytes>MaxBytes||
           manifest.Chunks!=(int)((manifest.TotalBytes+ChunkBytes-1)/ChunkBytes)||
           manifest.Sha256.Length!=64 ||
           !manifest.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Недійсний маніфест.");
        if(File.Exists(outputFile))
            throw new IOException("Цільовий файл існує. Обери іншу назву.");
        var temporary=outputFile+"."+manifest.TransferId.ToString("N")+".partial";
        var encrypted=new byte[ChunkBytes];
        var plaintext=new byte[ChunkBytes];
        var nonce=new byte[12];var tag=new byte[16];
        long restored=0;
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try {
            await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,
                FileShare.None,65536,FileOptions.Asynchronous)) {
                using var aes=new AesGcm(pairingKey,16);
                for(var i=0;i<manifest.Chunks;i++) {
                    cancel.ThrowIfCancellationRequested();
                    var expected=(int)Math.Min(ChunkBytes,manifest.TotalBytes-restored);
                    await using(var chunk=new FileStream(
                        ChunkPath(chunksFolder,manifest.TransferId,i),
                        FileMode.Open,FileAccess.Read,FileShare.Read,65536,
                        FileOptions.Asynchronous|FileOptions.SequentialScan)) {
                        if(chunk.Length!=expected+nonce.Length+tag.Length)
                            throw new InvalidDataException("Пошкоджений зашифрований фрагмент.");
                        await chunk.ReadExactlyAsync(nonce,cancel);
                        await chunk.ReadExactlyAsync(tag,cancel);
                        await chunk.ReadExactlyAsync(encrypted.AsMemory(0,expected),cancel);
                    }
                    aes.Decrypt(nonce,encrypted.AsSpan(0,expected),
                        tag,plaintext.AsSpan(0,expected),AAD(manifest.TransferId,i,expected));
                    hash.AppendData(plaintext.AsSpan(0,expected));
                    await output.WriteAsync(plaintext.AsMemory(0,expected),cancel);
                    restored+=expected;progress?.Report(restored);
                    CryptographicOperations.ZeroMemory(plaintext.AsSpan(0,expected));
                }
                await output.FlushAsync(cancel);
            }
            if(restored!=manifest.TotalBytes ||
               !Convert.ToHexString(hash.GetHashAndReset())
                  .Equals(manifest.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Контрольна сума SHA-256 не збіглася.");
            File.Move(temporary,outputFile,false);
        }catch {
            if(File.Exists(temporary))File.Delete(temporary);
            throw;
        }finally{
            CryptographicOperations.ZeroMemory(encrypted);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(tag);
        }
    }
}
