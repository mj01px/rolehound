namespace Rolehound.Api.Infrastructure.Email;

/// <summary>
/// An email: <see cref="Body"/> is the plain-text version every client can show; <see cref="Html"/>,
/// when set, is the formatted one (see <see cref="EmailLayout"/>). Templates live with the module that sends them.
/// </summary>
public sealed record EmailMessage(string To, string Subject, string Body, string? Html = null);
