using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Accuracy;
using Rolehound.Api.Features.Ai;
using Rolehound.Api.Features.Ats;
using Rolehound.Api.Features.Collection;
using Rolehound.Api.Features.Dossier;
using Rolehound.Api.Features.Funnel;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Features.Pitch;
using Rolehound.Api.Features.TailoredResume;
using Rolehound.Api.Features.Telegram;
using Rolehound.Api.Features.ScoreProfiles;

namespace Rolehound.Api.Infrastructure.Persistence;

/// <summary>
/// The single database context. Each feature module adds its entities and
/// configures them in its own <c>IEntityTypeConfiguration</c> classes, which
/// are picked up from this assembly. Identity tables come from
/// <see cref="IdentityUserContext{TUser, TKey}"/> (users, claims, logins and
/// tokens; roles are a column on the user).
/// </summary>
public sealed class RolehoundDbContext(DbContextOptions<RolehoundDbContext> options)
    : IdentityUserContext<User, Guid>(options), IDataProtectionKeyContext
{
    /// <summary>ASP.NET Data Protection key ring, wrapped by a certificate outside the database (see DataProtectionExtensions).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<AccountToken> AccountTokens => Set<AccountToken>();

    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();

    public DbSet<AiSettings> AiSettings => Set<AiSettings>();

    public DbSet<AiUsage> AiUsage => Set<AiUsage>();

    public DbSet<DossierVersion> DossierVersions => Set<DossierVersion>();

    public DbSet<UserScoreProfile> ScoreProfiles => Set<UserScoreProfile>();

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<JobTombstone> JobTombstones => Set<JobTombstone>();

    public DbSet<UserJob> UserJobs => Set<UserJob>();

    public DbSet<SourceTarget> SourceTargets => Set<SourceTarget>();

    public DbSet<CollectionRun> CollectionRuns => Set<CollectionRun>();

    public DbSet<Application> Applications => Set<Application>();

    public DbSet<Interaction> Interactions => Set<Interaction>();

    public DbSet<PitchVersion> PitchVersions => Set<PitchVersion>();

    public DbSet<ResumeText> ResumeTexts => Set<ResumeText>();

    public DbSet<AtsReport> AtsReports => Set<AtsReport>();

    public DbSet<ResumeGeneration> ResumeGenerations => Set<ResumeGeneration>();

    public DbSet<SampleItem> SampleItems => Set<SampleItem>();

    public DbSet<TelegramLink> TelegramLinks => Set<TelegramLink>();

    public DbSet<TelegramLinkCode> TelegramLinkCodes => Set<TelegramLinkCode>();

    public DbSet<SuggestedModelsRow> SuggestedModels => Set<SuggestedModelsRow>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.ApplyConfigurationsFromAssembly(typeof(RolehoundDbContext).Assembly);
    }
}
