namespace ServerCat.Core.Interfaces;

/// <summary>
/// Contract that all data collectors must implement.
/// Each collector handles one transport/protocol (WinRM, WMI, SNMP).
/// </summary>
public interface ICollector
{
    /// <summary>Short identifier for this collector type (e.g., "winrm", "wmi", "snmp").</summary>
    string CollectorType { get; }

    /// <summary>
    /// Collect full inventory from the target server.
    /// Must not throw; wrap internal errors into CollectorResult.IsSuccess = false.
    /// </summary>
    Task<CollectorResult> CollectAsync(CollectorTarget target, CancellationToken ct = default);

    /// <summary>
    /// Test basic connectivity to the target without performing a full collection.
    /// Returns structured check results for the connectivity test UI.
    /// </summary>
    Task<ConnectivityTestResult> TestConnectivityAsync(CollectorTarget target, CancellationToken ct = default);
}

/// <summary>
/// Input context passed to a collector for each poll run.
/// Credentials are provided as a resolved value — never stored after the poll.
/// </summary>
public class CollectorTarget
{
    public Guid ServerId { get; init; }
    public string Hostname { get; init; } = string.Empty;
    public string? IpAddress { get; init; }
    public string CollectorType { get; init; } = string.Empty;

    /// <summary>
    /// Resolved credential object. Zero/dispose after use.
    /// Never log this object.
    /// </summary>
    public ResolvedCredential Credential { get; init; } = new();
}

/// <summary>
/// Resolved credential in memory. Dispose after use to clear from heap.
/// </summary>
public class ResolvedCredential : IDisposable
{
    public string? Username { get; init; }

    /// <summary>
    /// Plaintext password/key. Store in a pinned char array if possible.
    /// Cleared on Dispose().
    /// </summary>
    public string? Secret { get; set; }

    public string? SnmpCommunity { get; init; }
    public string? SnmpAuthPassword { get; set; }
    public string? SnmpPrivPassword { get; set; }

    public void Dispose()
    {
        // Attempt to clear secrets from memory.
        // Note: .NET strings are immutable and GC-managed; this is best-effort.
        // For stronger guarantees, use SecureString or pinned char[] in production.
        Secret = null;
        SnmpAuthPassword = null;
        SnmpPrivPassword = null;
        GC.Collect(0, GCCollectionMode.Forced);
    }
}

/// <summary>Full inventory result from a successful or partial collection.</summary>
public class CollectorResult
{
    public bool IsSuccess { get; init; }
    public bool IsPartial { get; init; }
    public string? ErrorMessage { get; init; }
    public int DurationMs { get; init; }

    // Host identity
    public string? HostnameResolved { get; init; }
    public string? Fqdn { get; init; }
    public string? Domain { get; init; }

    // OS
    public string? OsName { get; init; }
    public string? OsVersion { get; init; }
    public string? OsBuild { get; init; }
    public DateTime? InstallDate { get; init; }

    // Uptime
    public DateTime? LastBootTime { get; init; }
    public long? UptimeSeconds { get; init; }

    // Hardware
    public CpuInfo? CpuInfo { get; init; }
    public long? MemoryTotalMb { get; init; }
    public long? MemoryAvailableMb { get; init; }

    // Storage
    public List<DiskVolume> DiskVolumes { get; init; } = new();

    // Network
    public List<NetworkAdapter> NetworkAdapters { get; init; } = new();

    // Services
    public List<ServiceInfo> Services { get; init; } = new();

    // Patches
    public DateOnly? LastUpdateInstalled { get; init; }
    public bool? PendingReboot { get; init; }

    // Optional
    public List<InstalledSoftware>? InstalledSoftware { get; init; }

    // Raw output preserved for debugging
    public object? RawData { get; init; }
}

public record CpuInfo(string Model, int Sockets, int CoresPerSocket, int LogicalProcessors);
public record DiskVolume(string Drive, string? Label, string? Filesystem, double SizeGb, double FreeGb)
{
    public double FreePct => SizeGb > 0 ? Math.Round(FreeGb / SizeGb * 100, 1) : 0;
}
public record NetworkAdapter(string Name, string? IpAddress, string? SubnetMask, string? MacAddress, string? DefaultGateway, string[] DnsServers);
public record ServiceInfo(string Name, string DisplayName, string Status, string StartType);
public record InstalledSoftware(string Name, string? Version, string? Vendor, DateOnly? InstallDate);

/// <summary>Result of a connectivity test (not a full poll).</summary>
public class ConnectivityTestResult
{
    public bool Success { get; init; }
    public int DurationMs { get; init; }
    public string? Error { get; init; }
    public List<ConnectivityCheck> Checks { get; init; } = new();
}

public record ConnectivityCheck(string Name, bool Passed, string? Detail);
