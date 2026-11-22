using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Connectors;

namespace Rolehound.Api.Features.Jobs;

/// <summary>
/// Pasted text for a link without a connector. All fields are what the person
/// pasted; nothing is fetched from that link (FR-JOB-11).
/// </summary>
public sealed record AddJobLinkRequest(string? Url, string? Title, string? Company, string? Location, string? Description);

public sealed record AddJobLinkResponse(Guid Id, bool Fetched);

/// <summary>
/// POST /jobs/links (FR-JOB-10): a link from a source with a connector is read
/// through it; any other link needs the pasted job text and is kept as a
/// reference only. A deleted job comes back and loses its tombstone (FR-JOB-15).
/// The job joins the person's list even if their profile does not match it.
/// </summary>
internal static class JobLinks
{
    public static async Task<Results<Created<AddJobLinkResponse>, Ok<AddJobLinkResponse>, ValidationProblem, ProblemHttpResult>> Add(
        AddJobLinkRequest request,
        ClaimsPrincipal principal,
        RolehoundDbContext db,
        IEnumerable<IJobLinkReader> readers,
        JobScoring scoring,
        TimeProvider time,
        CancellationToken ct)
    {
        var raw = request.Url?.Trim() ?? string.Empty;
        if (raw.Length is 0 or > 1000 || !Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            return Problems.Validation("url", "Cole o link completo da vaga, começando com https://.");
        }

        CollectedJob item;
        JobSource source;
        string? targetKey = null;
        var fetched = false;
        var match = readers.Select(r => (Reader: r, Link: r.Parse(url))).FirstOrDefault(m => m.Link is not null);
        if (match.Link is { } link)
        {
            try
            {
                item = await match.Reader.FetchOneAsync(link, url, ct);
            }
            catch (ConnectorException ex) when (ex.Code is "target_not_found" or "job_closed")
            {
                return Problems.Problem(StatusCodes.Status422UnprocessableEntity, "job_not_found", "Essa vaga não está mais aberta na fonte.");
            }
            catch (ConnectorException)
            {
                return Problems.Problem(StatusCodes.Status502BadGateway, "source_unavailable", "A fonte não respondeu agora. Tente de novo em alguns minutos.");
            }

            source = match.Reader.Source;
            targetKey = link.Target;
            fetched = true;
        }
        else
        {
            var errors = PastedErrors(request);
            if (errors is not null)
            {
                return errors;
            }

            source = JobSource.Link;
            item = new CollectedJob(
                ExternalIdOf(url),
                request.Title!.Trim(),
                request.Company?.Trim() ?? string.Empty,
                request.Location?.Trim() ?? string.Empty,
                request.Description!.Trim(),
                url.GetLeftPart(UriPartial.Query),
                null,
                null);
        }

        var now = time.GetUtcNow();
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.Source == source && j.ExternalId == item.ExternalId, ct);
        var created = job is null;
        if (job is null)
        {
            var target = targetKey is null ? null : await db.SourceTargets.AsNoTracking().FirstOrDefaultAsync(t => t.Source == source && t.Key == targetKey, ct);
            job = new Job
            {
                Source = source,
                ExternalId = item.ExternalId,
                TargetId = target?.Id,
                Title = string.Empty,
                Company = string.Empty,
                Location = string.Empty,
                Description = string.Empty,
                Url = string.Empty,
                FirstSeenAt = now,
                LastSeenAt = now,
                OpenedAt = now,
                Status = SourceStatus.Active,
            };
            JobFields.Apply(job, item, target?.Label);
            db.Jobs.Add(job);
        }
        else if (fetched)
        {
            var label = job.TargetId is { } targetId ? (await db.SourceTargets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == targetId, ct))?.Label : null;
            JobFields.Apply(job, item, label);
            job.LastSeenAt = now;
        }

        if (job.Status == SourceStatus.Closed && fetched)
        {
            job.Status = SourceStatus.Active;
            job.ClosedAt = null;
            job.OpenedAt = now;
        }

        await db.JobTombstones.Where(t => t.Source == source && t.ExternalId == item.ExternalId).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await scoring.ScoreJobsAsync([job], ct);

        var userId = principal.GetUserId();
        var userJob = await db.UserJobs.SingleAsync(u => u.UserId == userId && u.JobId == job.Id, ct);
        userJob.AddedByUser = true;
        userJob.ArchivedAt = null;
        userJob.ArchiveReason = null;
        userJob.ArchiveNote = null;
        userJob.LastInteractionAt = now;
        await db.SaveChangesAsync(ct);

        var response = new AddJobLinkResponse(job.Id, fetched);
        return created ? TypedResults.Created($"/jobs/{job.Id}", response) : TypedResults.Ok(response);
    }

    private static ValidationProblem? PastedErrors(AddJobLinkRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Description))
        {
            return Problems.Validation(
                new Dictionary<string, string[]> { ["description"] = ["Esse site não tem conector. Cole o título e o texto da vaga."] },
                "link_needs_text");
        }

        var errors = new Dictionary<string, string[]>();
        if (request.Title.Trim().Length > 500)
        {
            errors["title"] = ["O título pode ter até 500 caracteres."];
        }

        if (request.Description.Trim().Length > HtmlText.MaxLength)
        {
            errors["description"] = [$"O texto pode ter até {HtmlText.MaxLength:N0} caracteres."];
        }

        if (request.Company?.Trim().Length > 200)
        {
            errors["company"] = ["A empresa pode ter até 200 caracteres."];
        }

        if (request.Location?.Trim().Length > 300)
        {
            errors["location"] = ["O local pode ter até 300 caracteres."];
        }

        return errors.Count == 0 ? null : Problems.Validation(errors);
    }

    /// <summary>Same link, same job: scheme and host lowercased, fragment dropped.</summary>
    internal static string ExternalIdOf(Uri url)
    {
        var normalized = $"{url.Scheme}://{url.Host.ToLowerInvariant()}{url.PathAndQuery}".TrimEnd('/');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
