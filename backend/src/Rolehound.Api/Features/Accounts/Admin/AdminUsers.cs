using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Accounts.Admin;

public sealed record AdminUserResponse(
    Guid Id,
    string Name,
    string Email,
    string Username,
    string Role,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? AccessLinkExpiresAt,
    bool OnboardingCompleted,
    string TwoFactorMethod,
    string? PendingEmail);

public sealed record CreateUserRequest(string? Name, string? Email, string? Username, string? Role);

/// <summary>An empty <see cref="Email"/> keeps the current address, which the admin never sees.</summary>
public sealed record HistoryPerson(Guid Id, string Name);

/// <summary>
/// One entry of an account's history: what happened (<see cref="Type"/>), to
/// whom (<see cref="Subject"/>) and who did it (<see cref="Actor"/>, null when
/// the person acted on their own account or nobody signed in, as on a failed login).
/// </summary>
public sealed record HistoryEntry(Guid Id, string Type, DateTimeOffset OccurredAt, HistoryPerson? Actor, HistoryPerson? Subject);

public sealed record HistoryPage(IReadOnlyList<HistoryEntry> Items, int Total, int Offset);

public sealed record UpdateUserRequest(string? Name, string? Email, string? Username);

public sealed record ChangeRoleRequest(string? Role);

/// <summary>
/// User administration (FR-ACC-01, 03, 18, 19, 20). The admin sees accounts,
/// never their content, and emails only masked (LGPD data minimization). Precision and onboarding step join this list in later slices.
/// </summary>
internal static partial class AdminUsers
{
    public const int NameMaxLength = 100;

    public static async Task<Ok<List<AdminUserResponse>>> List(RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().OrderBy(u => u.CreatedAt).ToListAsync(ct);
        var links = await OpenLinksAsync(db, time, null, ct);
        return TypedResults.Ok(users.Select(u => ToResponse(u, links)).ToList());
    }

    public static async Task<Results<Ok<AdminUserResponse>, ProblemHttpResult>> Get(Guid id, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? Problems.NotFound() : TypedResults.Ok(ToResponse(user, await OpenLinksAsync(db, time, id, ct)));
    }

    /// <summary>
    /// Edits name, email and username. A pending account gets the new email at
    /// once plus a fresh access link there. The email of an account in use is
    /// the person's alone to change (from their profile, confirmed by both
    /// addresses): an admin who could point it elsewhere could reset the
    /// password and take the account over.
    /// </summary>
    public static async Task<Results<Ok<AdminUserResponse>, ProblemHttpResult, ValidationProblem>> Update(
        Guid id,
        UpdateUserRequest request,
        ClaimsPrincipal principal,
        UserManager<User> users,
        AccountTokens tokens,
        AccountEmails emails,
        SecurityLog log,
        RolehoundDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return Problems.NotFound();
        }

        var name = request.Name?.Trim();
        var email = string.IsNullOrWhiteSpace(request.Email) ? user.Email : request.Email.Trim();
        var username = request.Username?.Trim().ToLowerInvariant();
        var errors = Validate(name, email, username);
        var emailChanged = email is not null && !string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase);
        if (emailChanged && user.HasAccess)
        {
            errors["email"] = ["Só a própria pessoa troca o e-mail de uma conta em uso, pelo perfil."];
        }
        else if (emailChanged && !errors.ContainsKey("email") && await users.FindByEmailAsync(email!) is not null)
        {
            errors["email"] = ["Esse e-mail já está em uso."];
        }

        if (username is not null && username != user.UserName && !errors.ContainsKey("username") && await users.FindByNameAsync(username) is not null)
        {
            errors["username"] = ["Esse usuário já está em uso."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var admin = await users.GetUserAsync(principal);
        user.Name = name!;
        if (username != user.UserName)
        {
            var renamed = await users.SetUserNameAsync(user, username);
            if (!renamed.Succeeded)
            {
                return Problems.Validation("username", renamed);
            }
        }

        string? accessLink = null;
        if (emailChanged)
        {
            var changed = await users.SetEmailAsync(user, email);
            if (!changed.Succeeded)
            {
                return Problems.Validation("email", changed);
            }

            accessLink = await tokens.IssueLinkAsync(user.Id, AccountTokenPurpose.Access, AccountTokens.AccessLifetime, admin?.UserName, ct);
        }

        await users.UpdateAsync(user);
        log.Record(SecurityEventType.UserEdited, user.Id, admin?.Id);
        await db.SaveChangesAsync(ct);

        if (accessLink is not null)
        {
            await emails.SendAccessLinkAsync(user, accessLink, ct);
        }

        return TypedResults.Ok(ToResponse(user, await OpenLinksAsync(db, time, user.Id, ct)));
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
        var name = request.Name?.Trim();
        var email = request.Email?.Trim();
        var username = request.Username?.Trim().ToLowerInvariant();
        var errors = Validate(name, email, username);
        if (!TryParseRole(request.Role, out var role))
        {
            errors["role"] = ["Papel inválido."];
        }

        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var user = new User { Name = name!, Email = email, UserName = username, Role = role, CreatedAt = time.GetUtcNow() };
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

        return TypedResults.Created($"/admin/users/{user.Id}", ToResponse(user, await OpenLinksAsync(db, time, user.Id, ct)));
    }

    public const int HistoryDefaultLimit = 20;
    public const int HistoryMaxLimit = 100;

    /// <summary>
    /// What happened to an account and what the person did to other accounts as
    /// an admin, newest first, within the log's retention, a page at a time
    /// (offset, like the job list). No IP addresses.
    /// </summary>
    public static async Task<Results<Ok<HistoryPage>, ProblemHttpResult, ValidationProblem>> History(
        Guid id, int? limit, int? offset, RolehoundDbContext db, CancellationToken ct)
    {
        var take = limit ?? HistoryDefaultLimit;
        var skip = offset ?? 0;
        if (take is < 1 or > HistoryMaxLimit)
        {
            return Problems.Validation("limit", $"Use de 1 a {HistoryMaxLimit}.");
        }

        if (skip < 0)
        {
            return Problems.Validation("offset", "Use 0 ou mais.");
        }

        if (!await db.Users.AnyAsync(u => u.Id == id, ct))
        {
            return Problems.NotFound();
        }

        var about = db.SecurityEvents.AsNoTracking().Where(e => e.UserId == id || e.ActorId == id);
        var total = await about.CountAsync(ct);
        var events = await about
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Skip(skip)
            .Take(take)
            .Select(e => new { e.Id, e.Type, e.OccurredAt, e.ActorId, e.UserId })
            .ToListAsync(ct);

        var ids = events.SelectMany(e => new[] { e.ActorId, e.UserId }).OfType<Guid>().Distinct().ToList();
        var people = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new HistoryPerson(u.Id, u.Name.Length > 0 ? u.Name : u.UserName!), ct);

        // A deleted account leaves its id behind in other people's history, with no name to show.
        HistoryPerson? Person(Guid? personId) => personId is { } key
            ? people.TryGetValue(key, out var person) ? person : new HistoryPerson(key, "Conta excluída")
            : null;

        var items = events.Select(e => new HistoryEntry(
            e.Id,
            e.Type.ToString(),
            e.OccurredAt,
            e.ActorId == e.UserId ? null : Person(e.ActorId),
            Person(e.UserId))).ToList();
        return TypedResults.Ok(new HistoryPage(items, total, skip));
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> ResendAccessLink(
        Guid id, ClaimsPrincipal principal, UserManager<User> users, AccountTokens tokens, AccountEmails emails, SecurityLog log, RolehoundDbContext db, CancellationToken ct)
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
        log.Record(SecurityEventType.AccessLinkSent, user.Id, admin?.Id);
        await db.SaveChangesAsync(ct);
        await emails.SendAccessLinkAsync(user, token, ct);
        return TypedResults.NoContent();
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> Deactivate(
        Guid id, ClaimsPrincipal principal, UserManager<User> users, AccountRevocation revocation, SecurityLog log, RolehoundDbContext db, CancellationToken ct)
    {
        await using var transaction = await LockAdminsAsync(db, ct);
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
        // Rotating the stamp ends every session at the next request; links and the Telegram chat end now.
        await revocation.RevokeAsync(user.Id, null, ct);
        await users.UpdateSecurityStampAsync(user);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
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

        await using var transaction = await LockAdminsAsync(db, ct);
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
        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Opens a transaction holding a lock every change to the set of active admins
    /// takes first, so two of them cannot both pass the last-admin check and leave none.
    /// </summary>
    internal static async Task<IDbContextTransaction> LockAdminsAsync(RolehoundDbContext db, CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7262001)", ct);
        return transaction;
    }

    internal static async Task<bool> IsLastActiveAdminAsync(User user, RolehoundDbContext db, CancellationToken ct) =>
        user is { Role: UserRole.Admin, IsActive: true }
        && !await db.Users.AnyAsync(u => u.Id != user.Id && u.Role == UserRole.Admin && u.IsActive, ct);

    private static bool TryParseRole(string? value, out UserRole role) =>
        Enum.TryParse(value, ignoreCase: true, out role) && Enum.IsDefined(role);

    private static Dictionary<string, string[]> Validate(string? name, string? email, string? username)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(name) || name.Length > NameMaxLength)
        {
            errors["name"] = [$"Informe o nome, com até {NameMaxLength} caracteres."];
        }

        if (!Validation.IsEmail(email))
        {
            errors["email"] = ["E-mail inválido."];
        }

        if (username is null || !UsernamePattern().IsMatch(username))
        {
            errors["username"] = ["Use de 3 a 32 caracteres: letras minúsculas, números, ponto, hífen ou sublinhado."];
        }

        return errors;
    }

    private sealed record OpenLinks(Dictionary<Guid, DateTimeOffset> Access, Dictionary<Guid, string> EmailChange);

    /// <summary>Unused, unexpired access and email change links, optionally for one account.</summary>
    private static async Task<OpenLinks> OpenLinksAsync(RolehoundDbContext db, TimeProvider time, Guid? userId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var open = await db.AccountTokens.AsNoTracking()
            .Where(t => (t.Purpose == AccountTokenPurpose.Access || t.Purpose == AccountTokenPurpose.EmailChange)
                && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now
                && (userId == null || t.UserId == userId))
            .Select(t => new { t.UserId, t.Purpose, t.ExpiresAt, t.Payload })
            .ToListAsync(ct);

        return new OpenLinks(
            open.Where(t => t.Purpose == AccountTokenPurpose.Access).GroupBy(t => t.UserId).ToDictionary(g => g.Key, g => g.Max(t => t.ExpiresAt)),
            open.Where(t => t.Purpose == AccountTokenPurpose.EmailChange && t.Payload is not null)
                .GroupBy(t => t.UserId).ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.ExpiresAt).First().Payload!));
    }

    private static AdminUserResponse ToResponse(User user, OpenLinks links) => new(
        user.Id,
        user.Name,
        MaskEmail(user.Email!),
        user.UserName!,
        user.Role.ToString().ToLowerInvariant(),
        !user.IsActive ? "deactivated" : user.HasAccess ? "active" : "pending",
        user.CreatedAt,
        user.LastLoginAt,
        user.HasAccess ? null : links.Access.GetValueOrDefault(user.Id),
        user.OnboardingCompletedAt is not null,
        user.TwoFactorMethod.ToString().ToLowerInvariant(),
        links.EmailChange.GetValueOrDefault(user.Id) is { } pending ? MaskEmail(pending) : null);

    /// <summary>"ana.souza@exemplo.com" becomes "a***a@exemplo.com"; the domain stays readable.</summary>
    internal static string MaskEmail(string email)
    {
        var at = email.LastIndexOf('@');
        if (at <= 0)
        {
            return "***";
        }

        var local = email[..at];
        var shown = local.Length <= 2 ? $"{local[0]}***" : $"{local[0]}***{local[^1]}";
        return shown + email[at..];
    }

    [GeneratedRegex("^[a-z0-9._-]{3,32}$")]
    private static partial Regex UsernamePattern();
}
