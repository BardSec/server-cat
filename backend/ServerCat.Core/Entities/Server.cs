using ServerCat.Core.Enums;

namespace ServerCat.Core.Entities;

public class Server
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Canonical hostname (FQDN or short name). Must be unique.</summary>
    public string Hostname { get; set; } = string.Empty;

    /// <summary>Primary IP address. Optional — hostname is used for connectivity if set.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Human-readable display name. Defaults to Hostname if not set.</summary>
    public string? DisplayName { get; set; }

    public ServerEnvironment Environment { get; set; } = ServerEnvironment.Production;

    /// <summary>Team or individual responsible for this server.</summary>
    public string? Owner { get; set; }

    /// <summary>Physical or logical site (e.g., "DC-East", "HQ", "Remote-DR").</summary>
    public string? Site { get; set; }

    public Criticality Criticality { get; set; } = Criticality.Medium;

    public OsType OsType { get; set; } = OsType.Windows;

    /// <summary>Which collector to use: winrm, wmi, snmp.</summary>
    public string CollectorType { get; set; } = "winrm";

    public Guid? CredentialRefId { get; set; }
    public CredentialReference? CredentialRef { get; set; }

    public bool PollingEnabled { get; set; } = true;

    /// <summary>How often to poll this server, in minutes. Range: 5–10080 (1 week).</summary>
    public int PollingIntervalMinutes { get; set; } = 60;

    /// <summary>Free-form tags for filtering (e.g., ["web", "iis", "sql"]).</summary>
    public string[] Tags { get; set; } = Array.Empty<string>();

    /// <summary>When false, server is soft-deleted. Historical data is preserved.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "system";

    // Navigation
    public ICollection<Poll> Polls { get; set; } = new List<Poll>();
    public ICollection<Note> Notes { get; set; } = new List<Note>();
    public ICollection<OperationalLog> OperationalLogs { get; set; } = new List<OperationalLog>();
    public ICollection<ServerGroupMembership> GroupMemberships { get; set; } = new List<ServerGroupMembership>();
}
