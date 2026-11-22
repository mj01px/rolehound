using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Rolehound.Api.Features.Ats;

public sealed record ResumeResponse(string FileName, int Pages, int Characters, DateTimeOffset UploadedAt);

/// <summary>
/// One resume PDF per person, used only for ATS analysis (FR-DOS-08): upload,
/// replace and delete. The file is checked by content, not by name or type.
/// </summary>
internal static class ResumeEndpoints
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int MaxPages = 10;
    public const int MaxTextLength = 60_000;

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    public static IEndpointRouteBuilder MapResumeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var resume = endpoints.MapGroup("/resume").WithTags("Resume");
        resume.MapGet(string.Empty, Get);

        // The session cookie is SameSite=Strict, which is what stops cross-site posts
        // here; the API has no antiforgery tokens.
        resume.MapPut(string.Empty, Upload).DisableAntiforgery();
        resume.MapDelete(string.Empty, Remove);
        return endpoints;
    }

    private static async Task<Results<Ok<ResumeResponse>, ProblemHttpResult>> Get(ClaimsPrincipal principal, RolehoundDbContext db, CancellationToken ct)
    {
        var resume = await db.ResumeTexts.AsNoTracking().SingleOrDefaultAsync(r => r.UserId == principal.GetUserId(), ct);
        return resume is null ? Problems.NotFound("Nenhum currículo enviado.") : TypedResults.Ok(ToResponse(resume));
    }

    private static async Task<Results<Ok<ResumeResponse>, ValidationProblem>> Upload(
        IFormFile? file, ClaimsPrincipal principal, RolehoundDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return Problems.Validation("file", "Escolha um arquivo PDF.");
        }

        if (file.Length > MaxBytes)
        {
            return Problems.Validation("file", "O PDF pode ter até 5 MB.");
        }

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        var (text, pages, error) = Extract(bytes);
        if (error is not null)
        {
            return Problems.Validation("file", error);
        }

        var userId = principal.GetUserId();
        var resume = await db.ResumeTexts.SingleOrDefaultAsync(r => r.UserId == userId, ct);
        if (resume is null)
        {
            resume = new ResumeText { UserId = userId, FileName = string.Empty, Text = string.Empty };
            db.ResumeTexts.Add(resume);
        }

        resume.FileName = CleanName(file.FileName);
        resume.Text = text!;
        resume.Pages = pages;
        resume.UploadedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToResponse(resume));
    }

    private static async Task<NoContent> Remove(ClaimsPrincipal principal, RolehoundDbContext db, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        await db.ResumeTexts.Where(r => r.UserId == userId).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Reads the text of a PDF, refusing anything that is not one or carries no text.</summary>
    internal static (string? Text, int Pages, string? Error) Extract(byte[] bytes)
    {
        if (!bytes.AsSpan().StartsWith(PdfMagic))
        {
            return (null, 0, "Esse arquivo não é um PDF.");
        }

        try
        {
            using var document = PdfDocument.Open(bytes);
            if (document.NumberOfPages > MaxPages)
            {
                return (null, 0, $"O currículo pode ter até {MaxPages} páginas.");
            }

            var text = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                text.AppendLine(ContentOrderTextExtractor.GetText(page));
            }

            var plain = text.ToString().Trim();
            if (plain.Length < 50)
            {
                return (null, 0, "Não encontramos texto nesse PDF. Se ele for uma imagem escaneada, exporte de novo como texto.");
            }

            return (plain.Length > MaxTextLength ? plain[..MaxTextLength] : plain, document.NumberOfPages, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Encrypted, damaged or hostile files all end here.
            return (null, 0, "Não conseguimos ler esse PDF. Se ele tiver senha, exporte uma versão sem senha.");
        }
    }

    private static string CleanName(string name)
    {
        var clean = new string(Path.GetFileName(name).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0)
        {
            return "curriculo.pdf";
        }

        return clean.Length > 255 ? clean[..255] : clean;
    }

    private static ResumeResponse ToResponse(ResumeText resume) => new(resume.FileName, resume.Pages, resume.Text.Length, resume.UploadedAt);
}
