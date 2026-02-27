using System.Text.Json;

namespace ServerCat.Core.DTOs;

public class PollSummary
{
    public Guid Id { get; set; }
    public Guid ServerId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? CollectorType { get; set; }
    public string TriggeredBy { get; set; } = string.Empty;
    public string? TriggeredActor { get; set; }
    public int? DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PollResultDto
{
    public PollSummary Poll { get; set; } = new();

    // Host
    public string? HostnameResolved { get; set; }
    public string? Fqdn { get; set; }
    public string? Domain { get; set; }

    // OS
    public string? OsName { get; set; }
    public string? OsVersion { get; set; }
    public string? OsBuild { get; set; }
    public DateTime? InstallDate { get; set; }

    // Uptime
    public DateTime? LastBootTime { get; set; }
    public long? UptimeSeconds { get; set; }

    // Hardware
    public JsonElement? CpuInfo { get; set; }
    public long? MemoryTotalMb { get; set; }
    public long? MemoryAvailableMb { get; set; }

    // Storage
    public JsonElement? DiskVolumes { get; set; }

    // Network
    public JsonElement? NetworkAdapters { get; set; }

    // Services
    public JsonElement? Services { get; set; }

    // Patches
    public DateOnly? LastUpdateInstalled { get; set; }
    public bool? PendingReboot { get; set; }
}

public class TriggerPollResponse
{
    public Guid PollId { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime QueuedAt { get; set; }
}
