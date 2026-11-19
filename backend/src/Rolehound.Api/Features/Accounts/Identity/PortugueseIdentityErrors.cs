using Microsoft.AspNetCore.Identity;

namespace Rolehound.Api.Features.Accounts.Identity;

/// <summary>Identity messages shown to people, in Brazilian Portuguese.</summary>
internal sealed class PortugueseIdentityErrors : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"A senha precisa ter pelo menos {length} caracteres." };

    public override IdentityError PasswordRequiresUpper() =>
        new() { Code = nameof(PasswordRequiresUpper), Description = "A senha precisa de pelo menos uma letra maiúscula." };

    public override IdentityError PasswordRequiresDigit() =>
        new() { Code = nameof(PasswordRequiresDigit), Description = "A senha precisa de pelo menos um número." };

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        new() { Code = nameof(PasswordRequiresNonAlphanumeric), Description = "A senha precisa de pelo menos um caractere especial." };

    public override IdentityError PasswordMismatch() =>
        new() { Code = nameof(PasswordMismatch), Description = "Senha incorreta." };

    public override IdentityError DuplicateEmail(string email) =>
        new() { Code = nameof(DuplicateEmail), Description = "Esse e-mail já está em uso." };

    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = "Esse usuário já está em uso." };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = "E-mail inválido." };

    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = "Usuário inválido: use letras minúsculas, números, ponto, hífen ou sublinhado." };

    public override IdentityError InvalidToken() =>
        new() { Code = nameof(InvalidToken), Description = "Link ou código inválido ou expirado." };
}
