using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerCat.Core.DTOs;
using ServerCat.Core.Entities;
using ServerCat.Core.Enums;
using ServerCat.Core.Interfaces;
using ServerCat.Infrastructure.Data;
using ServerCat.Infrastructure.Services;

namespace ServerCat.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ServersController(
    ApplicationDbContext db,
    IPollingService pollingService,
    ILogger<ServersController> logger) : ControllerBase
{
    private string CurrentUser => User.Identity?.Name ?? "unknown";

    // ── GET /api/servers ──────────────────────────────────────────────────────
    [HttpGet]
    [ProducesResponseType<PagedResult<ServerListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListServers([FromQuery] ServerListQuery query)
    {
        var q = db.Servers
            .Where(s => s.IsActive)
            .AsQueryable();

        if (query.Environment.HasValue)
            q = q.Where(s => s.Environment == query.Environment.Value);

        if (query.Criticality.HasValue)
            q = q.Where(s => s.Criticality == query.Criticality.Value);

        if (!string.IsNullOrWhiteSpace(query.Site))
            q = q.Where(s => s.Site != null && s.Site.Contains(query.Site));

        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(s => s.Hostname.Contains(query.Search) ||
                              (s.DisplayName != null && s.DisplayName.Contains(query.Search)));

        if (query.Tags?.Length > 0)
            q = q.Where(s => query.Tags.All(t => s.Tags.Contains(t)));

        var totalCount = await q.CountAsync();

        // Sorting
        q = query.SortBy?.ToLower() switch
        {
            "criticality" => query.SortDir == "desc"
                ? q.OrderByDescending(s => s.Criticality)
                : q.OrderBy(s => s.Criticality),
            "site" => query.SortDir == "desc"
                ? q.OrderByDescending(s => s.Site)
                : q.OrderBy(s => s.Site),
            _ => query.SortDir == "desc"
                ? q.OrderByDescending(s => s.Hostname)
                : q.OrderBy(s => s.Hostname)
        };

        var servers = await q
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        // Fetch last poll info per server
        var serverIds = servers.Select(s => s.Id).ToList();
        var lastPolls = await db.Polls
            .Where(p => serverIds.Contains(p.ServerId))
            .GroupBy(p => p.ServerId)
            .Select(g => g.OrderByDescending(p => p.StartedAt).First())
            .ToListAsync();

        var lastPollByServer = lastPolls.ToDictionary(p => p.ServerId);

        // Fetch latest OS names from poll results
        var latestResults = await db.PollResults
            .Where(r => serverIds.Contains(r.ServerId))
            .GroupBy(r => r.ServerId)
            .Select(g => g.OrderByDescending(r => r.CollectedAt).First())
            .Select(r => new { r.ServerId, r.OsName })
            .ToListAsync();

        var osNameByServer = latestResults.ToDictionary(r => r.ServerId, r => r.OsName);

        var items = servers.Select(s =>
        {
            lastPollByServer.TryGetValue(s.Id, out var lastPoll);
            osNameByServer.TryGetValue(s.Id, out var osName);

            return new ServerListItem
            {
                Id = s.Id,
                Hostname = s.Hostname,
                DisplayName = s.DisplayName,
                IpAddress = s.IpAddress,
                Environment = s.Environment.ToString(),
                Criticality = s.Criticality.ToString(),
                Site = s.Site,
                Owner = s.Owner,
                OsName = osName,
                Tags = s.Tags,
                PollingEnabled = s.PollingEnabled,
                PollingIntervalMinutes = s.PollingIntervalMinutes,
                Status = ComputeStatus(lastPoll, s.PollingIntervalMinutes),
                LastPollAt = lastPoll?.StartedAt,
                LastPollStatus = lastPoll?.Status.ToString(),
                IsActive = s.IsActive,
                CreatedAt = s.CreatedAt
            };
        }).ToList();

        // Status filter (computed, not DB column — filter after projection)
        if (query.Status.HasValue)
        {
            var statusStr = query.Status.Value.ToString().ToLower();
            items = items.Where(i => i.Status == statusStr).ToList();
        }

        return Ok(new PagedResult<ServerListItem>
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        });
    }

    // ── GET /api/servers/{id} ─────────────────────────────────────────────────
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ServerDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetServer(Guid id)
    {
        var server = await db.Servers
            .Include(s => s.CredentialRef)
            .Include(s => s.GroupMemberships).ThenInclude(m => m.Group)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (server == null) return NotFound();

        var lastPoll = await db.Polls
            .Where(p => p.ServerId == id)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync();

        var latestResult = await db.PollResults
            .Where(r => r.ServerId == id)
            .OrderByDescending(r => r.CollectedAt)
            .Select(r => new { r.OsName })
            .FirstOrDefaultAsync();

        return Ok(new ServerDetail
        {
            Id = server.Id,
            Hostname = server.Hostname,
            DisplayName = server.DisplayName,
            IpAddress = server.IpAddress,
            Environment = server.Environment.ToString(),
            Criticality = server.Criticality.ToString(),
            Site = server.Site,
            Owner = server.Owner,
            OsType = server.OsType,
            OsName = latestResult?.OsName,
            CollectorType = server.CollectorType,
            Tags = server.Tags,
            PollingEnabled = server.PollingEnabled,
            PollingIntervalMinutes = server.PollingIntervalMinutes,
            CredentialRef = server.CredentialRef == null ? null : new CredentialRefSummary
            {
                Id = server.CredentialRef.Id,
                Name = server.CredentialRef.Name,
                AuthMethod = server.CredentialRef.AuthMethod.ToString(),
                UsernameHint = server.CredentialRef.UsernameHint
            },
            Groups = server.GroupMemberships
                .Where(m => m.Group != null)
                .Select(m => new GroupSummary
                {
                    Id = m.Group!.Id,
                    Name = m.Group.Name,
                    Color = m.Group.Color
                }).ToList(),
            Status = ComputeStatus(lastPoll, server.PollingIntervalMinutes),
            LastPollAt = lastPoll?.StartedAt,
            LastPollStatus = lastPoll?.Status.ToString(),
            IsActive = server.IsActive,
            CreatedAt = server.CreatedAt,
            UpdatedAt = server.UpdatedAt,
            CreatedBy = server.CreatedBy
        });
    }

    // ── POST /api/servers ─────────────────────────────────────────────────────
    [HttpPost]
    [Authorize(Roles = "Engineer,Admin")]
    [ProducesResponseType<ServerDetail>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateServer([FromBody] CreateServerRequest req)
    {
        // Check duplicate hostname
        if (await db.Servers.AnyAsync(s => s.Hostname == req.Hostname))
            return Conflict(new { title = "Hostname already exists", detail = $"A server with hostname '{req.Hostname}' already exists." });

        var server = new Server
        {
            Hostname = req.Hostname,
            IpAddress = req.IpAddress,
            DisplayName = req.DisplayName,
            Environment = req.Environment,
            Owner = req.Owner,
            Site = req.Site,
            Criticality = req.Criticality,
            OsType = req.OsType,
            CollectorType = req.CollectorType,
            CredentialRefId = req.CredentialRefId,
            PollingEnabled = req.PollingEnabled,
            PollingIntervalMinutes = req.PollingIntervalMinutes,
            Tags = req.Tags,
            CreatedBy = CurrentUser
        };

        db.Servers.Add(server);
        await db.SaveChangesAsync();

        // Write audit log
        await WriteLogAsync(server.Id, "server.created", LogSeverity.Info,
            $"Server '{server.Hostname}' added to catalog by {CurrentUser}",
            new { hostname = server.Hostname, environment = server.Environment.ToString() });

        // Schedule recurring poll
        if (server.PollingEnabled)
            pollingService.ScheduleServer(server.Id, server.PollingIntervalMinutes);

        logger.LogInformation("Server {Hostname} created by {User}", server.Hostname, CurrentUser);

        return CreatedAtAction(nameof(GetServer), new { id = server.Id },
            await GetServerDetailAsync(server.Id));
    }

    // ── PUT /api/servers/{id} ─────────────────────────────────────────────────
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Engineer,Admin")]
    [ProducesResponseType<ServerDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateServer(Guid id, [FromBody] UpdateServerRequest req)
    {
        var server = await db.Servers.FindAsync(id);
        if (server == null) return NotFound();

        var oldValues = new { server.Environment, server.Criticality, server.PollingEnabled, server.PollingIntervalMinutes };

        if (req.DisplayName != null) server.DisplayName = req.DisplayName;
        if (req.Environment.HasValue) server.Environment = req.Environment.Value;
        if (req.Owner != null) server.Owner = req.Owner;
        if (req.Site != null) server.Site = req.Site;
        if (req.Criticality.HasValue) server.Criticality = req.Criticality.Value;
        if (req.CollectorType != null) server.CollectorType = req.CollectorType;
        if (req.CredentialRefId.HasValue) server.CredentialRefId = req.CredentialRefId;
        if (req.PollingEnabled.HasValue) server.PollingEnabled = req.PollingEnabled.Value;
        if (req.PollingIntervalMinutes.HasValue) server.PollingIntervalMinutes = req.PollingIntervalMinutes.Value;
        if (req.Tags != null) server.Tags = req.Tags;
        server.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        await WriteLogAsync(server.Id, "server.updated", LogSeverity.Info,
            $"Server '{server.Hostname}' updated by {CurrentUser}",
            new { old = oldValues, updated_by = CurrentUser });

        // Re-schedule if polling settings changed
        if (req.PollingEnabled.HasValue || req.PollingIntervalMinutes.HasValue)
        {
            if (server.PollingEnabled)
                pollingService.ScheduleServer(server.Id, server.PollingIntervalMinutes);
            else
                pollingService.UnscheduleServer(server.Id);
        }

        return Ok(await GetServerDetailAsync(id));
    }

    // ── DELETE /api/servers/{id} ──────────────────────────────────────────────
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Engineer,Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteServer(Guid id, [FromQuery] bool hard = false)
    {
        var server = await db.Servers.FindAsync(id);
        if (server == null) return NotFound();

        if (hard)
        {
            // Hard delete: Admin only, cascades to polls/results
            if (!User.IsInRole("Admin"))
                return Forbid();

            db.Servers.Remove(server);
            await WriteLogAsync(null, "server.hard_deleted", LogSeverity.Warning,
                $"Server '{server.Hostname}' permanently deleted by {CurrentUser}",
                new { hostname = server.Hostname });
        }
        else
        {
            // Soft delete
            server.IsActive = false;
            server.UpdatedAt = DateTime.UtcNow;
            pollingService.UnscheduleServer(server.Id);

            await WriteLogAsync(server.Id, "server.deleted", LogSeverity.Info,
                $"Server '{server.Hostname}' deactivated by {CurrentUser}",
                new { hostname = server.Hostname });
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── POST /api/servers/{id}/connectivity-test ──────────────────────────────
    [HttpPost("{id:guid}/connectivity-test")]
    [Authorize(Roles = "Engineer,Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TestConnectivity(Guid id, [FromBody] ConnectivityTestRequest? req)
    {
        var server = await db.Servers.FindAsync(id);
        if (server == null) return NotFound();

        var result = await pollingService.TestConnectivityAsync(
            id,
            req?.CredentialRefId?.ToString(),
            HttpContext.RequestAborted);

        return Ok(result);
    }

    // ── POST /api/servers/{id}/poll ───────────────────────────────────────────
    [HttpPost("{id:guid}/poll")]
    [Authorize(Roles = "Engineer,Admin")]
    [ProducesResponseType<TriggerPollResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> TriggerPoll(Guid id)
    {
        var server = await db.Servers.FindAsync(id);
        if (server == null) return NotFound();

        if (!server.PollingEnabled)
            return BadRequest(new { detail = "Polling is disabled for this server." });

        // Rate limit: 1 manual trigger per server per 60 seconds
        var recentPoll = await db.Polls
            .Where(p => p.ServerId == id &&
                        p.TriggeredBy == PollTrigger.Manual &&
                        p.StartedAt > DateTime.UtcNow.AddSeconds(-60))
            .FirstOrDefaultAsync();

        if (recentPoll != null)
            return StatusCode(429, new { detail = "A manual poll was triggered within the last 60 seconds." });

        // Create a poll record immediately so we can return the ID
        var poll = new Poll
        {
            ServerId = id,
            StartedAt = DateTime.UtcNow,
            Status = PollStatus.Running,
            CollectorType = server.CollectorType,
            TriggeredBy = PollTrigger.Manual,
            TriggeredActor = CurrentUser
        };
        db.Polls.Add(poll);
        await db.SaveChangesAsync();

        await WriteLogAsync(id, "poll.triggered", LogSeverity.Info,
            $"Manual poll triggered for {server.Hostname} by {CurrentUser}",
            new { pollId = poll.Id });

        // Enqueue Hangfire background job
        BackgroundJob.Enqueue<PollingService>(svc =>
            svc.ExecutePollAsync(id, "manual", CurrentUser, CancellationToken.None));

        return Accepted(new TriggerPollResponse
        {
            PollId = poll.Id,
            Message = $"Poll queued. Check /api/polls/{poll.Id} for status.",
            QueuedAt = DateTime.UtcNow
        });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string ComputeStatus(Poll? lastPoll, int intervalMinutes)
    {
        if (lastPoll == null) return "gray";
        if (lastPoll.Status == PollStatus.Failed) return "red";

        var age = DateTime.UtcNow - lastPoll.StartedAt;
        if (age.TotalMinutes > intervalMinutes * 4) return "red";
        if (age.TotalMinutes > intervalMinutes * 2 || lastPoll.Status == PollStatus.Partial) return "yellow";
        return "green";
    }

    private async Task<ServerDetail> GetServerDetailAsync(Guid id)
    {
        var result = await GetServer(id);
        return ((OkObjectResult)result).Value as ServerDetail ?? new ServerDetail();
    }

    private async Task WriteLogAsync(Guid? serverId, string eventType, LogSeverity severity, string summary, object? details = null)
    {
        db.OperationalLogs.Add(new OperationalLog
        {
            ServerId = serverId,
            EventType = eventType,
            Severity = severity,
            Summary = summary,
            Details = details != null ? JsonDocument.Parse(JsonSerializer.Serialize(details)) : null,
            Actor = CurrentUser
        });
        await db.SaveChangesAsync();
    }
}
