using ServerCat.Core.Enums;

namespace ServerCat.Core.Entities;

/// <summary>
/// Represents a single poll execution run for a server.
/// Contains metadata about the run; actual collected data is in PollResult.
/// </summary>
public class Poll
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ServerId { get; set; }
    public Server? Server { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public PollStatus Status { get; set; } = PollStatus.Running;

    /// <summary>Which collector was used for this run.</summary>
    public string? CollectorType { get; set; }

    public PollTrigger TriggeredBy { get; set; } = PollTrigger.Scheduler;

    /// <summary>User who triggered this poll (for manual/API triggers).</summary>
    public string? TriggeredActor { get; set; }

    /// <summary>Error details if the poll failed or was partial.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Total poll duration in milliseconds.</summary>
    public int? DurationMs { get; set; }

    // Navigation
    public PollResult? PollResult { get; set; }
}
