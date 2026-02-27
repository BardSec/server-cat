using System.Text.Json;

namespace ServerCat.Core.Entities;

/// <summary>
/// Stores the full inventory snapshot collected during a poll run.
/// JSONB columns (represented as JsonDocument) hold structured sub-data.
/// </summary>
public class PollResult
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PollId { get; set; }
    public Poll? Poll { get; set; }

    public Guid ServerId { get; set; }
    public Server? Server { get; set; }

    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;

    // ── Host identity ──────────────────────────────────────────────
    public string? HostnameResolved { get; set; }
    public string? Fqdn { get; set; }
    public string? Domain { get; set; }

    // ── OS ─────────────────────────────────────────────────────────
    public string? OsName { get; set; }
    public string? OsVersion { get; set; }
    public string? OsBuild { get; set; }
    public DateTime? InstallDate { get; set; }

    // ── Uptime ─────────────────────────────────────────────────────
    public DateTime? LastBootTime { get; set; }
    public long? UptimeSeconds { get; set; }

    // ── Hardware ───────────────────────────────────────────────────
    /// <summary>
    /// JSON: { "model": "...", "sockets": 2, "coresPerSocket": 8, "logicalProcessors": 32 }
    /// </summary>
    public JsonDocument? CpuInfo { get; set; }
    public long? MemoryTotalMb { get; set; }
    public long? MemoryAvailableMb { get; set; }

    // ── Storage ────────────────────────────────────────────────────
    /// <summary>
    /// JSON array: [{ "drive": "C:", "label": "System", "filesystem": "NTFS",
    ///   "sizeGb": 127.9, "freeGb": 45.2, "freePct": 35.3 }]
    /// </summary>
    public JsonDocument? DiskVolumes { get; set; }

    // ── Network ────────────────────────────────────────────────────
    /// <summary>
    /// JSON array: [{ "name": "Ethernet0", "ipAddress": "10.0.1.50",
    ///   "macAddress": "00:11:22:33:44:55", "defaultGateway": "10.0.1.1",
    ///   "dnsServers": ["10.0.0.10"] }]
    /// </summary>
    public JsonDocument? NetworkAdapters { get; set; }

    // ── Services ───────────────────────────────────────────────────
    /// <summary>
    /// JSON array: [{ "name": "W3SVC", "displayName": "World Wide Web...",
    ///   "status": "Running", "startType": "Automatic" }]
    /// </summary>
    public JsonDocument? Services { get; set; }

    // ── Patches ────────────────────────────────────────────────────
    public DateOnly? LastUpdateInstalled { get; set; }
    public bool? PendingReboot { get; set; }

    // ── Software (optional, expensive) ─────────────────────────────
    /// <summary>
    /// Optional. JSON array of installed software. Expensive to collect.
    /// Collection must be explicitly enabled per-server.
    /// </summary>
    public JsonDocument? InstalledSoftware { get; set; }

    // ── Debug ──────────────────────────────────────────────────────
    /// <summary>Raw collector output preserved for debugging and re-parsing.</summary>
    public JsonDocument? RawData { get; set; }
}
