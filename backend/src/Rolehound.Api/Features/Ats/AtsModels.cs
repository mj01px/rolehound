using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Jobs;

namespace Rolehound.Api.Features.Ats;

/// <summary>
/// The person's resume for ATS analysis (FR-DOS-08). Only the extracted text is
/// kept: the analysis needs nothing else, and the file itself is never stored.
/// </summary>
public sealed class ResumeText
{
    public Guid UserId { get; init; }

    public required string FileName { get; set; }

    public required string Text { get; set; }

    public int Pages { get; set; }

    public DateTimeOffset UploadedAt { get; set; }
}

/// <summary>One ATS analysis of the resume against one job (FR-AI-06). Running it again replaces it.</summary>
public sealed class AtsReport
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid UserId { get; init; }

    public Guid JobId { get; init; }

    /// <summary>0 to 100.</summary>
    public int Score { get; set; }

    /// <summary>strong, moderate or weak.</summary>
    public required string Verdict { get; set; }

    public required string Summary { get; set; }

    public List<string> MatchedKeywords { get; set; } = [];

    public List<string> MissingKeywords { get; set; } = [];

    public List<string> Strengths { get; set; } = [];

    public List<string> Gaps { get; set; } = [];

    public List<string> Suggestions { get; set; } = [];

    /// <summary>The resume this analysis read, so the report says what it scored.</summary>
    public required string ResumeFileName { get; set; }

    public required string Model { get; set; }

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class ResumeTextConfiguration : IEntityTypeConfiguration<ResumeText>
{
    public void Configure(EntityTypeBuilder<ResumeText> builder)
    {
        builder.ToTable("resume_texts");
        builder.HasKey(r => r.UserId);
        builder.Property(r => r.FileName).HasMaxLength(255);
        builder.Property(r => r.Text).HasMaxLength(ResumeEndpoints.MaxTextLength);
        builder.HasOne<User>().WithOne().HasForeignKey<ResumeText>(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AtsReportConfiguration : IEntityTypeConfiguration<AtsReport>
{
    public void Configure(EntityTypeBuilder<AtsReport> builder)
    {
        builder.ToTable("ats_reports", t => t.HasCheckConstraint("ck_ats_reports_score", "score BETWEEN 0 AND 100"));
        builder.Property(r => r.Verdict).HasMaxLength(16);
        builder.Property(r => r.Summary).HasMaxLength(1000);
        builder.Property(r => r.MatchedKeywords).HasColumnType("jsonb");
        builder.Property(r => r.MissingKeywords).HasColumnType("jsonb");
        builder.Property(r => r.Strengths).HasColumnType("jsonb");
        builder.Property(r => r.Gaps).HasColumnType("jsonb");
        builder.Property(r => r.Suggestions).HasColumnType("jsonb");
        builder.Property(r => r.ResumeFileName).HasMaxLength(255);
        builder.Property(r => r.Model).HasMaxLength(200);
        builder.HasIndex(r => new { r.UserId, r.JobId }).IsUnique();
        builder.HasIndex(r => r.JobId);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Job>().WithMany().HasForeignKey(r => r.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}
