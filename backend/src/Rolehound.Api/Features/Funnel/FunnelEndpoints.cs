using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Funnel;

public sealed record EnterFunnelRequest(string? Status);

public sealed record ApplicationRequest(
    string? Status, int Priority, DateOnly? AppliedOn, string? NextStep, DateOnly? NextStepOn, string? Contact, bool HasReferral, string? Notes);

public sealed record InteractionRequest(DateOnly? Date, string? Title, string? Detail);

public sealed record ApplicationCard(
    Guid Id,
    Guid JobId,
    string Title,
    string Company,
    string Status,
    int Priority,
    DateOnly? AppliedOn,
    string NextStep,
    DateOnly? NextStepOn,
    bool IsOverdue,
    bool JobClosedAtSource,
    DateTimeOffset UpdatedAt);

public sealed record BoardColumn(string Status, IReadOnlyList<ApplicationCard> Items);

public sealed record BoardResponse(IReadOnlyList<BoardColumn> Columns, IReadOnlyList<ApplicationCard> Overdue, IReadOnlyList<ApplicationCard> Closed);

public sealed record InteractionResponse(Guid Id, DateOnly Date, string Title, string Detail, bool IsSystem, DateTimeOffset CreatedAt);

public sealed record ApplicationDetailResponse(
    ApplicationCard Application, string Contact, bool HasReferral, string Notes, string JobUrl, IReadOnlyList<InteractionResponse> Interactions);

/// <summary>
/// The application funnel (FR-FUN), same flow as the previous tool: enter from a
/// job, any status to any status, a timeline the app also writes to, and a board.
/// Every change counts as an interaction with the job for retention (BR-JOB-10).
/// </summary>
internal static class FunnelEndpoints
{
    /// <summary>Funnel dates are calendar days in Brazil, not UTC (BR-FUN-07).</summary>
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    // Timeline entries are stored data shown as is, so they are in the app's language.
    private const string EnteredTitle = "Entrou no funil";

    private static readonly Dictionary<ApplicationStatus, string> StatusLabels = new()
    {
        [ApplicationStatus.Interest] = "Tenho interesse",
        [ApplicationStatus.Applied] = "Aplicada",
        [ApplicationStatus.Screening] = "Em triagem",
        [ApplicationStatus.Challenge] = "Teste ou desafio",
        [ApplicationStatus.Interview] = "Entrevista",
        [ApplicationStatus.Offer] = "Proposta",
        [ApplicationStatus.Rejected] = "Rejeitada",
        [ApplicationStatus.Withdrawn] = "Desisti",
    };

    public static IEndpointRouteBuilder MapFunnelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/jobs/{jobId:guid}/application", Enter).WithTags("Funnel");

        var applications = endpoints.MapGroup("/applications").WithTags("Funnel");
        applications.MapGet(string.Empty, Board);
        applications.MapGet("/{id:guid}", Detail);
        applications.MapPut("/{id:guid}", Update);
        applications.MapDelete("/{id:guid}", Remove);
        applications.MapPost("/{id:guid}/interactions", AddInteraction);
        applications.MapPut("/{id:guid}/interactions/{interactionId:guid}", UpdateInteraction);
        applications.MapDelete("/{id:guid}/interactions/{interactionId:guid}", RemoveInteraction);
        return endpoints;
    }

    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone).DateTime);

    private static async Task<Results<Created<ApplicationCard>, ValidationProblem, ProblemHttpResult>> Enter(
        Guid jobId, EnterFunnelRequest request, ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var status = ApplicationStatus.Applied;
        if (request.Status is not null && !TryStatus(request.Status, out status))
        {
            return Problems.Validation("status", "Status inválido.");
        }

        var userId = principal.GetUserId();
        var userJob = await db.UserJobs.SingleOrDefaultAsync(u => u.UserId == userId && u.JobId == jobId, ct);
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == jobId, ct);
        if (userJob is null || job is null || !(userJob.IsRelevant || userJob.AddedByUser))
        {
            return Problems.NotFound("Vaga não encontrada.");
        }

        if (await db.Applications.AnyAsync(a => a.UserId == userId && a.JobId == jobId, ct))
        {
            return Problems.Conflict("application_exists", "Essa vaga já está no seu funil.");
        }

        var now = time.GetUtcNow();
        var today = Today(time);
        var application = new Application
        {
            UserId = userId,
            JobId = jobId,
            Status = status,
            AppliedOn = status == ApplicationStatus.Applied ? today : null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Applications.Add(application);
        db.Interactions.Add(new Interaction { ApplicationId = application.Id, Date = today, Title = EnteredTitle, IsSystem = true, CreatedAt = now, UpdatedAt = now });

        // BR-FUN-03: the job leaves the list views; an archive no longer applies.
        userJob.ArchivedAt = null;
        userJob.ArchiveReason = null;
        userJob.ArchiveNote = null;
        userJob.LastInteractionAt = now;
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/applications/{application.Id}", ToCard(application, job, today));
    }

    private static async Task<Ok<BoardResponse>> Board(ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var today = Today(time);
        var rows = await db.Applications.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Join(db.Jobs.AsNoTracking(), a => a.JobId, j => j.Id, (a, j) => new { Application = a, Job = j })
            .ToListAsync(ct);

        var cards = rows.Select(r => ToCard(r.Application, r.Job, today)).ToList();
        var active = Enum.GetValues<ApplicationStatus>().Where(s => s is not (ApplicationStatus.Rejected or ApplicationStatus.Withdrawn));
        var columns = active
            .Select(status => new BoardColumn(
                Lower(status),
                cards.Where(c => c.Status == Lower(status)).OrderBy(c => c.Priority).ThenByDescending(c => c.UpdatedAt).ToList()))
            .ToList();

        var overdue = cards.Where(c => c.IsOverdue).OrderBy(c => c.NextStepOn).ThenBy(c => c.Priority).ToList();
        var closed = cards.Where(c => c.Status is "rejected" or "withdrawn").OrderByDescending(c => c.UpdatedAt).ToList();
        return TypedResults.Ok(new BoardResponse(columns, overdue, closed));
    }

    private static async Task<Results<Ok<ApplicationDetailResponse>, ProblemHttpResult>> Detail(
        Guid id, ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var row = await db.Applications.AsNoTracking()
            .Where(a => a.Id == id && a.UserId == userId)
            .Join(db.Jobs.AsNoTracking(), a => a.JobId, j => j.Id, (a, j) => new { Application = a, Job = j })
            .SingleOrDefaultAsync(ct);
        if (row is null)
        {
            return Problems.NotFound("Candidatura não encontrada.");
        }

        var interactions = await db.Interactions.AsNoTracking()
            .Where(i => i.ApplicationId == id)
            .OrderByDescending(i => i.Date).ThenByDescending(i => i.CreatedAt)
            .Select(i => new InteractionResponse(i.Id, i.Date, i.Title, i.Detail, i.IsSystem, i.CreatedAt))
            .ToListAsync(ct);

        var a = row.Application;
        return TypedResults.Ok(new ApplicationDetailResponse(ToCard(a, row.Job, Today(time)), a.Contact, a.HasReferral, a.Notes, row.Job.Url, interactions));
    }

    private static async Task<Results<Ok<ApplicationCard>, ValidationProblem, ProblemHttpResult>> Update(
        Guid id, ApplicationRequest request, ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (!TryStatus(request.Status, out var status))
        {
            errors["status"] = ["Status inválido."];
        }

        if (request.Priority is < 1 or > 5)
        {
            errors["priority"] = ["A prioridade vai de 1 a 5."];
        }

        Limit(errors, "nextStep", request.NextStep, 300, "O próximo passo");
        Limit(errors, "contact", request.Contact, 200, "O contato");
        Limit(errors, "notes", request.Notes, 5000, "As notas");
        if (errors.Count > 0)
        {
            return Problems.Validation(errors);
        }

        var userId = principal.GetUserId();
        var application = await db.Applications.SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
        if (application is null)
        {
            return Problems.NotFound("Candidatura não encontrada.");
        }

        var now = time.GetUtcNow();
        var today = Today(time);
        var previous = application.Status;
        application.Status = status;
        application.Priority = request.Priority;
        application.AppliedOn = request.AppliedOn;
        application.NextStep = request.NextStep?.Trim() ?? string.Empty;
        application.NextStepOn = request.NextStepOn;
        application.Contact = request.Contact?.Trim() ?? string.Empty;
        application.HasReferral = request.HasReferral;
        application.Notes = request.Notes?.Trim() ?? string.Empty;
        if (status == ApplicationStatus.Applied && application.AppliedOn is null)
        {
            application.AppliedOn = today;
        }

        application.UpdatedAt = now;
        if (previous != status)
        {
            db.Interactions.Add(new Interaction
            {
                ApplicationId = id,
                Date = today,
                Title = $"{StatusLabels[previous]} → {StatusLabels[status]}",
                IsSystem = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await TouchAsync(db, application, now, ct);
        await db.SaveChangesAsync(ct);
        var job = await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == application.JobId, ct);
        return TypedResults.Ok(ToCard(application, job, today));
    }

    // Leaving the funnel deletes the timeline too; the job returns to the list views.
    private static async Task<Results<NoContent, ProblemHttpResult>> Remove(
        Guid id, ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var application = await db.Applications.SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
        if (application is null)
        {
            return Problems.NotFound("Candidatura não encontrada.");
        }

        await TouchAsync(db, application, time.GetUtcNow(), ct);
        db.Applications.Remove(application);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<InteractionResponse>, ValidationProblem, ProblemHttpResult>> AddInteraction(
        Guid id, InteractionRequest request, ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (InteractionErrors(request) is { } errors)
        {
            return errors;
        }

        var application = await OwnAsync(db, principal, id, ct);
        if (application is null)
        {
            return Problems.NotFound("Candidatura não encontrada.");
        }

        var now = time.GetUtcNow();
        var interaction = new Interaction
        {
            ApplicationId = id,
            Date = request.Date!.Value,
            Title = request.Title!.Trim(),
            Detail = request.Detail?.Trim() ?? string.Empty,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Interactions.Add(interaction);
        application.UpdatedAt = now;
        await TouchAsync(db, application, now, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/applications/{id}/interactions/{interaction.Id}", ToResponse(interaction));
    }

    private static async Task<Results<Ok<InteractionResponse>, ValidationProblem, ProblemHttpResult>> UpdateInteraction(
        Guid id, Guid interactionId, InteractionRequest request, ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (InteractionErrors(request) is { } errors)
        {
            return errors;
        }

        var application = await OwnAsync(db, principal, id, ct);
        var interaction = application is null ? null : await db.Interactions.SingleOrDefaultAsync(i => i.Id == interactionId && i.ApplicationId == id, ct);
        if (application is null || interaction is null)
        {
            return Problems.NotFound("Interação não encontrada.");
        }

        var now = time.GetUtcNow();
        interaction.Date = request.Date!.Value;
        interaction.Title = request.Title!.Trim();
        interaction.Detail = request.Detail?.Trim() ?? string.Empty;
        interaction.UpdatedAt = now;
        await TouchAsync(db, application, now, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToResponse(interaction));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveInteraction(
        Guid id, Guid interactionId, ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var application = await OwnAsync(db, principal, id, ct);
        var deleted = application is null ? 0 : await db.Interactions.Where(i => i.Id == interactionId && i.ApplicationId == id).ExecuteDeleteAsync(ct);
        if (application is null || deleted == 0)
        {
            return Problems.NotFound("Interação não encontrada.");
        }

        await TouchAsync(db, application, time.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static Task<Application?> OwnAsync(RolehoundDbContext db, ClaimsPrincipal principal, Guid id, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        return db.Applications.SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct);
    }

    /// <summary>Funnel work counts as interaction with the job (BR-JOB-10).</summary>
    private static async Task TouchAsync(RolehoundDbContext db, Application application, DateTimeOffset now, CancellationToken ct)
    {
        var userJob = await db.UserJobs.SingleOrDefaultAsync(u => u.UserId == application.UserId && u.JobId == application.JobId, ct);
        if (userJob is not null)
        {
            userJob.LastInteractionAt = now;
        }
    }

    private static ValidationProblem? InteractionErrors(InteractionRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Date is null)
        {
            errors["date"] = ["Informe a data."];
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors["title"] = ["Informe o que aconteceu."];
        }

        Limit(errors, "title", request.Title, 200, "O título");
        Limit(errors, "detail", request.Detail, 5000, "O detalhe");
        return errors.Count == 0 ? null : Problems.Validation(errors);
    }

    private static void Limit(Dictionary<string, string[]> errors, string field, string? value, int max, string subject)
    {
        if (value?.Trim().Length > max)
        {
            errors[field] = [$"{subject} pode ter até {max} caracteres."];
        }
    }

    private static bool TryStatus(string? value, out ApplicationStatus status) =>
        Enum.TryParse(value, ignoreCase: true, out status) && Enum.IsDefined(status) && !int.TryParse(value, out _);

    internal static ApplicationCard ToCard(Application a, Job job, DateOnly today) => new(
        a.Id,
        a.JobId,
        job.Title,
        job.Company,
        Lower(a.Status),
        a.Priority,
        a.AppliedOn,
        a.NextStep,
        a.NextStepOn,
        a.IsActive && a.NextStepOn < today,
        job.Status == SourceStatus.Closed,
        a.UpdatedAt);

    private static InteractionResponse ToResponse(Interaction i) => new(i.Id, i.Date, i.Title, i.Detail, i.IsSystem, i.CreatedAt);

    private static string Lower(ApplicationStatus status) => status.ToString().ToLowerInvariant();
}
