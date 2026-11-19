using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts.SignIn;

public sealed record TokenRequest(string? Token);

public sealed record AccessLinkResponse(string Email, string Username, string? GrantedBy);

public sealed record AcceptAccessRequest(string? Token, string? Password);

/// <summary>
/// The access link the admin sends (FR-ACC-02, FR-ACC-04):
/// POST /auth/access/inspect shows who it is for; POST /auth/access/accept sets the password.
/// </summary>
internal static class FirstAccess
{
    internal const string LinkInvalid = "Este link não vale mais. Peça um novo ao administrador.";

    public static async Task<Results<Ok<AccessLinkResponse>, ProblemHttpResult>> Inspect(
        TokenRequest request, AccountTokens tokens, UserManager<User> users, CancellationToken ct)
    {
        var token = await tokens.FindLinkAsync(request.Token, AccountTokenPurpose.Access, ct);
        var user = token is null ? null : await users.FindByIdAsync(token.UserId.ToString());
        if (token is null || user is null || user.HasAccess || !user.IsActive)
        {
            return Problems.BadRequest("link_invalid", LinkInvalid);
        }

        return TypedResults.Ok(new AccessLinkResponse(user.Email!, user.UserName!, token.Payload));
    }

    public static async Task<Results<Ok<LoginResponse>, ProblemHttpResult, ValidationProblem>> Accept(
        AcceptAccessRequest request,
        AccountTokens tokens,
        UserManager<User> users,
        SignInManager<User> signIn,
        SecurityLog log,
        RolehoundDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var token = await tokens.FindLinkAsync(request.Token, AccountTokenPurpose.Access, ct);
        var user = token is null ? null : await users.FindByIdAsync(token.UserId.ToString());
        if (token is null || user is null || user.HasAccess || !user.IsActive)
        {
            return Problems.BadRequest("link_invalid", LinkInvalid);
        }

        if (string.IsNullOrEmpty(request.Password))
        {
            return Problems.Validation("password", "Informe a senha.");
        }

        tokens.Consume(token);
        user.EmailConfirmed = true;
        user.LastLoginAt = time.GetUtcNow();
        log.Record(SecurityEventType.AccessGranted, user.Id);

        // Validates the password rules and saves the token, user and event together.
        var result = await users.AddPasswordAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return Problems.Validation("password", result);
        }

        await db.SaveChangesAsync(ct);
        await signIn.SignInAsync(user, isPersistent: true);
        return TypedResults.Ok(new LoginResponse("signedIn", null));
    }
}
