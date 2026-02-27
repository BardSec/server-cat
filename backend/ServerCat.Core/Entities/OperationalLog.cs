using System.Text.Json;
using ServerCat.Core.Enums;

namespace ServerCat.Core.Entities;

/// <summary>
/// Immutable structured audit trail.
/// The database user has INSERT-only access to this table.
/// No application code path deletes or updates these records.
/// </summary>
public class OperationalLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Nullable: system-level events are not tied to a specific server.</summary>
    public Guid? ServerId { get; set; }
    public Server? Server { get; set; }

    /// <summary>
    /// Structured event type constant. Examples:
    ///   server.created, server.updated, server.deleted
    ///   poll.triggered, poll.success, poll.failed
    ///   note.added, note.updated, note.deleted
    ///   credential.created, credential.updated
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    public LogSeverity Severity { get; set; } = LogSeverity.Info;

    /// <summary>Human-readable summary for display in the UI.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Structured context/diff as JSON. MUST NOT contain secret values.
    /// For updates, include { "old": {...}, "new": {...} } showing changed fields.
    /// </summary>
    public JsonDocument? Details { get; set; }

    /// <summary>Identity of the actor: authenticated user (domain\user) or "system".</summary>
    public string Actor { get; set; } = "system";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
