using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CampusFlow.AccountRecovery;

/// <summary>Environment-bound encrypted MSAL cache. The key remains in Azure settings, never alongside the file.</summary>
internal sealed class RecoveryTokenCache(WritebackRecoverySettings settings, string? root = null)
{
    private string DirectoryPath => Path.Combine(root ?? "/home/data/campusflow-password-recovery", settings.Environment, settings.KeyId);
    private string CachePath => Path.Combine(DirectoryPath, "delegated-cache.bin");
    internal bool Exists => File.Exists(CachePath) || settings.BootstrapCache.Length is > 0 and <= 2796204;
    internal async Task<IDisposable> LockAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var file = new FileStream(Path.Combine(DirectoryPath, "cache.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file.Name, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                return file;
            }
            catch (IOException) when (attempt < 39) { await Task.Delay(250, ct); }
        }
    }
    internal async Task<byte[]> ReadAsync(CancellationToken ct)
    {
        if (settings.BootstrapCache.Length > 2796204) throw new CryptographicException("Recovery authorization is unavailable.");
        var data = File.Exists(CachePath) ? await File.ReadAllBytesAsync(CachePath, ct) : Convert.FromBase64String(settings.BootstrapCache);
        if (data.Length is < 29 or > 2097152 || data[0] != 1) throw new CryptographicException("Recovery authorization is unavailable.");
        var output = new byte[data.Length - 29];
        using var aes = new AesGcm(settings.EncryptionKey!, 16);
        aes.Decrypt(data.AsSpan(1, 12), data.AsSpan(29), data.AsSpan(13, 16), output, Encoding.UTF8.GetBytes(settings.Context));
        return output;
    }
    internal async Task WriteAsync(byte[] data, CancellationToken ct)
    {
        if (data.Length is 0 or > 2097123) throw new CryptographicException("Recovery authorization is unavailable.");
        var output = new byte[data.Length + 29]; output[0] = 1; RandomNumberGenerator.Fill(output.AsSpan(1, 12));
        using var aes = new AesGcm(settings.EncryptionKey!, 16);
        aes.Encrypt(output.AsSpan(1, 12), data, output.AsSpan(29), output.AsSpan(13, 16), Encoding.UTF8.GetBytes(settings.Context));
        var temporary = CachePath + "." + Guid.NewGuid().ToString("N");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temporary, options)) { await stream.WriteAsync(output, ct); await stream.FlushAsync(ct); }
            File.Move(temporary, CachePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
