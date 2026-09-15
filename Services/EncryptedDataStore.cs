using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Util.Store;

namespace CalendarFlyout.Services;

/// <summary>IDataStore com DPAPI: access token e refresh token criptografados para o usuário Windows.</summary>
public sealed class EncryptedDataStore : IDataStore
{
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public EncryptedDataStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    private string PathFor<T>(string key) => Path.Combine(_directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(typeof(T).FullName + ":" + key))) + ".bin");

    public async Task StoreAsync<T>(string key, T value)
    {
        await _gate.WaitAsync();
        var path = PathFor<T>(key);
        var temp = path + ".tmp";
        byte[]? plain = null;
        try
        {
            plain = JsonSerializer.SerializeToUtf8Bytes(value);
            var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            // O arquivo temporário também contém apenas dados criptografados.
            await File.WriteAllBytesAsync(temp, encrypted);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            try { if (File.Exists(temp)) File.Delete(temp); }
            finally { _gate.Release(); }
        }
    }

    public async Task<T> GetAsync<T>(string key)
    {
        await _gate.WaitAsync();
        byte[]? plain = null;
        try
        {
            var path = PathFor<T>(key);
            if (!File.Exists(path)) return default!;
            plain = ProtectedData.Unprotect(await File.ReadAllBytesAsync(path), null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<T>(plain)!;
        }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            _gate.Release();
        }
    }

    public async Task DeleteAsync<T>(string key)
    {
        await _gate.WaitAsync();
        try { File.Delete(PathFor<T>(key)); }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync()
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var path in Directory.EnumerateFiles(_directory, "*.bin")) File.Delete(path);
        }
        finally { _gate.Release(); }
    }
}
