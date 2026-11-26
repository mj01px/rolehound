using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.TailoredResume;

public sealed record GenerateResumeRequest(string? Language);

public sealed record ResumeGenerationResponse(
    Guid Id,
    Guid JobId,
    string Language,
    string Status,
    string? Error,
    string? Model,
    int DossierVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<ResumeChange>? Changes,
    IReadOnlyList<ResumeCut>? Cuts);

/// <summary>
/// Tailored resume (FR-AI-07 to FR-AI-11): queue a generation, follow its
/// status, review changes and cuts, download the PDF again any time.
/// </summary>
internal static class TailoredResumeEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapTailoredResumeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/jobs/{jobId:guid}/resumes", List).WithTags("Tailored resume");
        endpoints.MapPost("/jobs/{jobId:guid}/resumes", Queue).WithTags("Tailored resume");

        var resumes = endpoints.MapGroup("/resumes").WithTags("Tailored resume");
        resumes.MapGet("/{id:guid}", Get);
        resumes.MapGet("/{id:guid}/pdf", Pdf);
        return endpoints;
    }

    private static async Task<Results<Ok<List<ResumeGenerationResponse>>, ProblemHttpResult>> List(
        Guid jobId, ClaimsPrincipal principal, RolehoundDbContext db, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var generations = await db.ResumeGenerations.AsNoTracking()
            .Where(g => g.UserId == userId && g.JobId == jobId)
            .OrderByDescending(g => g.CreatedAt)
            .ToListAsync(ct);
        return TypedResults.Ok(generations.Select(g => ToResponse(g, withReview: false)).ToList());
    }

    private static async Task<Results<Accepted<ResumeGenerationResponse>, ValidationProblem, ProblemHttpResult>> Queue(
        Guid jobId, GenerateResumeRequest request, ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        var language = request.Language ?? "pt";
        if (language is not ("pt" or "en"))
        {
            return Problems.Validation("language", "Use pt ou en.");
        }

        var userId = principal.GetUserId();
        var visible = await JobAccess.Usable(db, userId, jobId).AnyAsync(ct);
        if (!visible)
        {
            return Problems.NotFound("Vaga não encontrada.");
        }

        if (!await db.DossierVersions.AnyAsync(d => d.UserId == userId, ct))
        {
            return Problems.Conflict("dossier_missing", "Importe seu Dossier primeiro.");
        }

        // FR-AI-11: one generation at a time per person.
        if (await db.ResumeGenerations.AnyAsync(g => g.UserId == userId && (g.Status == GenerationStatus.Queued || g.Status == GenerationStatus.Running), ct))
        {
            return Problems.Conflict("generation_in_progress", "Já tem um currículo sendo gerado. Espere ele terminar.");
        }

        var generation = new ResumeGeneration { UserId = userId, JobId = jobId, Language = language, Status = GenerationStatus.Queued, CreatedAt = time.GetUtcNow() };
        db.ResumeGenerations.Add(generation);
        await db.SaveChangesAsync(ct);
        return TypedResults.Accepted($"/resumes/{generation.Id}", ToResponse(generation, withReview: false));
    }

    private static async Task<Results<Ok<ResumeGenerationResponse>, ProblemHttpResult>> Get(
        Guid id, ClaimsPrincipal principal, RolehoundDbContext db, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var generation = await db.ResumeGenerations.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id && g.UserId == userId, ct);
        return generation is null ? Problems.NotFound("Currículo não encontrado.") : TypedResults.Ok(ToResponse(generation, withReview: true));
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> Pdf(
        Guid id, ClaimsPrincipal principal, RolehoundDbContext db, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var generation = await db.ResumeGenerations.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id && g.UserId == userId, ct);
        if (generation?.Document is null)
        {
            return Problems.NotFound("Currículo não encontrado.");
        }

        var document = JsonSerializer.Deserialize<ResumeDocument>(generation.Document, Json)!;

        // BR-AI-05: contact details come from the Dossier version the resume was built from.
        var dossier = await db.DossierVersions.AsNoTracking()
            .Where(d => d.UserId == userId && d.Number == generation.DossierVersion)
            .Select(d => d.Content)
            .SingleOrDefaultAsync(ct);
        var contact = ContactOf(dossier);

        var bytes = ResumePdf.Render(document, contact);
        var name = $"{Slug(document.Name)}-{(document.Language == "pt" ? "curriculo" : "resume")}.pdf";
        return TypedResults.File(bytes, "application/pdf", name);
    }

    internal static ResumeContact ContactOf(string? dossierJson)
    {
        var contact = dossierJson is null ? null : JsonNode.Parse(dossierJson)?["person"]?["contact"];
        string? Field(string name) => contact?[name] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;
        return new ResumeContact(Field("email"), Field("phone"), Field("linkedin"), Field("github"), Field("website"));
    }

    private static string Slug(string name)
    {
        var slug = new string(name.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => char.IsAsciiLetterOrDigit(c) || c == ' ')
            .ToArray()).Trim().Replace(' ', '-');
        return slug.Length == 0 ? "curriculo" : slug;
    }

    private static ResumeGenerationResponse ToResponse(ResumeGeneration g, bool withReview) => new(
        g.Id,
        g.JobId,
        g.Language,
        g.Status.ToString().ToLowerInvariant(),
        g.Error,
        g.Model,
        g.DossierVersion,
        g.CreatedAt,
        g.FinishedAt,
        withReview && g.Changes is not null ? JsonSerializer.Deserialize<List<ResumeChange>>(g.Changes, Json) : null,
        withReview && g.Cuts is not null ? JsonSerializer.Deserialize<List<ResumeCut>>(g.Cuts, Json) : null);
}
