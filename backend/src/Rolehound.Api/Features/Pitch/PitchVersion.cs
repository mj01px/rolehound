using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Jobs;

namespace Rolehound.Api.Features.Pitch;

/// <summary>One generated "tell us about yourself" text. Every version is kept (FR-AI-05).</summary>
public sealed class PitchVersion
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid UserId { get; init; }

    public Guid JobId { get; init; }

    /// <summary>1, 2, 3... per person and job.</summary>
    public int Number { get; init; }

    /// <summary>"pt" or "en".</summary>
    public required string Language { get; init; }

    public string? Instruction { get; init; }

    public required string Text { get; init; }

    public required string Model { get; init; }

    public int PromptTokens { get; init; }

    public int CompletionTokens { get; init; }

    public int DossierVersion { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class PitchVersionConfiguration : IEntityTypeConfiguration<PitchVersion>
{
    public void Configure(EntityTypeBuilder<PitchVersion> builder)
    {
        builder.ToTable("pitch_versions");
        builder.Property(p => p.Language).HasMaxLength(2);
        builder.Property(p => p.Instruction).HasMaxLength(500);
        builder.Property(p => p.Text).HasMaxLength(10_000);
        builder.Property(p => p.Model).HasMaxLength(200);
        builder.HasIndex(p => new { p.UserId, p.JobId, p.Number }).IsUnique();
        builder.HasIndex(p => p.JobId);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Job>().WithMany().HasForeignKey(p => p.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}
