namespace ServerCat.Core.Entities;

public class ServerGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Hex color for UI badge (e.g., "#3B82F6").</summary>
    public string Color { get; set; } = "#6B7280";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ServerGroupMembership> Memberships { get; set; } = new List<ServerGroupMembership>();
}

public class ServerGroupMembership
{
    public Guid ServerId { get; set; }
    public Server? Server { get; set; }

    public Guid GroupId { get; set; }
    public ServerGroup? Group { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public string? AddedBy { get; set; }
}
