using Microsoft.Extensions.Options;
using Rolehound.Api.Infrastructure;
using Rolehound.Api.Infrastructure.Email;

namespace Rolehound.Api.Features.Accounts;

/// <summary>Account emails, in Brazilian Portuguese, plain text.</summary>
public sealed class AccountEmails(IEmailSender sender, IEmailOutbox outbox, IOptions<WebOptions> web)
{
    private const string Footer = "\n\nSe não foi você, ignore este e-mail ou avise o administrador.\n\nrolehound";

    public Task SendAccessLinkAsync(User user, string token, CancellationToken ct) =>
        Send(user.Email!, "Seu acesso ao rolehound",
            $"Olá, {user.UserName}.\n\nSeu acesso ao rolehound foi liberado. Defina sua senha pelo link abaixo:\n\n{web.Value.Link("acesso", token)}\n\nO link vale por 72 horas e só pode ser usado uma vez.", ct);

    /// <summary>Queued, not awaited: forgot password must not reveal through its timing or errors that the account exists.</summary>
    public void QueuePasswordReset(User user, string token) =>
        outbox.Enqueue(new EmailMessage(user.Email!, "Redefinir sua senha do rolehound",
            $"Recebemos um pedido para redefinir sua senha. Crie uma senha nova pelo link abaixo:\n\n{web.Value.Link("nova-senha", token)}\n\nO link vale por 1 hora e só pode ser usado uma vez.{Footer}"));

    public Task SendCodeAsync(User user, string code, CancellationToken ct) =>
        Send(user.Email!, $"Seu código do rolehound: {code}",
            $"Seu código de verificação é {code}.\n\nEle vale por 10 minutos e só pode ser usado uma vez.{Footer}", ct);

    public Task SendEmailChangeLinkAsync(User user, string newEmail, string token, CancellationToken ct) =>
        Send(newEmail, "Confirme seu novo e-mail no rolehound",
            $"Olá, {user.UserName}.\n\nConfirme que este é o seu novo e-mail de acesso pelo link abaixo:\n\n{web.Value.Link("confirmar-email", token)}\n\nO link vale por 24 horas.{Footer}", ct);

    public Task SendNoticeAsync(string to, string what, CancellationToken ct) =>
        Send(to, "Alteração na sua conta do rolehound",
            $"{what}{Footer}", ct);

    private Task Send(string to, string subject, string body, CancellationToken ct) =>
        sender.SendAsync(new EmailMessage(to, subject, body), ct);
}
