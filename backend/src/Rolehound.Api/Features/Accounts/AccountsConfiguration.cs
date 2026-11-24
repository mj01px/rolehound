using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rolehound.Api.Features.Accounts.Admin;

namespace Rolehound.Api.Features.Accounts;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.Property(u => u.Name).HasMaxLength(AdminUsers.NameMaxLength);
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(16);
        builder.Property(u => u.TwoFactorMethod).HasConversion<string>().HasMaxLength(16);
        builder.Ignore(u => u.HasAccess);
    }
}

internal sealed class AccountTokenConfiguration : IEntityTypeConfiguration<AccountToken>
{
    public void Configure(EntityTypeBuilder<AccountToken> builder)
    {
        builder.ToTable("account_tokens");
        builder.Property(t => t.Purpose).HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.Hash).HasMaxLength(32);
        builder.Property(t => t.Payload).HasMaxLength(256);
        // Not unique: a person can, rarely, receive the same six-digit code twice.
        builder.HasIndex(t => t.Hash);
        builder.HasIndex(t => new { t.UserId, t.Purpose });
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEvent>
{
    public void Configure(EntityTypeBuilder<SecurityEvent> builder)
    {
        builder.ToTable("security_events");
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(48);
        builder.Property(e => e.IpAddress).HasMaxLength(45);
        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => new { e.UserId, e.OccurredAt });
        builder.HasIndex(e => new { e.ActorId, e.OccurredAt });
    }
}
