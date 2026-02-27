using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServerCat.Core.Entities;
using ServerCat.Core.Enums;
using ServerCat.Core.Interfaces;
using ServerCat.Infrastructure.Collectors;
using ServerCat.Infrastructure.Data;

namespace ServerCat.Infrastructure.Services;

/// <summary>
/// Orchestrates a complete poll cycle:
///   1. Resolve server + credential
///   2. Create Poll record (status=Running)
///   3. Invoke collector
///   4. Map CollectorResult → PollResult entity
///   5. Persist PollResult
///   6. Update Poll record (status=Success/Partial/Failed)
///   7. Write OperationalLog entry
///   8. Zero credentials from memory
/// </summary>
public class PollingService(
    ApplicationDbContext db,
    CollectorFactory collectorFactory,
    ICredentialStore credentialStore,
    ILogger<PollingService> logger) : IPollingService
{
    public async Task<Guid> ExecutePollAsync(
        Guid serverId,
        string triggeredBy = "scheduler",
        string? actor = null,
        CancellationToken ct = default)
    {
        // Load server with credential reference
        var server = await db.Servers
            .Include(s => s.CredentialRef)
            .FirstOrDefaultAsync(s => s.Id == serverId && s.IsActive, ct);

        if (server == null)
        {
            logger.LogWarning("Poll requested for unknown or inactive server {ServerId}", serverId);
            throw new InvalidOperationException($"Server {serverId} not found or inactive.");
        }

        if (!server.PollingEnabled)
        {
            logger.LogInformation("Polling disabled for server {Hostname}. Skipping.", server.Hostname);
            throw new InvalidOperationException($"Polling is disabled for server {server.Hostname}.");
        }

        // Create poll record
        var poll = new Poll
        {
            ServerId = serverId,
            StartedAt = DateTime.UtcNow,
            Status = PollStatus.Running,
            CollectorType = server.CollectorType,
            TriggeredBy = Enum.TryParse<PollTrigger>(triggeredBy, true, out var trigger)
                ? trigger : PollTrigger.Scheduler,
            TriggeredActor = actor
        };
        db.Polls.Add(poll);
        await db.SaveChangesAsync(ct);

        ResolvedCredential? credential = null;
        try
        {
            // Resolve credential from vault
            credential = await ResolveCredentialAsync(server.CredentialRef, ct);

            var collectorTarget = new CollectorTarget
            {
                ServerId = serverId,
                Hostname = server.Hostname,
                IpAddress = server.IpAddress,
                CollectorType = server.CollectorType,
                Credential = credential
            };

            // Execute collection
            var collector = collectorFactory.Create(server.CollectorType);
            var result = await collector.CollectAsync(collectorTarget, ct);

            // Map to PollResult entity
            var pollResult = MapToEntity(poll.Id, serverId, result);
            db.PollResults.Add(pollResult);

            // Update poll record
            poll.CompletedAt = DateTime.UtcNow;
            poll.DurationMs = result.DurationMs;
            poll.Status = result.IsSuccess ? PollStatus.Success
                        : result.IsPartial ? PollStatus.Partial
                        : PollStatus.Failed;
            poll.ErrorMessage = result.ErrorMessage;

            await db.SaveChangesAsync(ct);

            // Write audit log
            await WriteLogAsync(serverId, actor ?? "system",
                poll.Status == PollStatus.Success ? "poll.success" : "poll.partial",
                poll.Status == PollStatus.Success ? LogSeverity.Info : LogSeverity.Warning,
                $"Poll {poll.Status.ToString().ToLower()} for {server.Hostname} in {result.DurationMs}ms " +
                (result.ErrorMessage != null ? $"({result.ErrorMessage})" : ""),
                new { pollId = poll.Id, durationMs = result.DurationMs }, ct);

            logger.LogInformation("Poll {PollId} for {Hostname} completed with status {Status}",
                poll.Id, server.Hostname, poll.Status);

            return poll.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Poll {PollId} for {Hostname} failed with exception", poll.Id, server.Hostname);

            poll.CompletedAt = DateTime.UtcNow;
            poll.Status = PollStatus.Failed;
            poll.ErrorMessage = ex.Message;
            await db.SaveChangesAsync(ct);

            await WriteLogAsync(serverId, actor ?? "system", "poll.failed", LogSeverity.Error,
                $"Poll failed for {server.Hostname}: {ex.Message}",
                new { pollId = poll.Id, error = ex.Message }, ct);

            return poll.Id;
        }
        finally
        {
            credential?.Dispose();
        }
    }

    public async Task<ConnectivityTestResult> TestConnectivityAsync(
        Guid serverId,
        string? overrideCredentialRefId = null,
        CancellationToken ct = default)
    {
        var server = await db.Servers
            .Include(s => s.CredentialRef)
            .FirstOrDefaultAsync(s => s.Id == serverId, ct)
            ?? throw new InvalidOperationException($"Server {serverId} not found.");

        CredentialReference? credRef = server.CredentialRef;
        if (overrideCredentialRefId != null && Guid.TryParse(overrideCredentialRefId, out var credId))
        {
            credRef = await db.CredentialReferences.FindAsync(new object[] { credId }, ct);
        }

        using var credential = await ResolveCredentialAsync(credRef, ct);
        var target = new CollectorTarget
        {
            ServerId = serverId,
            Hostname = server.Hostname,
            IpAddress = server.IpAddress,
            CollectorType = server.CollectorType,
            Credential = credential
        };

        var collector = collectorFactory.Create(server.CollectorType);
        return await collector.TestConnectivityAsync(target, ct);
    }

    public void ScheduleServer(Guid serverId, int intervalMinutes)
    {
        // Hangfire cron: convert minutes to cron expression
        // For intervals < 60 min, use */N * * * *
        // For 60 min, use 0 * * * *
        // For longer intervals, use a simplified cron
        var cron = intervalMinutes < 60
            ? $"*/{intervalMinutes} * * * *"
            : $"0 */{intervalMinutes / 60} * * *";

        RecurringJob.AddOrUpdate<PollingService>(
            recurringJobId: $"poll-server-{serverId}",
            methodCall: svc => svc.ExecutePollAsync(serverId, "scheduler", null, CancellationToken.None),
            cronExpression: cron,
            timeZone: TimeZoneInfo.Utc);

        logger.LogInformation("Scheduled polling for server {ServerId} every {Interval} minutes", serverId, intervalMinutes);
    }

    public void UnscheduleServer(Guid serverId)
    {
        RecurringJob.RemoveIfExists($"poll-server-{serverId}");
        logger.LogInformation("Unscheduled polling for server {ServerId}", serverId);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<ResolvedCredential> ResolveCredentialAsync(
        CredentialReference? credRef,
        CancellationToken ct)
    {
        if (credRef == null)
            return new ResolvedCredential();

        var secret = await credentialStore.UnprotectAsync(credRef.SecretStoreKey);
        return new ResolvedCredential
        {
            Username = credRef.UsernameHint,
            Secret = secret
        };
    }

    private static PollResult MapToEntity(Guid pollId, Guid serverId, CollectorResult result)
    {
        return new PollResult
        {
            PollId = pollId,
            ServerId = serverId,
            CollectedAt = DateTime.UtcNow,
            HostnameResolved = result.HostnameResolved,
            Fqdn = result.Fqdn,
            Domain = result.Domain,
            OsName = result.OsName,
            OsVersion = result.OsVersion,
            OsBuild = result.OsBuild,
            InstallDate = result.InstallDate,
            LastBootTime = result.LastBootTime,
            UptimeSeconds = result.UptimeSeconds,
            CpuInfo = result.CpuInfo != null
                ? JsonDocument.Parse(JsonSerializer.Serialize(result.CpuInfo))
                : null,
            MemoryTotalMb = result.MemoryTotalMb,
            MemoryAvailableMb = result.MemoryAvailableMb,
            DiskVolumes = result.DiskVolumes.Count > 0
                ? JsonDocument.Parse(JsonSerializer.Serialize(result.DiskVolumes))
                : null,
            NetworkAdapters = result.NetworkAdapters.Count > 0
                ? JsonDocument.Parse(JsonSerializer.Serialize(result.NetworkAdapters))
                : null,
            Services = result.Services.Count > 0
                ? JsonDocument.Parse(JsonSerializer.Serialize(result.Services))
                : null,
            LastUpdateInstalled = result.LastUpdateInstalled,
            PendingReboot = result.PendingReboot,
            InstalledSoftware = result.InstalledSoftware?.Count > 0
                ? JsonDocument.Parse(JsonSerializer.Serialize(result.InstalledSoftware))
                : null
        };
    }

    private async Task WriteLogAsync(Guid? serverId, string actor, string eventType,
        LogSeverity severity, string summary, object? details, CancellationToken ct)
    {
        db.OperationalLogs.Add(new OperationalLog
        {
            ServerId = serverId,
            EventType = eventType,
            Severity = severity,
            Summary = summary,
            Details = details != null ? JsonDocument.Parse(JsonSerializer.Serialize(details)) : null,
            Actor = actor
        });
        await db.SaveChangesAsync(ct);
    }
}
