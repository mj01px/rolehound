using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Jobs;
using Rolehound.Scoring;

namespace Rolehound.Api.Features.Accuracy;

public enum SampleItemStatus
{
    Pending,
    Labelled,
    Skipped,
}

/// <summary>
/// One highlight in a person's monthly labelled sample (BR-ACU-01). What the
/// app showed is frozen here, so a later rescore or deleted job does not change
/// the month's numbers.
/// </summary>
public sealed class SampleItem
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid UserId { get; init; }

    /// <summary>Kept after the job is deleted by retention; the label still counts.</summary>
    public Guid? JobId { get; set; }

    /// <summary>First day of the month (America/Sao_Paulo) the sample belongs to.</summary>
    public DateOnly Month { get; init; }

    public required string Title { get; init; }

    public required string Company { get; init; }

    public int Score { get; init; }

    public WorkMode ShownWorkMode { get; init; }

    public SampleItemStatus Status { get; set; }

    public bool? Relevant { get; set; }

    /// <summary>Null when the person does not know.</summary>
    public bool? WorkModeRight { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LabelledAt { get; set; }
}

internal sealed class SampleItemConfiguration : IEntityTypeConfiguration<SampleItem>
{
    public void Configure(EntityTypeBuilder<SampleItem> builder)
    {
        builder.ToTable("sample_items");
        builder.Property(s => s.Title).HasMaxLength(500);
        builder.Property(s => s.Company).HasMaxLength(200);
        builder.Property(s => s.ShownWorkMode).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(s => new { s.UserId, s.Month });
        builder.HasIndex(s => new { s.UserId, s.JobId });
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Job>().WithMany().HasForeignKey(s => s.JobId).OnDelete(DeleteBehavior.SetNull);
    }
}
