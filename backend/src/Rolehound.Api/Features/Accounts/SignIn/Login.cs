using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Rolehound.Api.Features.Accounts.Identity;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts.SignIn;

public sealed record LoginRequest(string? Email, string? Password);

/// <summary><c>status</c> is <c>signedIn</c> or <c>twoFactorRequired</c>; <c>method</c> tells which second factor.</summary>
public sealed record LoginResponse(string Status, string? Method);

/// <summary>POST /auth/login (FR-ACC-05, FR-ACC-15).</summary>
internal static class Login
{
    // One message for every failure, so the answer never reveals whether the email exists.
    internal const string InvalidCredentials = "E-mail ou senha incorretos. Depois de 5 tentativas o acesso fica bloqueado por 15 minutos.";

    public static async Task<Results<Ok<LoginResponse>, ProblemHttpResult, ValidationProblem>> Handle(
        LoginRequest request,
        UserManager<User> users,
        SignInManager<User> signIn,
        AccountEmails emails,
        SecurityLog log,
        RolehoundDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return Problems.Validation("email", "Informe e-mail e senha.");
        }

        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.HasAccess)
        {
            // Spend the same work as a real check, so timing does not reveal unknown emails.
            users.PasswordHasher.VerifyHashedPassword(new User(), DummyHash.Value, request.Password);
            return Problems.Unauthorized("invalid_credentials", InvalidCredentials);
        }

        var result = await signIn.PasswordSignInAsync(user, request.Password, isPersistent: true, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            user.LastLoginAt = time.GetUtcNow();
            log.Record(SecurityEventType.LoginSucceeded, user.Id);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(new LoginResponse("signedIn", null));
        }

        if (result.RequiresTwoFactor)
        {
            if (user.TwoFactorMethod == TwoFactorMethod.Email)
            {
                var code = await users.GenerateTwoFactorTokenAsync(user, EmailCodeTokenProvider.Name);
                await emails.SendCodeAsync(user, code, ct);
            }

            return TypedResults.Ok(new LoginResponse("twoFactorRequired", MethodName(user.TwoFactorMethod)));
        }

        log.Record(result.IsLockedOut ? SecurityEventType.LockedOut : SecurityEventType.LoginFailed, user.Id);
        await db.SaveChangesAsync(ct);
        return Problems.Unauthorized("invalid_credentials", InvalidCredentials);
    }

    internal static string MethodName(TwoFactorMethod method) => method switch
    {
        TwoFactorMethod.Totp => "totp",
        TwoFactorMethod.Email => "email",
        _ => "none",
    };

    private static class DummyHash
    {
        public static readonly string Value = new PasswordHasher<User>().HashPassword(new User(), "not-a-real-password");
    }
}
