using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rolehound.Api.Features.Accounts;

namespace Rolehound.Api.Features.Dossier;

/// <summary>
/// One import of a person's Dossier. Every import is a new version, kept while
/// the account exists, because AI generations point at the version they used (FR-DOS-06).
/// </summary>
public sealed class DossierVersion
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid UserId { get; init; }

    /// <summary>1, 2, 3... per person.</summary>
    public int Number { get; init; }

    public int SchemaVersion { get; init; }

    /// <summary>The validated JSON, as imported.</summary>
    public required string Content { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class DossierVersionConfiguration : IEntityTypeConfiguration<DossierVersion>
{
    public void Configure(EntityTypeBuilder<DossierVersion> builder)
    {
        builder.ToTable("dossier_versions");
        builder.Property(d => d.Content).HasColumnType("jsonb");
        builder.HasIndex(d => new { d.UserId, d.Number }).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
