using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ServerCat.Core.Entities;
using ServerCat.Core.Enums;

namespace ServerCat.Infrastructure.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<Poll> Polls => Set<Poll>();
    public DbSet<PollResult> PollResults => Set<PollResult>();
    public DbSet<CredentialReference> CredentialReferences => Set<CredentialReference>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<OperationalLog> OperationalLogs => Set<OperationalLog>();
    public DbSet<ServerGroup> ServerGroups => Set<ServerGroup>();
    public DbSet<ServerGroupMembership> ServerGroupMemberships => Set<ServerGroupMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── CredentialReference ───────────────────────────────────────────
        modelBuilder.Entity<CredentialReference>(e =>
        {
            e.ToTable("credential_references");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.SecretStoreKey).HasMaxLength(500).IsRequired();
            e.Property(x => x.UsernameHint).HasMaxLength(255);
            e.Property(x => x.AuthMethod)
             .HasConversion(new EnumToStringConverter<AuthMethod>())
             .HasMaxLength(50);
            e.HasIndex(x => x.Name).IsUnique();
        });

        // ── Server ────────────────────────────────────────────────────────
        modelBuilder.Entity<Server>(e =>
        {
            e.ToTable("servers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Hostname).HasMaxLength(255).IsRequired();
            e.HasIndex(x => x.Hostname).IsUnique();
            e.Property(x => x.IpAddress).HasMaxLength(50);
            e.Property(x => x.DisplayName).HasMaxLength(255);
            e.Property(x => x.Owner).HasMaxLength(255);
            e.Property(x => x.Site).HasMaxLength(100);
            e.Property(x => x.CollectorType).HasMaxLength(50);
            e.Property(x => x.CreatedBy).HasMaxLength(255);
            e.Property(x => x.Tags)
             .HasColumnType("text[]")
             .HasConversion(
                 v => v,
                 v => v ?? Array.Empty<string>());

            e.Property(x => x.Environment)
             .HasConversion(new EnumToStringConverter<ServerEnvironment>())
             .HasMaxLength(50);
            e.Property(x => x.Criticality)
             .HasConversion(new EnumToStringConverter<Criticality>())
             .HasMaxLength(20);
            e.Property(x => x.OsType)
             .HasConversion(new EnumToStringConverter<OsType>())
             .HasMaxLength(20);

            e.HasOne(x => x.CredentialRef)
             .WithMany(c => c.Servers)
             .HasForeignKey(x => x.CredentialRefId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ── Poll ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Poll>(e =>
        {
            e.ToTable("polls");
            e.HasKey(x => x.Id);
            e.Property(x => x.CollectorType).HasMaxLength(50);
            e.Property(x => x.TriggeredActor).HasMaxLength(255);
            e.Property(x => x.Status)
             .HasConversion(new EnumToStringConverter<PollStatus>())
             .HasMaxLength(20);
            e.Property(x => x.TriggeredBy)
             .HasConversion(new EnumToStringConverter<PollTrigger>())
             .HasMaxLength(20);

            e.HasOne(x => x.Server)
             .WithMany(s => s.Polls)
             .HasForeignKey(x => x.ServerId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.ServerId, x.StartedAt });
            e.HasIndex(x => x.Status);
        });

        // ── PollResult ────────────────────────────────────────────────────
        modelBuilder.Entity<PollResult>(e =>
        {
            e.ToTable("poll_results");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PollId).IsUnique();
            e.HasIndex(x => new { x.ServerId, x.CollectedAt });

            // JSONB columns
            e.Property(x => x.CpuInfo).HasColumnType("jsonb");
            e.Property(x => x.DiskVolumes).HasColumnType("jsonb");
            e.Property(x => x.NetworkAdapters).HasColumnType("jsonb");
            e.Property(x => x.Services).HasColumnType("jsonb");
            e.Property(x => x.InstalledSoftware).HasColumnType("jsonb");
            e.Property(x => x.RawData).HasColumnType("jsonb");

            e.HasOne(x => x.Poll)
             .WithOne(p => p.PollResult)
             .HasForeignKey<PollResult>(x => x.PollId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Note ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Note>(e =>
        {
            e.ToTable("notes");
            e.HasKey(x => x.Id);
            e.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
            e.Property(x => x.UpdatedBy).HasMaxLength(255);
            e.HasIndex(x => new { x.ServerId, x.CreatedAt });

            e.HasOne(x => x.Server)
             .WithMany(s => s.Notes)
             .HasForeignKey(x => x.ServerId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── OperationalLog ────────────────────────────────────────────────
        modelBuilder.Entity<OperationalLog>(e =>
        {
            e.ToTable("operational_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            e.Property(x => x.Summary).IsRequired();
            e.Property(x => x.Actor).HasMaxLength(255).IsRequired();
            e.Property(x => x.Details).HasColumnType("jsonb");
            e.Property(x => x.Severity)
             .HasConversion(new EnumToStringConverter<LogSeverity>())
             .HasMaxLength(20);

            e.HasIndex(x => new { x.ServerId, x.CreatedAt });
            e.HasIndex(x => x.EventType);
            e.HasIndex(x => x.Actor);
            e.HasIndex(x => x.CreatedAt);

            e.HasOne(x => x.Server)
             .WithMany(s => s.OperationalLogs)
             .HasForeignKey(x => x.ServerId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ── ServerGroup ───────────────────────────────────────────────────
        modelBuilder.Entity<ServerGroup>(e =>
        {
            e.ToTable("server_groups");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Color).HasMaxLength(7);
        });

        // ── ServerGroupMembership ─────────────────────────────────────────
        modelBuilder.Entity<ServerGroupMembership>(e =>
        {
            e.ToTable("server_group_memberships");
            e.HasKey(x => new { x.ServerId, x.GroupId });
            e.Property(x => x.AddedBy).HasMaxLength(255);

            e.HasOne(x => x.Server)
             .WithMany(s => s.GroupMemberships)
             .HasForeignKey(x => x.ServerId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Group)
             .WithMany(g => g.Memberships)
             .HasForeignKey(x => x.GroupId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
