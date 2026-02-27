using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerCat.Core.DTOs;
using ServerCat.Infrastructure.Data;
using System.Text.Json;

namespace ServerCat.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class PollsController(ApplicationDbContext db) : ControllerBase
{
    // ── GET /api/servers/{serverId}/polls ─────────────────────────────────────
    [HttpGet("servers/{serverId:guid}/polls")]
    [ProducesResponseType<PagedResult<PollSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPolls(
        Guid serverId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        if (!await db.Servers.AnyAsync(s => s.Id == serverId))
            return NotFound();

        var q = db.Polls.Where(p => p.ServerId == serverId);

        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<Core.Enums.PollStatus>(status, true, out var statusEnum))
            q = q.Where(p => p.Status == statusEnum);

        if (from.HasValue) q = q.Where(p => p.StartedAt >= from.Value);
        if (to.HasValue) q = q.Where(p => p.StartedAt <= to.Value);

        var totalCount = await q.CountAsync();
        var polls = await q
            .OrderByDescending(p => p.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResult<PollSummary>
        {
            Items = polls.Select(MapPollSummary).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    // ── GET /api/servers/{serverId}/polls/latest ──────────────────────────────
    [HttpGet("servers/{serverId:guid}/polls/latest")]
    [ProducesResponseType<PollResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLatestPoll(Guid serverId)
    {
        var poll = await db.Polls
            .Where(p => p.ServerId == serverId && p.Status != Core.Enums.PollStatus.Running)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync();

        if (poll == null)
            return NotFound(new { detail = "No completed polls found for this server." });

        var result = await db.PollResults.FirstOrDefaultAsync(r => r.PollId == poll.Id);

        return Ok(BuildPollResultDto(poll, result));
    }

    // ── GET /api/polls/{pollId} ───────────────────────────────────────────────
    [HttpGet("polls/{pollId:guid}")]
    [ProducesResponseType<PollResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPoll(Guid pollId)
    {
        var poll = await db.Polls.FindAsync(pollId);
        if (poll == null) return NotFound();

        var result = await db.PollResults.FirstOrDefaultAsync(r => r.PollId == pollId);

        return Ok(BuildPollResultDto(poll, result));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static PollSummary MapPollSummary(Core.Entities.Poll p) => new()
    {
        Id = p.Id,
        ServerId = p.ServerId,
        StartedAt = p.StartedAt,
        CompletedAt = p.CompletedAt,
        Status = p.Status.ToString(),
        CollectorType = p.CollectorType,
        TriggeredBy = p.TriggeredBy.ToString(),
        TriggeredActor = p.TriggeredActor,
        DurationMs = p.DurationMs,
        ErrorMessage = p.ErrorMessage
    };

    private static PollResultDto BuildPollResultDto(
        Core.Entities.Poll poll,
        Core.Entities.PollResult? result) => new()
    {
        Poll = MapPollSummary(poll),
        HostnameResolved = result?.HostnameResolved,
        Fqdn = result?.Fqdn,
        Domain = result?.Domain,
        OsName = result?.OsName,
        OsVersion = result?.OsVersion,
        OsBuild = result?.OsBuild,
        InstallDate = result?.InstallDate,
        LastBootTime = result?.LastBootTime,
        UptimeSeconds = result?.UptimeSeconds,
        CpuInfo = result?.CpuInfo?.RootElement,
        MemoryTotalMb = result?.MemoryTotalMb,
        MemoryAvailableMb = result?.MemoryAvailableMb,
        DiskVolumes = result?.DiskVolumes?.RootElement,
        NetworkAdapters = result?.NetworkAdapters?.RootElement,
        Services = result?.Services?.RootElement,
        LastUpdateInstalled = result?.LastUpdateInstalled,
        PendingReboot = result?.PendingReboot
    };
}
