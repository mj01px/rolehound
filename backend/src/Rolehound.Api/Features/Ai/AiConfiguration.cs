using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rolehound.Api.Features.Accounts;

namespace Rolehound.Api.Features.Ai;

internal sealed class AiSettingsConfiguration : IEntityTypeConfiguration<AiSettings>
{
    public void Configure(EntityTypeBuilder<AiSettings> builder)
    {
        builder.ToTable("ai_settings");
        builder.HasKey(s => s.UserId);
        builder.Property(s => s.KeyHint).HasMaxLength(4);
        builder.Property(s => s.ManagementKeyHint).HasMaxLength(4);
        builder.Property(s => s.PitchAndAtsModel).HasMaxLength(120);
        builder.Property(s => s.ResumeModel).HasMaxLength(120);
        builder.Property(s => s.ProfileAndFixModel).HasMaxLength(120);
        builder.Ignore(s => s.HasKey);
        builder.Ignore(s => s.HasModels);
        builder.HasOne<User>().WithOne().HasForeignKey<AiSettings>(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AiUsageConfiguration : IEntityTypeConfiguration<AiUsage>
{
    public void Configure(EntityTypeBuilder<AiUsage> builder)
    {
        builder.ToTable("ai_usage");
        builder.Property(u => u.Feature).HasConversion<string>().HasMaxLength(32);
        builder.Property(u => u.Model).HasMaxLength(120);
        builder.HasIndex(u => new { u.UserId, u.At });
        builder.HasOne<User>().WithMany().HasForeignKey(u => u.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
