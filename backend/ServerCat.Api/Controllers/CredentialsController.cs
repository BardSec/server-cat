using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerCat.Core.DTOs;
using ServerCat.Core.Entities;
using ServerCat.Core.Enums;
using ServerCat.Core.Interfaces;
using ServerCat.Infrastructure.Data;

namespace ServerCat.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class CredentialsController(
    ApplicationDbContext db,
    ICredentialStore credentialStore,
    ILogger<CredentialsController> logger) : ControllerBase
{
    private string CurrentUser => User.Identity?.Name ?? "unknown";

    // ── GET /api/credentials ──────────────────────────────────────────────────
    [HttpGet]
    [Authorize(Roles = "Engineer,Admin")]
    [ProducesResponseType<PagedResult<CredentialDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCredentials()
    {
        var creds = await db.CredentialReferences.ToListAsync();

        // Compute usage count per credential
        var serverCounts = await db.Servers
            .Where(s => s.IsActive && s.CredentialRefId.HasValue)
            .GroupBy(s => s.CredentialRefId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync();

        var countMap = serverCounts.ToDictionary(x => x.Id, x => x.Count);

        return Ok(new PagedResult<CredentialDto>
        {
            Items = creds.Select(c => new CredentialDto
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                AuthMethod = c.AuthMethod.ToString(),
                UsernameHint = c.UsernameHint,
                ServersUsingCount = countMap.GetValueOrDefault(c.Id, 0),
                CreatedAt = c.CreatedAt,
                CreatedBy = c.CreatedBy
            }).ToList(),
            TotalCount = creds.Count,
            Page = 1,
            PageSize = creds.Count
        });
    }

    // ── POST /api/credentials ─────────────────────────────────────────────────
    [HttpPost]
    [ProducesResponseType<CredentialDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCredential([FromBody] CreateCredentialRequest req)
    {
        if (await db.CredentialReferences.AnyAsync(c => c.Name == req.Name))
            return Conflict(new { detail = $"A credential named '{req.Name}' already exists." });

        if (!Enum.TryParse<AuthMethod>(req.AuthMethod, true, out var authMethod))
            return BadRequest(new { detail = $"Invalid auth method: {req.AuthMethod}" });

        // Encrypt the secret immediately — it must never be stored in plaintext
        string storeKey;
        try
        {
            storeKey = await credentialStore.ProtectAsync(req.SecretValue);
        }
        finally
        {
            // Best-effort: clear the secret from the request object
            req.SecretValue = string.Empty;
        }

        var cred = new CredentialReference
        {
            Name = req.Name,
            Description = req.Description,
            AuthMethod = authMethod,
            UsernameHint = req.UsernameHint,
            SecretStoreKey = storeKey,
            CreatedBy = CurrentUser
        };

        db.CredentialReferences.Add(cred);

        db.OperationalLogs.Add(new OperationalLog
        {
            EventType = "credential.created",
            Severity = LogSeverity.Info,
            Summary = $"Credential reference '{req.Name}' created by {CurrentUser}",
            Details = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                name = req.Name,
                authMethod = authMethod.ToString(),
                usernameHint = req.UsernameHint
                // NEVER include secretValue or storeKey in logs
            })),
            Actor = CurrentUser
        });

        await db.SaveChangesAsync();

        logger.LogInformation("Credential reference '{Name}' created by {User}", cred.Name, CurrentUser);

        return CreatedAtAction(nameof(ListCredentials), null, MapCredential(cred, 0));
    }

    // ── PUT /api/credentials/{id} ─────────────────────────────────────────────
    [HttpPut("{id:guid}")]
    [ProducesResponseType<CredentialDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCredential(Guid id, [FromBody] UpdateCredentialRequest req)
    {
        var cred = await db.CredentialReferences.FindAsync(id);
        if (cred == null) return NotFound();

        if (req.Description != null) cred.Description = req.Description;
        if (req.UsernameHint != null) cred.UsernameHint = req.UsernameHint;

        if (!string.IsNullOrEmpty(req.NewSecretValue))
        {
            try
            {
                await credentialStore.UpdateAsync(cred.SecretStoreKey, req.NewSecretValue);
            }
            finally
            {
                req.NewSecretValue = string.Empty;
            }

            logger.LogInformation("Credential secret rotated for '{Name}' by {User}", cred.Name, CurrentUser);
        }

        cred.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        db.OperationalLogs.Add(new OperationalLog
        {
            EventType = "credential.updated",
            Severity = LogSeverity.Info,
            Summary = $"Credential reference '{cred.Name}' updated by {CurrentUser}",
            Actor = CurrentUser
        });
        await db.SaveChangesAsync();

        return Ok(MapCredential(cred, 0));
    }

    // ── DELETE /api/credentials/{id} ──────────────────────────────────────────
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCredential(Guid id)
    {
        var cred = await db.CredentialReferences.FindAsync(id);
        if (cred == null) return NotFound();

        // Prevent deletion if servers are using this credential
        var usingCount = await db.Servers.CountAsync(s => s.CredentialRefId == id && s.IsActive);
        if (usingCount > 0)
            return BadRequest(new { detail = $"Cannot delete: {usingCount} active server(s) reference this credential. Reassign them first." });

        await credentialStore.DeleteAsync(cred.SecretStoreKey);
        db.CredentialReferences.Remove(cred);

        db.OperationalLogs.Add(new OperationalLog
        {
            EventType = "credential.deleted",
            Severity = LogSeverity.Warning,
            Summary = $"Credential reference '{cred.Name}' deleted by {CurrentUser}",
            Actor = CurrentUser
        });

        await db.SaveChangesAsync();
        return NoContent();
    }

    private static CredentialDto MapCredential(CredentialReference c, int serversUsing) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Description = c.Description,
        AuthMethod = c.AuthMethod.ToString(),
        UsernameHint = c.UsernameHint,
        ServersUsingCount = serversUsing,
        CreatedAt = c.CreatedAt,
        CreatedBy = c.CreatedBy
    };
}
