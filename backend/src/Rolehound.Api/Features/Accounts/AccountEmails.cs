using Microsoft.Extensions.Options;
using Rolehound.Api.Infrastructure;
using Rolehound.Api.Infrastructure.Email;

namespace Rolehound.Api.Features.Accounts;

/// <summary>Account emails, in Brazilian Portuguese, in the shared layout (<see cref="EmailLayout"/>) with a plain-text copy.</summary>
public sealed class AccountEmails(IEmailSender sender, IEmailOutbox outbox, IOptions<WebOptions> web)
{
    public Task SendAccessLinkAsync(User user, string token, CancellationToken ct) =>
        Send(user.Email!, "Seu acesso ao rolehound", new EmailContent(
            "Defina sua senha e comece a usar o rolehound.",
            $"Olá, {NameOf(user)}. Seu acesso foi liberado.",
            ["O rolehound junta vagas do Brasil, dá a cada uma uma nota pelo seu perfil e organiza suas candidaturas num funil só.", "Para entrar, defina sua senha:"],
            new EmailAction("Definir minha senha", web.Value.Link("acesso", token)),
            Note: "O link vale por 72 horas e só pode ser usado uma vez."), ct);

    /// <summary>Queued, not awaited: forgot password must not reveal through its timing or errors that the account exists.</summary>
    public void QueuePasswordReset(User user, string token) =>
        outbox.Enqueue(EmailLayout.Render(user.Email!, "Redefinir sua senha do rolehound", new EmailContent(
            "Crie uma senha nova para sua conta.",
            "Redefinir sua senha",
            ["Recebemos um pedido para redefinir a senha da sua conta. Crie uma senha nova pelo botão abaixo."],
            new EmailAction("Criar senha nova", web.Value.Link("nova-senha", token)),
            Note: "O link vale por 1 hora e só pode ser usado uma vez. Se você não pediu, sua senha atual continua valendo.")));

    public Task SendCodeAsync(User user, string code, CancellationToken ct) =>
        Send(user.Email!, $"Seu código do rolehound: {code}", new EmailContent(
            $"Seu código de verificação é {code}.",
            "Seu código de verificação",
            ["Digite este código no rolehound para continuar:"],
            Code: code,
            Note: "Ele vale por 10 minutos e só pode ser usado uma vez. Ninguém do rolehound vai pedir este código para você."), ct);

    public Task SendEmailChangeLinkAsync(User user, string newEmail, string token, CancellationToken ct) =>
        Send(newEmail, "Confirme seu novo e-mail no rolehound", new EmailContent(
            "Confirme que este é o seu novo e-mail de acesso.",
            "Confirme seu novo e-mail",
            [$"Olá, {NameOf(user)}. Pediram para usar este endereço como o e-mail de acesso da sua conta do rolehound."],
            new EmailAction("Confirmar este e-mail", web.Value.Link("confirmar-email", token)),
            Note: "O link vale por 24 horas. Até lá, você continua entrando com o e-mail atual."), ct);

    /// <summary>A change to the account the person should know about (password, email, two-step verification).</summary>
    public Task SendNoticeAsync(string to, string what, CancellationToken ct) =>
        Send(to, "Alteração na sua conta do rolehound", new EmailContent(
            what,
            "Alteração na sua conta",
            [what],
            new EmailAction("Abrir meu perfil", new Uri($"{web.Value.BaseUrl.TrimEnd('/')}/perfil")),
            Note: "Se não foi você, troque sua senha agora e use \"Sair de todas\" em Perfil › Sessões."), ct);

    private static string NameOf(User user) =>
        string.IsNullOrWhiteSpace(user.Name) ? user.UserName! : user.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

    private Task Send(string to, string subject, EmailContent content, CancellationToken ct) =>
        sender.SendAsync(EmailLayout.Render(to, subject, content), ct);
}
