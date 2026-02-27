using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerCat.Core.DTOs;
using ServerCat.Core.Enums;
using ServerCat.Infrastructure.Data;

namespace ServerCat.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController(ApplicationDbContext db) : ControllerBase
{
    // ── GET /api/dashboard/summary ────────────────────────────────────────────
    [HttpGet("summary")]
    [ProducesResponseType<DashboardSummary>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary()
    {
        var activeServers = await db.Servers
            .Where(s => s.IsActive)
            .Select(s => new
            {
                s.Id,
                s.Criticality,
                Environment = s.Environment.ToString(),
                s.PollingIntervalMinutes
            })
            .ToListAsync();

        var serverIds = activeServers.Select(s => s.Id).ToList();

        // Last poll per server
        var lastPolls = await db.Polls
            .Where(p => serverIds.Contains(p.ServerId))
            .GroupBy(p => p.ServerId)
            .Select(g => g.OrderByDescending(p => p.StartedAt).First())
            .ToListAsync();

        var lastPollByServer = lastPolls.ToDictionary(p => p.ServerId);

        // Compute statuses
        var statusCounts = new Dictionary<string, int> { ["green"] = 0, ["yellow"] = 0, ["red"] = 0, ["gray"] = 0 };
        foreach (var server in activeServers)
        {
            lastPollByServer.TryGetValue(server.Id, out var lp);
            var status = ComputeStatus(lp, server.PollingIntervalMinutes);
            statusCounts[status]++;
        }

        // Criticality counts
        var criticalityCounts = activeServers
            .GroupBy(s => s.Criticality.ToString().ToLower())
            .ToDictionary(g => g.Key, g => g.Count());

        // Environment counts
        var environmentCounts = activeServers
            .GroupBy(s => s.Environment.ToLower())
            .ToDictionary(g => g.Key, g => g.Count());

        // Recent failures (last 24h)
        var since = DateTime.UtcNow.AddHours(-24);
        var recentFailures = await db.Polls
            .Include(p => p.Server)
            .Where(p => p.Status == PollStatus.Failed && p.StartedAt >= since && p.Server!.IsActive)
            .OrderByDescending(p => p.StartedAt)
            .Take(10)
            .Select(p => new RecentFailure
            {
                ServerId = p.ServerId,
                Hostname = p.Server!.Hostname,
                FailedAt = p.StartedAt,
                Error = p.ErrorMessage
            })
            .ToListAsync();

        // Poll counts (last 24h)
        var pollsLast24h = await db.Polls.CountAsync(p => p.StartedAt >= since);
        var failedPolls24h = await db.Polls.CountAsync(p => p.StartedAt >= since && p.Status == PollStatus.Failed);

        return Ok(new DashboardSummary
        {
            TotalServers = await db.Servers.CountAsync(),
            ActiveServers = activeServers.Count,
            ByStatus = statusCounts,
            ByCriticality = criticalityCounts,
            ByEnvironment = environmentCounts,
            RecentFailures = recentFailures,
            PollsLast24H = pollsLast24h,
            FailedPollsLast24H = failedPolls24h
        });
    }

    private static string ComputeStatus(Core.Entities.Poll? lastPoll, int intervalMinutes)
    {
        if (lastPoll == null) return "gray";
        if (lastPoll.Status == PollStatus.Failed) return "red";
        var age = DateTime.UtcNow - lastPoll.StartedAt;
        if (age.TotalMinutes > intervalMinutes * 4) return "red";
        if (age.TotalMinutes > intervalMinutes * 2 || lastPoll.Status == PollStatus.Partial) return "yellow";
        return "green";
    }
}
