namespace ServerCat.Core.Interfaces;

/// <summary>
/// Orchestrates poll execution: resolves credentials, invokes collector, persists results.
/// </summary>
public interface IPollingService
{
    /// <summary>Execute a full poll for the given server. Called by Hangfire job.</summary>
    Task<Guid> ExecutePollAsync(Guid serverId, string triggeredBy = "scheduler", string? actor = null, CancellationToken ct = default);

    /// <summary>Test connectivity without running a full poll or storing results.</summary>
    Task<ConnectivityTestResult> TestConnectivityAsync(Guid serverId, string? overrideCredentialRefId = null, CancellationToken ct = default);

    /// <summary>
    /// Schedule (or re-schedule) recurring polls for a server.
    /// Called whenever a server is added or its polling interval changes.
    /// </summary>
    void ScheduleServer(Guid serverId, int intervalMinutes);

    /// <summary>Remove the recurring poll job for a server (called on soft-delete or disable).</summary>
    void UnscheduleServer(Guid serverId);
}
