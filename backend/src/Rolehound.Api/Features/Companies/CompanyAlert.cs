using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Collection;

namespace Rolehound.Api.Features.Companies;

/// <summary>A person follows a company (a collection target): new jobs from it that match their profile reach their notifications.</summary>
public sealed class CompanyAlert
{
    public Guid UserId { get; init; }

    public Guid TargetId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public enum ConnectorRequestStatus
{
    Open,

    /// <summary>The platform is read now; the people who asked hear about it.</summary>
    Done,

    /// <summary>Not going to be read (a closed portal, too few companies); nobody is told.</summary>
    Declined,
}

/// <summary>
/// Someone tried to follow a company on a job platform Rolehound does not read
/// yet. The admin sees the requests per platform, to decide which connector comes next,
/// and closes them as done or declined.
/// Only the host is kept, never the full link.
/// </summary>
public sealed class ConnectorRequest
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Platform { get; init; }

    public required string Host { get; init; }

    public Guid UserId { get; init; }

    public DateTimeOffset RequestedAt { get; init; }

    public ConnectorRequestStatus Status { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Why it was declined; shown to the admin only.</summary>
    public string? Reason { get; set; }

    /// <summary>Closed as done from the panel: the person who asked is told (bell and Telegram).</summary>
    public bool Announced { get; set; }
}

internal sealed class CompanyAlertConfiguration : IEntityTypeConfiguration<CompanyAlert>
{
    public void Configure(EntityTypeBuilder<CompanyAlert> builder)
    {
        builder.ToTable("company_alerts");
        builder.HasKey(a => new { a.UserId, a.TargetId });
        builder.HasIndex(a => a.TargetId);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SourceTarget>().WithMany().HasForeignKey(a => a.TargetId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ConnectorRequestConfiguration : IEntityTypeConfiguration<ConnectorRequest>
{
    public void Configure(EntityTypeBuilder<ConnectorRequest> builder)
    {
        builder.ToTable("connector_requests");
        builder.Property(r => r.Platform).HasMaxLength(40);
        builder.Property(r => r.Host).HasMaxLength(255);
        builder.HasIndex(r => r.Platform);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.Reason).HasMaxLength(200);
        builder.HasIndex(r => new { r.UserId, r.Status });
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
