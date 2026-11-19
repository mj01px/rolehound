using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rolehound.Api.Features.Accounts;

namespace Rolehound.Api.Features.ScoreProfiles;

/// <summary>A person's active score profile and search terms, one per person.</summary>
public sealed class UserScoreProfile
{
    public Guid UserId { get; init; }

    /// <summary><see cref="ProfileDocument"/> as JSON.</summary>
    public required string Document { get; set; }

    /// <summary>The Dossier version the profile was last generated from.</summary>
    public int? DossierVersion { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class UserScoreProfileConfiguration : IEntityTypeConfiguration<UserScoreProfile>
{
    public void Configure(EntityTypeBuilder<UserScoreProfile> builder)
    {
        builder.ToTable("score_profiles");
        builder.HasKey(p => p.UserId);
        builder.Property(p => p.Document).HasColumnType("jsonb");
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
