namespace Rolehound.Api.Infrastructure.Email;

/// <summary>A plain-text email. Templates live with the module that sends them.</summary>
public sealed record EmailMessage(string To, string Subject, string Body);
