using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts.Admin;

public sealed record AdminUserResponse(
    Guid Id,
    string Email,
    string Username,
    string Role,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? AccessLinkExpiresAt);

public sealed record CreateUserRequest(string? Email, string? Username, string? Role);

public sealed record ChangeRoleRequest(string? Role);

/// <summary>
/// User administration (FR-ACC-01, 03, 18, 19, 20). The admin sees accounts,
/// never their content. Precision and onboarding step join this list in later slices.
/// </summary>
internal static partial class AdminUsers
{
    public static async Task<Ok<List<AdminUserResponse>>> List(RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var users = await db.Users.AsNoTracking().OrderBy(u => u.CreatedAt).ToListAsync(ct);
        var pendingLinks = await db.AccountTokens.AsNoTracking()
            .Where(t => t.Purpose == AccountTokenPurpose.Access && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
            .GroupBy(t => t.UserId)
            .Select(g => new { UserId = g.Key, ExpiresAt = g.Max(t => t.ExpiresAt) })
            .ToDictionaryAsync(x => x.UserId, x => x.ExpiresAt, ct);

        return TypedResults.Ok(users.Select(u => ToResponse(u, pendingLinks.GetValueOrDefault(u.Id))).ToList());
    }

    public static async Task<Results<Created<AdminUserResponse>, ProblemHttpResult, ValidationProblem>> Create(
        CreateUserRequest request,
        ClaimsPrincipal principal,
        UserManager<User> users,
        AccountTokens tokens,
        AccountEmails emails,
        SecurityLog log,
        RolehoundDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var email = request.Email?.Trim();
        var username = request.Username?.Trim().ToLowerInvariant();
        if (!Validation.IsEmail(email))
        {
            errors["email"] = ["E-mail inválido."];
        }

        if (username is null || !UsernamePattern().IsMatch(username))
        {
            errors["username"] = ["Use de 3 a 32 caracteres: letras minúsculas, números, ponto, hífen ou sublinhado."];
        }

        if (!TryParseRole(request.Role, out var role))
        {
            errors["role"] = ["Papel inválido."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var user = new User { Email = email, UserName = username, Role = role, CreatedAt = time.GetUtcNow() };
        var created = await users.CreateAsync(user);
        if (!created.Succeeded)
        {
            var field = created.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.InvalidEmail)) ? "email" : "username";
            return Problems.Validation(field, created);
        }

        var admin = await users.GetUserAsync(principal);
        var token = await tokens.IssueLinkAsync(user.Id, AccountTokenPurpose.Access, AccountTokens.AccessLifetime, admin?.UserName, ct);
        log.Record(SecurityEventType.UserCreated, user.Id, admin?.Id);
        await db.SaveChangesAsync(ct);
        await emails.SendAccessLinkAsync(user, token, ct);

        return TypedResults.Created($"/admin/users/{user.Id}", ToResponse(user, time.GetUtcNow() + AccountTokens.AccessLifetime));
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> ResendAccessLink(
        Guid id, ClaimsPrincipal principal, UserManager<User> users, AccountTokens tokens, AccountEmails emails, RolehoundDbContext db, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Problems.NotFound();
        }

        if (user.HasAccess)
        {
            return Problems.Conflict("already_has_access", "Essa pessoa já definiu a senha.");
        }

        var admin = await users.GetUserAsync(principal);
        var token = await tokens.IssueLinkAsync(user.Id, AccountTokenPurpose.Access, AccountTokens.AccessLifetime, admin?.UserName, ct);
        await db.SaveChangesAsync(ct);
        await emails.SendAccessLinkAsync(user, token, ct);
        return TypedResults.NoContent();
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> Deactivate(
        Guid id, ClaimsPrincipal principal, UserManager<User> users, SecurityLog log, RolehoundDbContext db, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Problems.NotFound();
        }

        if (user.Id == principal.GetUserId())
        {
            return Problems.Conflict("own_account", "Você não pode desativar a própria conta.");
        }

        if (await IsLastActiveAdminAsync(user, db, ct))
        {
            return Problems.Conflict("last_admin", "O sistema precisa de pelo menos um admin ativo.");
        }

        user.IsActive = false;
        log.Record(SecurityEventType.UserDeactivated, user.Id, principal.GetUserId());
        // Rotating the stamp ends every session at the next request.
        await users.UpdateSecurityStampAsync(user);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> Reactivate(
        Guid id, ClaimsPrincipal principal, UserManager<User> users, SecurityLog log, RolehoundDbContext db, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Problems.NotFound();
        }

        user.IsActive = true;
        log.Record(SecurityEventType.UserReactivated, user.Id, principal.GetUserId());
        await users.UpdateAsync(user);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    public static async Task<Results<NoContent, ProblemHttpResult, ValidationProblem>> ChangeRole(
        Guid id, ChangeRoleRequest request, ClaimsPrincipal principal, UserManager<User> users, SecurityLog log, RolehoundDbContext db, CancellationToken ct)
    {
        if (!TryParseRole(request.Role, out var role))
        {
            return Problems.Validation("role", "Papel inválido.");
        }

        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Problems.NotFound();
        }

        if (user.Id == principal.GetUserId())
        {
            return Problems.Conflict("own_role", "Ninguém muda o próprio papel.");
        }

        if (role != UserRole.Admin && await IsLastActiveAdminAsync(user, db, ct))
        {
            return Problems.Conflict("last_admin", "O sistema precisa de pelo menos um admin ativo.");
        }

        user.Role = role;
        log.Record(SecurityEventType.RoleChanged, user.Id, principal.GetUserId());
        // The role rides in the session; a new stamp makes the change apply at once.
        await users.UpdateSecurityStampAsync(user);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<bool> IsLastActiveAdminAsync(User user, RolehoundDbContext db, CancellationToken ct) =>
        user is { Role: UserRole.Admin, IsActive: true }
        && !await db.Users.AnyAsync(u => u.Id != user.Id && u.Role == UserRole.Admin && u.IsActive, ct);

    private static bool TryParseRole(string? value, out UserRole role) =>
        Enum.TryParse(value, ignoreCase: true, out role) && Enum.IsDefined(role);

    private static AdminUserResponse ToResponse(User user, DateTimeOffset? accessLinkExpiresAt) => new(
        user.Id,
        user.Email!,
        user.UserName!,
        user.Role.ToString().ToLowerInvariant(),
        !user.IsActive ? "deactivated" : user.HasAccess ? "active" : "pending",
        user.CreatedAt,
        user.LastLoginAt,
        user.HasAccess ? null : accessLinkExpiresAt);

    [GeneratedRegex("^[a-z0-9._-]{3,32}$")]
    private static partial Regex UsernamePattern();
}
