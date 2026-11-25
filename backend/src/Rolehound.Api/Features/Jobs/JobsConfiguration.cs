using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Collection;

namespace Rolehound.Api.Features.Jobs;

internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs");
        builder.Property(j => j.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(j => j.ExternalId).HasMaxLength(200);
        builder.Property(j => j.Title).HasMaxLength(500);
        builder.Property(j => j.Company).HasMaxLength(200);
        builder.Property(j => j.Location).HasMaxLength(300);
        builder.Property(j => j.Url).HasMaxLength(1000);
        builder.Property(j => j.SourceWorkMode).HasConversion<string>().HasMaxLength(16);
        builder.Property(j => j.WorkMode).HasConversion<string>().HasMaxLength(16);
        builder.Property(j => j.Seniority).HasConversion<string>().HasMaxLength(16);
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(j => new { j.Source, j.ExternalId }).IsUnique();
        builder.HasIndex(j => j.TargetId);
        builder.HasIndex(j => new { j.Status, j.ClosedAt });
        builder.HasOne<SourceTarget>().WithMany().HasForeignKey(j => j.TargetId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class JobTombstoneConfiguration : IEntityTypeConfiguration<JobTombstone>
{
    public void Configure(EntityTypeBuilder<JobTombstone> builder)
    {
        builder.ToTable("job_tombstones");
        builder.HasKey(t => new { t.Source, t.ExternalId });
        builder.Property(t => t.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.ExternalId).HasMaxLength(200);
        builder.HasIndex(t => t.LastListedAt);
    }
}

internal sealed class UserJobConfiguration : IEntityTypeConfiguration<UserJob>
{
    public void Configure(EntityTypeBuilder<UserJob> builder)
    {
        builder.ToTable("user_jobs");
        builder.HasKey(u => new { u.UserId, u.JobId });
        builder.Property(u => u.Tags).HasColumnType("jsonb");
        builder.Property(u => u.MatchedGroups).HasColumnType("jsonb");
        builder.Property(u => u.ArchiveReason).HasMaxLength(32);
        builder.Property(u => u.ArchiveNote).HasMaxLength(500);
        builder.HasIndex(u => new { u.UserId, u.IsRelevant, u.Score });
        builder.HasIndex(u => u.JobId);
        builder.HasOne<User>().WithMany().HasForeignKey(u => u.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Job>().WithMany().HasForeignKey(u => u.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SourceTargetConfiguration : IEntityTypeConfiguration<SourceTarget>
{
    public void Configure(EntityTypeBuilder<SourceTarget> builder)
    {
        builder.ToTable("source_targets");
        builder.Property(t => t.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.Key).HasMaxLength(200);
        builder.Property(t => t.Label).HasMaxLength(120);
        builder.Property(t => t.LastError).HasMaxLength(500);
        builder.HasIndex(t => new { t.Source, t.Key }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.AddedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class CollectionRunConfiguration : IEntityTypeConfiguration<CollectionRun>
{
    public void Configure(EntityTypeBuilder<CollectionRun> builder)
    {
        builder.ToTable("collection_runs");
        builder.Property(r => r.Error).HasMaxLength(500);
        builder.Property(r => r.Warning).HasMaxLength(500);
        builder.HasIndex(r => r.StartedAt);
        builder.HasIndex(r => r.BatchId);
        builder.HasOne<SourceTarget>().WithMany().HasForeignKey(r => r.TargetId).OnDelete(DeleteBehavior.Cascade);
    }
}
