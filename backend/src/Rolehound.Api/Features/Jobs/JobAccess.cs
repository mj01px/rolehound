using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Features.Jobs;

/// <summary>
/// Which job a person may use in the AI features (pitch, ATS, tailored resume):
/// one that matches their profile, that they added, or that is in their funnel.
/// One rule, so those features cannot drift apart.
/// </summary>
internal static class JobAccess
{
    public static IQueryable<UserJob> Usable(RolehoundDbContext db, Guid userId, Guid jobId) =>
        db.UserJobs.Where(u => u.UserId == userId && u.JobId == jobId
            && (u.IsRelevant || u.AddedByUser || db.Applications.Any(a => a.UserId == userId && a.JobId == jobId)));
}
