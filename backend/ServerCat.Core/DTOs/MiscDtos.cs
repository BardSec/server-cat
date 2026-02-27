using System.ComponentModel.DataAnnotations;

namespace ServerCat.Core.DTOs;

// ── Notes ─────────────────────────────────────────────────────────────────────

public class CreateNoteRequest
{
    [Required, MinLength(1)]
    public string Content { get; set; } = string.Empty;
    public bool IsPinned { get; set; } = false;
}

public class UpdateNoteRequest
{
    public string? Content { get; set; }
    public bool? IsPinned { get; set; }
}

public class NoteDto
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
}

// ── Operational Logs ──────────────────────────────────────────────────────────

public class OperationalLogDto
{
    public Guid Id { get; set; }
    public Guid? ServerId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public object? Details { get; set; }
    public string Actor { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

// ── Credentials ───────────────────────────────────────────────────────────────

public class CreateCredentialRequest
{
    [Required, StringLength(255, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    public string AuthMethod { get; set; } = string.Empty;

    public string? UsernameHint { get; set; }

    /// <summary>
    /// The actual secret value. Encrypted immediately on receipt.
    /// Never stored, logged, or returned in responses.
    /// </summary>
    [Required]
    public string SecretValue { get; set; } = string.Empty;
}

public class UpdateCredentialRequest
{
    public string? Description { get; set; }
    public string? UsernameHint { get; set; }
    public string? NewSecretValue { get; set; }
}

public class CredentialDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AuthMethod { get; set; } = string.Empty;
    public string? UsernameHint { get; set; }
    public int ServersUsingCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

// ── Groups ────────────────────────────────────────────────────────────────────

public class CreateGroupRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    [RegularExpression(@"^#[0-9A-Fa-f]{6}$", ErrorMessage = "Color must be a valid hex color (e.g., #3B82F6)")]
    public string Color { get; set; } = "#6B7280";
}

public class GroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Color { get; set; } = string.Empty;
    public int ServerCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ── Dashboard ─────────────────────────────────────────────────────────────────

public class DashboardSummary
{
    public int TotalServers { get; set; }
    public int ActiveServers { get; set; }
    public Dictionary<string, int> ByStatus { get; set; } = new();
    public Dictionary<string, int> ByCriticality { get; set; } = new();
    public Dictionary<string, int> ByEnvironment { get; set; } = new();
    public List<RecentFailure> RecentFailures { get; set; } = new();
    public int PollsLast24H { get; set; }
    public int FailedPollsLast24H { get; set; }
}

public class RecentFailure
{
    public Guid ServerId { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public DateTime FailedAt { get; set; }
    public string? Error { get; set; }
}

// ── Connectivity Test ─────────────────────────────────────────────────────────

public class ConnectivityTestRequest
{
    [Required]
    public string CollectorType { get; set; } = string.Empty;
    public Guid? CredentialRefId { get; set; }
}
