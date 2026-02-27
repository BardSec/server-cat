using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerCat.Core.Interfaces;

namespace ServerCat.Infrastructure.Security;

/// <summary>
/// Credential store backed by Windows DPAPI + a local JSON vault file.
///
/// DPAPI scope: DPAPI_LOCAL_MACHINE (tied to the machine + service account entropy).
/// This means vault.json cannot be decrypted on a different machine without the
/// entropy file. See VaultOptions.EntropyFilePath.
///
/// Security properties:
/// - vault.json contains only DPAPI-encrypted ciphertext (Base64).
/// - NTFS ACL must restrict vault.json to the service account only.
/// - If the machine changes, you must re-import all credentials via admin tooling.
///
/// ASSUMPTION: Running on Windows. On Linux, this class will throw.
/// Use an alternate ICredentialStore implementation for cross-platform deployments.
/// </summary>
[SupportedOSPlatform("windows")]
public class DpapiCredentialStore : ICredentialStore
{
    private readonly VaultOptions _options;
    private readonly ILogger<DpapiCredentialStore> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private byte[]? _entropy;

    public DpapiCredentialStore(IOptions<VaultOptions> options, ILogger<DpapiCredentialStore> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> ProtectAsync(string plaintext)
    {
        var storeKey = $"vault:{Guid.NewGuid()}";
        var ciphertext = EncryptWithDpapi(plaintext);

        var vault = await LoadVaultAsync();
        vault[storeKey] = ciphertext;
        await SaveVaultAsync(vault);

        _logger.LogInformation("Stored new credential entry with key prefix vault:...");
        return storeKey;
    }

    public async Task<string> UnprotectAsync(string storeKey)
    {
        var vault = await LoadVaultAsync();

        if (!vault.TryGetValue(storeKey, out var ciphertext))
            throw new KeyNotFoundException($"Credential key not found in vault: {storeKey}");

        return DecryptWithDpapi(ciphertext);
    }

    public async Task DeleteAsync(string storeKey)
    {
        var vault = await LoadVaultAsync();
        if (vault.Remove(storeKey))
        {
            await SaveVaultAsync(vault);
            _logger.LogInformation("Deleted credential entry from vault.");
        }
    }

    public async Task UpdateAsync(string storeKey, string newPlaintext)
    {
        var ciphertext = EncryptWithDpapi(newPlaintext);
        var vault = await LoadVaultAsync();

        if (!vault.ContainsKey(storeKey))
            throw new KeyNotFoundException($"Credential key not found in vault: {storeKey}");

        vault[storeKey] = ciphertext;
        await SaveVaultAsync(vault);
        _logger.LogInformation("Updated credential entry in vault.");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private string EncryptWithDpapi(string plaintext)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        // DataProtectionScope.LocalMachine: encrypted per-machine (not per-user).
        // Add entropy to bind to this application specifically.
        var encrypted = ProtectedData.Protect(data, GetEntropy(), DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(encrypted);
    }

    private string DecryptWithDpapi(string ciphertext)
    {
        var data = Convert.FromBase64String(ciphertext);
        var decrypted = ProtectedData.Unprotect(data, GetEntropy(), DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(decrypted);
    }

    private byte[] GetEntropy()
    {
        if (_entropy != null) return _entropy;

        // Use a per-application entropy file to bind decryption to this application.
        // The entropy file itself must be protected by NTFS ACL.
        if (!string.IsNullOrEmpty(_options.EntropyFilePath) && File.Exists(_options.EntropyFilePath))
        {
            _entropy = File.ReadAllBytes(_options.EntropyFilePath);
        }
        else
        {
            // Generate on first run and persist.
            _entropy = RandomNumberGenerator.GetBytes(32);
            if (!string.IsNullOrEmpty(_options.EntropyFilePath))
            {
                var dir = Path.GetDirectoryName(_options.EntropyFilePath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.WriteAllBytes(_options.EntropyFilePath, _entropy);
                _logger.LogWarning("Generated new vault entropy file at {Path}. Back this up!", _options.EntropyFilePath);
            }
        }
        return _entropy;
    }

    private async Task<Dictionary<string, string>> LoadVaultAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            if (!File.Exists(_options.VaultFilePath))
                return new Dictionary<string, string>();

            var json = await File.ReadAllTextAsync(_options.VaultFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task SaveVaultAsync(Dictionary<string, string> vault)
    {
        await _fileLock.WaitAsync();
        try
        {
            var dir = Path.GetDirectoryName(_options.VaultFilePath);
            if (dir != null) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(vault, new JsonSerializerOptions { WriteIndented = false });

            // Write to temp file then rename for atomic update
            var tmpPath = _options.VaultFilePath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json);
            File.Move(tmpPath, _options.VaultFilePath, overwrite: true);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}

public class VaultOptions
{
    public const string SectionName = "Vault";

    /// <summary>Path to the encrypted vault JSON file.</summary>
    public string VaultFilePath { get; set; } = "vault/vault.json";

    /// <summary>
    /// Path to the binary entropy file used to bind DPAPI encryption to this app.
    /// Must be protected by NTFS ACL (service account only).
    /// </summary>
    public string EntropyFilePath { get; set; } = "vault/vault.entropy";
}
