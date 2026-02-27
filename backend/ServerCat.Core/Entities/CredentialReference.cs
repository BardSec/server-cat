using ServerCat.Core.Enums;

namespace ServerCat.Core.Entities;

/// <summary>
/// Represents a reference to credentials stored in the secure vault.
/// IMPORTANT: No actual secrets are stored in this entity or the database.
/// The SecretStoreKey is an opaque lookup key for the vault only.
/// </summary>
public class CredentialReference
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Friendly name for display in the UI.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public AuthMethod AuthMethod { get; set; }

    /// <summary>
    /// Display hint only — shown in the UI so admins know which account is being used.
    /// Example: "CORP\svc-servercat-poll". Never used for authentication directly.
    /// </summary>
    public string? UsernameHint { get; set; }

    /// <summary>
    /// Opaque key used to look up the actual secret in the vault.
    /// Format: "vault:{guid}" — never contains the actual credential value.
    /// </summary>
    public string SecretStoreKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";

    // Navigation
    public ICollection<Server> Servers { get; set; } = new List<Server>();
}
