using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Jobs;

namespace Rolehound.Api.Features.TailoredResume;

public enum GenerationStatus
{
    Queued,
    Running,
    Done,
    Failed,
}

/// <summary>
/// One tailored resume request and its result (FR-AI-10, FR-AI-11). The queue
/// lives in this table, so a restart loses nothing. The PDF is rendered on
/// demand from <see cref="Document"/>, so it can be downloaded again.
/// </summary>
public sealed class ResumeGeneration
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid UserId { get; init; }

    public Guid JobId { get; init; }

    /// <summary>"pt" or "en".</summary>
    public required string Language { get; init; }

    public GenerationStatus Status { get; set; }

    public string? Error { get; set; }

    /// <summary>The <see cref="ResumeDocument"/> as JSON, once done.</summary>
    public string? Document { get; set; }

    /// <summary>The <see cref="ResumeChange"/> list as JSON.</summary>
    public string? Changes { get; set; }

    /// <summary>The <see cref="ResumeCut"/> list as JSON.</summary>
    public string? Cuts { get; set; }

    public string? Model { get; set; }

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public int DossierVersion { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}

internal sealed class ResumeGenerationConfiguration : IEntityTypeConfiguration<ResumeGeneration>
{
    public void Configure(EntityTypeBuilder<ResumeGeneration> builder)
    {
        builder.ToTable("resume_generations");
        builder.Property(g => g.Language).HasMaxLength(2);
        builder.Property(g => g.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(g => g.Error).HasMaxLength(500);
        builder.Property(g => g.Document).HasColumnType("jsonb");
        builder.Property(g => g.Changes).HasColumnType("jsonb");
        builder.Property(g => g.Cuts).HasColumnType("jsonb");
        builder.Property(g => g.Model).HasMaxLength(200);
        builder.HasIndex(g => new { g.Status, g.CreatedAt });
        builder.HasIndex(g => new { g.UserId, g.JobId });
        builder.HasIndex(g => g.JobId);

        // FR-AI-11, enforced by the database: at most one queued or running generation per person.
        builder.HasIndex(g => g.UserId)
            .IsUnique()
            .HasFilter("status IN ('Queued', 'Running')")
            .HasDatabaseName("ux_resume_generations_one_in_progress");
        builder.HasOne<User>().WithMany().HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Job>().WithMany().HasForeignKey(g => g.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}
