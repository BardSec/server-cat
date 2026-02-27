namespace ServerCat.Core.Entities;

/// <summary>
/// Free-form markdown note attached to a server.
/// Supports pinning important notes to the top.
/// </summary>
public class Note
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ServerId { get; set; }
    public Server? Server { get; set; }

    /// <summary>Markdown content. Sanitized client-side before rendering (DOMPurify).</summary>
    public string Content { get; set; } = string.Empty;

    public bool IsPinned { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
}
