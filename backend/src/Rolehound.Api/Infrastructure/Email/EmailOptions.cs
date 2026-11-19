using System.ComponentModel.DataAnnotations;

namespace Rolehound.Api.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string Section = "Email";

    [Required, EmailAddress]
    public string From { get; init; } = "no-reply@rolehound.local";

    public string FromName { get; init; } = "rolehound";

    /// <summary>SMTP host. Empty in development means emails are only logged.</summary>
    public string? SmtpHost { get; init; }

    [Range(1, 65535)]
    public int SmtpPort { get; init; } = 2525;

    public string? SmtpUser { get; init; }

    public string? SmtpPassword { get; init; }

    /// <summary>STARTTLS on submission ports. Off only for a local catcher such as Mailpit.</summary>
    public bool UseStartTls { get; init; } = true;
}
