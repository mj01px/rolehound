using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rolehound.Api.Features.Accounts;

namespace Rolehound.Api.Features.Telegram;

/// <summary>A person's connected Telegram chat (FR-NOT-01). One per person.</summary>
public sealed class TelegramLink
{
    public Guid UserId { get; init; }

    public long ChatId { get; set; }

    public DateTimeOffset LinkedAt { get; set; }

    /// <summary>Day (America/Sao_Paulo) the overdue digest last went out, so it goes once a day.</summary>
    public DateOnly? LastOverdueDigestOn { get; set; }
}

/// <summary>A single-use code that links a chat to a person; valid for 10 minutes (R19). Only its hash is stored.</summary>
public sealed class TelegramLinkCode
{
    public required string CodeHash { get; init; }

    public Guid UserId { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed class TelegramOptions
{
    /// <summary>The webhook secret is the only thing that authenticates Telegram's calls; it must not be guessable.</summary>
    public const int WebhookSecretMinLength = 32;

    public string? BotToken { get; set; }

    /// <summary>The bot's username, without @, for the t.me link.</summary>
    public string? BotUsername { get; set; }

    /// <summary>Sent back by Telegram on every webhook call; 32 to 256 of A-Z, a-z, 0-9, _ and -.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Public HTTPS address of POST /telegram/webhook; registered on start when set.</summary>
    public string? WebhookUrl { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(BotToken) && !string.IsNullOrWhiteSpace(BotUsername) && !string.IsNullOrWhiteSpace(WebhookSecret);
}

internal sealed class TelegramLinkConfiguration : IEntityTypeConfiguration<TelegramLink>
{
    public void Configure(EntityTypeBuilder<TelegramLink> builder)
    {
        builder.ToTable("telegram_links");
        builder.HasKey(l => l.UserId);
        builder.HasIndex(l => l.ChatId).IsUnique();
        builder.HasOne<User>().WithOne().HasForeignKey<TelegramLink>(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TelegramLinkCodeConfiguration : IEntityTypeConfiguration<TelegramLinkCode>
{
    public void Configure(EntityTypeBuilder<TelegramLinkCode> builder)
    {
        builder.ToTable("telegram_link_codes");
        builder.HasKey(c => c.CodeHash);
        builder.Property(c => c.CodeHash).HasMaxLength(64);
        builder.HasIndex(c => c.UserId);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
