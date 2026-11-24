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

/// <summary>
/// Someone tried to follow a company on a job platform Rolehound does not read
/// yet. The admin sees the count per platform, to decide which connector comes next.
/// Only the host is kept, never the full link.
/// </summary>
public sealed class ConnectorRequest
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Platform { get; init; }

    public required string Host { get; init; }

    public Guid UserId { get; init; }

    public DateTimeOffset RequestedAt { get; init; }
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
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
