using System.ComponentModel.DataAnnotations;
using ServerCat.Core.Enums;

namespace ServerCat.Core.DTOs;

// ── Request DTOs ──────────────────────────────────────────────────────────────

public class CreateServerRequest
{
    [Required, StringLength(255, MinimumLength = 1)]
    public string Hostname { get; set; } = string.Empty;

    [RegularExpression(@"^(\d{1,3}\.){3}\d{1,3}$", ErrorMessage = "Invalid IP address format")]
    public string? IpAddress { get; set; }

    [StringLength(255)]
    public string? DisplayName { get; set; }

    [Required]
    public ServerEnvironment Environment { get; set; } = ServerEnvironment.Production;

    [StringLength(255)]
    public string? Owner { get; set; }

    [StringLength(100)]
    public string? Site { get; set; }

    public Criticality Criticality { get; set; } = Criticality.Medium;

    public OsType OsType { get; set; } = OsType.Windows;

    [Required, StringLength(50)]
    public string CollectorType { get; set; } = "winrm";

    public Guid? CredentialRefId { get; set; }

    public bool PollingEnabled { get; set; } = true;

    [Range(5, 10080)]
    public int PollingIntervalMinutes { get; set; } = 60;

    public string[] Tags { get; set; } = Array.Empty<string>();
}

public class UpdateServerRequest
{
    [StringLength(255)]
    public string? DisplayName { get; set; }

    public ServerEnvironment? Environment { get; set; }

    [StringLength(255)]
    public string? Owner { get; set; }

    [StringLength(100)]
    public string? Site { get; set; }

    public Criticality? Criticality { get; set; }

    [StringLength(50)]
    public string? CollectorType { get; set; }

    public Guid? CredentialRefId { get; set; }

    public bool? PollingEnabled { get; set; }

    [Range(5, 10080)]
    public int? PollingIntervalMinutes { get; set; }

    public string[]? Tags { get; set; }
}

public class ServerListQuery
{
    public ServerEnvironment? Environment { get; set; }
    public Criticality? Criticality { get; set; }
    public string? Site { get; set; }
    public ServerStatus? Status { get; set; }
    public string? Search { get; set; }
    public string[]? Tags { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string SortBy { get; set; } = "hostname";
    public string SortDir { get; set; } = "asc";
}

// ── Response DTOs ─────────────────────────────────────────────────────────────

public class ServerListItem
{
    public Guid Id { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? IpAddress { get; set; }
    public string Environment { get; set; } = string.Empty;
    public string Criticality { get; set; } = string.Empty;
    public string? Site { get; set; }
    public string? Owner { get; set; }
    public string? OsName { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public bool PollingEnabled { get; set; }
    public int PollingIntervalMinutes { get; set; }
    public string Status { get; set; } = "gray";
    public DateTime? LastPollAt { get; set; }
    public string? LastPollStatus { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ServerDetail : ServerListItem
{
    public OsType OsType { get; set; }
    public string CollectorType { get; set; } = string.Empty;
    public CredentialRefSummary? CredentialRef { get; set; }
    public List<GroupSummary> Groups { get; set; } = new();
    public DateTime UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

public class CredentialRefSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string AuthMethod { get; set; } = string.Empty;
    public string? UsernameHint { get; set; }
}

public class GroupSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
