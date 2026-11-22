using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Jobs;

namespace Rolehound.Api.Features.Funnel;

internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    public void Configure(EntityTypeBuilder<Application> builder)
    {
        builder.ToTable("applications", t => t.HasCheckConstraint("ck_applications_priority", "priority BETWEEN 1 AND 5"));
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.NextStep).HasMaxLength(300);
        builder.Property(a => a.Contact).HasMaxLength(200);
        builder.Property(a => a.Notes).HasMaxLength(5000);
        builder.Ignore(a => a.IsActive);
        builder.HasIndex(a => new { a.UserId, a.JobId }).IsUnique();
        builder.HasIndex(a => a.JobId);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Job>().WithMany().HasForeignKey(a => a.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InteractionConfiguration : IEntityTypeConfiguration<Interaction>
{
    public void Configure(EntityTypeBuilder<Interaction> builder)
    {
        builder.ToTable("interactions");
        builder.Property(i => i.Title).HasMaxLength(200);
        builder.Property(i => i.Detail).HasMaxLength(5000);
        builder.HasIndex(i => new { i.ApplicationId, i.Date });
        builder.HasOne<Application>().WithMany().HasForeignKey(i => i.ApplicationId).OnDelete(DeleteBehavior.Cascade);
    }
}
