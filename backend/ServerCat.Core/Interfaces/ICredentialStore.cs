namespace ServerCat.Core.Interfaces;

/// <summary>
/// Abstraction for the secret vault.
/// Default implementation: DpapiCredentialStore (Windows DPAPI + vault.json).
/// Alternative implementations: HashiCorp Vault, CyberArk, Azure Key Vault.
/// </summary>
public interface ICredentialStore
{
    /// <summary>
    /// Encrypt and store a secret. Returns the opaque key for later retrieval.
    /// The caller must zero the plaintext secret after calling this method.
    /// </summary>
    Task<string> ProtectAsync(string plaintext);

    /// <summary>
    /// Retrieve and decrypt a secret by its opaque store key.
    /// The returned string should be zeroed after use by the caller.
    /// </summary>
    Task<string> UnprotectAsync(string storeKey);

    /// <summary>Delete a secret from the vault (called when a credential reference is deleted).</summary>
    Task DeleteAsync(string storeKey);

    /// <summary>Update an existing secret. Returns the same store key if successful.</summary>
    Task UpdateAsync(string storeKey, string newPlaintext);
}
