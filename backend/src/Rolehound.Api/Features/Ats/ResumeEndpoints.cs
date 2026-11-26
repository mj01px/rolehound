using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
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

    /// <summary>
    /// The most the compressed streams of one PDF may unpack to. A real resume needs a few
    /// megabytes; a "PDF bomb" (a small file that unpacks to gigabytes) is refused before parsing.
    /// </summary>
    public const long MaxUnpackedBytes = 128 * 1024 * 1024;

    private static readonly TimeSpan ExtractTimeout = TimeSpan.FromSeconds(20);

    // One PDF read at a time in the process: parsing is the costly part, and the API's memory is shared.
    private static readonly SemaphoreSlim ExtractGate = new(1, 1);

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();
    private static readonly byte[] StreamKeyword = "stream"u8.ToArray();
    private static readonly byte[] EndStreamKeyword = "endstream"u8.ToArray();

    public static IEndpointRouteBuilder MapResumeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var resume = endpoints.MapGroup("/resume").WithTags("Resume");
        resume.MapGet(string.Empty, Get);

        // The session cookie is SameSite=Strict, which is what stops cross-site posts
        // here; the API has no antiforgery tokens.
        // The limits stop an oversized body while it arrives, before it is buffered (Kestrel's default is 30 MB).
        resume.MapPut(string.Empty, Upload)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxBytes + (64 * 1024)))
            .WithFormOptions(multipartBodyLengthLimit: MaxBytes + (16 * 1024));
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

        var (text, pages, error) = await ExtractBoundedAsync(bytes, ct);
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

    /// <summary>Runs <see cref="Extract"/> one at a time and within <see cref="ExtractTimeout"/>.</summary>
    private static async Task<(string? Text, int Pages, string? Error)> ExtractBoundedAsync(byte[] bytes, CancellationToken ct)
    {
        const string Busy = "Não deu para ler o PDF agora. Tente de novo em alguns segundos.";
        if (!await ExtractGate.WaitAsync(ExtractTimeout, ct))
        {
            return (null, 0, Busy);
        }

        // The parser cannot be cancelled: the gate is released when it really ends, not when we stop waiting.
        var work = Task.Run(() => Extract(bytes), CancellationToken.None);
        _ = work.ContinueWith(_ => ExtractGate.Release(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        try
        {
            return await work.WaitAsync(ExtractTimeout, ct);
        }
        catch (TimeoutException)
        {
            return (null, 0, "Esse PDF demorou demais para ser lido. Exporte de novo, com menos imagens.");
        }
    }

    /// <summary>
    /// Inflates every stream of the file into a scratch buffer, counting bytes and
    /// keeping none, and stops past <see cref="MaxUnpackedBytes"/>. Streams that are
    /// not zlib data fail at their header and count nothing.
    /// </summary>
    internal static bool UnpacksWithinLimit(byte[] bytes)
    {
        var scratch = new byte[81920];
        long total = 0;
        var span = bytes.AsSpan();
        var at = 0;
        while (true)
        {
            var found = span[at..].IndexOf(StreamKeyword);
            if (found < 0)
            {
                return true;
            }

            var start = at + found + StreamKeyword.Length;
            at = start;
            if (start >= bytes.Length || (bytes[start] != '\r' && bytes[start] != '\n'))
            {
                continue;
            }

            start += bytes[start] == '\r' && start + 1 < bytes.Length && bytes[start + 1] == '\n' ? 2 : 1;
            var end = span[start..].IndexOf(EndStreamKeyword);
            var length = end < 0 ? bytes.Length - start : end;
            try
            {
                using var inflater = new ZLibStream(new MemoryStream(bytes, start, length, writable: false), CompressionMode.Decompress);
                int read;
                while ((read = inflater.Read(scratch, 0, scratch.Length)) > 0)
                {
                    total += read;
                    if (total > MaxUnpackedBytes)
                    {
                        return false;
                    }
                }
            }
            catch (InvalidDataException)
            {
                // Not zlib, or cut short: whatever it is, it unpacked to what was counted.
            }

            at = start + length;
        }
    }

    /// <summary>Reads the text of a PDF, refusing anything that is not one or carries no text.</summary>
    internal static (string? Text, int Pages, string? Error) Extract(byte[] bytes)
    {
        if (!bytes.AsSpan().StartsWith(PdfMagic))
        {
            return (null, 0, "Esse arquivo não é um PDF.");
        }

        if (!UnpacksWithinLimit(bytes))
        {
            return (null, 0, "Esse PDF é pesado demais por dentro. Exporte de novo, com menos imagens.");
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
