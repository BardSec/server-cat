using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerCat.Core.DTOs;
using ServerCat.Core.Entities;
using ServerCat.Core.Enums;
using ServerCat.Infrastructure.Data;

namespace ServerCat.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class NotesController(ApplicationDbContext db) : ControllerBase
{
    private string CurrentUser => User.Identity?.Name ?? "unknown";

    // ── GET /api/servers/{serverId}/notes ─────────────────────────────────────
    [HttpGet("servers/{serverId:guid}/notes")]
    [ProducesResponseType<PagedResult<NoteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListNotes(
        Guid serverId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (!await db.Servers.AnyAsync(s => s.Id == serverId))
            return NotFound();

        var totalCount = await db.Notes.CountAsync(n => n.ServerId == serverId);

        // Pinned notes first, then by date descending
        var notes = await db.Notes
            .Where(n => n.ServerId == serverId)
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResult<NoteDto>
        {
            Items = notes.Select(MapNote).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    // ── POST /api/servers/{serverId}/notes ────────────────────────────────────
    [HttpPost("servers/{serverId:guid}/notes")]
    [Authorize(Roles = "Engineer,Admin")]
    [ProducesResponseType<NoteDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateNote(Guid serverId, [FromBody] CreateNoteRequest req)
    {
        if (!await db.Servers.AnyAsync(s => s.Id == serverId))
            return NotFound();

        var note = new Note
        {
            ServerId = serverId,
            Content = req.Content,
            IsPinned = req.IsPinned,
            CreatedBy = CurrentUser
        };

        db.Notes.Add(note);

        db.OperationalLogs.Add(new OperationalLog
        {
            ServerId = serverId,
            EventType = "note.added",
            Severity = LogSeverity.Info,
            Summary = $"Note added by {CurrentUser}",
            Details = JsonDocument.Parse(JsonSerializer.Serialize(new { noteId = note.Id, isPinned = req.IsPinned })),
            Actor = CurrentUser
        });

        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(ListNotes), new { serverId }, MapNote(note));
    }

    // ── PUT /api/notes/{noteId} ───────────────────────────────────────────────
    [HttpPut("notes/{noteId:guid}")]
    [Authorize(Roles = "Engineer,Admin")]
    [ProducesResponseType<NoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateNote(Guid noteId, [FromBody] UpdateNoteRequest req)
    {
        var note = await db.Notes.FindAsync(noteId);
        if (note == null) return NotFound();

        // Only original author or Admin can update
        if (note.CreatedBy != CurrentUser && !User.IsInRole("Admin"))
            return Forbid();

        if (req.Content != null) note.Content = req.Content;
        if (req.IsPinned.HasValue) note.IsPinned = req.IsPinned.Value;
        note.UpdatedAt = DateTime.UtcNow;
        note.UpdatedBy = CurrentUser;

        db.OperationalLogs.Add(new OperationalLog
        {
            ServerId = note.ServerId,
            EventType = "note.updated",
            Severity = LogSeverity.Info,
            Summary = $"Note updated by {CurrentUser}",
            Actor = CurrentUser
        });

        await db.SaveChangesAsync();
        return Ok(MapNote(note));
    }

    // ── DELETE /api/notes/{noteId} ────────────────────────────────────────────
    [HttpDelete("notes/{noteId:guid}")]
    [Authorize(Roles = "Engineer,Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteNote(Guid noteId)
    {
        var note = await db.Notes.FindAsync(noteId);
        if (note == null) return NotFound();

        if (note.CreatedBy != CurrentUser && !User.IsInRole("Admin"))
            return Forbid();

        db.Notes.Remove(note);

        db.OperationalLogs.Add(new OperationalLog
        {
            ServerId = note.ServerId,
            EventType = "note.deleted",
            Severity = LogSeverity.Info,
            Summary = $"Note deleted by {CurrentUser}",
            Actor = CurrentUser
        });

        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── GET /api/servers/{serverId}/logs ──────────────────────────────────────
    [HttpGet("servers/{serverId:guid}/logs")]
    [ProducesResponseType<PagedResult<OperationalLogDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListServerLogs(
        Guid serverId,
        [FromQuery] string? eventType = null,
        [FromQuery] string? severity = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var q = db.OperationalLogs.Where(l => l.ServerId == serverId);

        if (!string.IsNullOrEmpty(eventType)) q = q.Where(l => l.EventType == eventType);
        if (!string.IsNullOrEmpty(severity) &&
            Enum.TryParse<LogSeverity>(severity, true, out var sev))
            q = q.Where(l => l.Severity == sev);
        if (from.HasValue) q = q.Where(l => l.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(l => l.CreatedAt <= to.Value);

        var total = await q.CountAsync();
        var logs = await q
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResult<OperationalLogDto>
        {
            Items = logs.Select(MapLog).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    // ── GET /api/operational-logs ─────────────────────────────────────────────
    [HttpGet("operational-logs")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<PagedResult<OperationalLogDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAllLogs(
        [FromQuery] string? eventType = null,
        [FromQuery] string? actor = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var q = db.OperationalLogs.AsQueryable();

        if (!string.IsNullOrEmpty(eventType)) q = q.Where(l => l.EventType == eventType);
        if (!string.IsNullOrEmpty(actor)) q = q.Where(l => l.Actor == actor);
        if (from.HasValue) q = q.Where(l => l.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(l => l.CreatedAt <= to.Value);

        var total = await q.CountAsync();
        var logs = await q
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResult<OperationalLogDto>
        {
            Items = logs.Select(MapLog).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    // ── Mappers ───────────────────────────────────────────────────────────────

    private static NoteDto MapNote(Note n) => new()
    {
        Id = n.Id,
        ServerId = n.ServerId,
        Content = n.Content,
        IsPinned = n.IsPinned,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt,
        CreatedBy = n.CreatedBy,
        UpdatedBy = n.UpdatedBy
    };

    private static OperationalLogDto MapLog(OperationalLog l) => new()
    {
        Id = l.Id,
        ServerId = l.ServerId,
        EventType = l.EventType,
        Severity = l.Severity.ToString(),
        Summary = l.Summary,
        Details = l.Details?.RootElement,
        Actor = l.Actor,
        CreatedAt = l.CreatedAt
    };
}
