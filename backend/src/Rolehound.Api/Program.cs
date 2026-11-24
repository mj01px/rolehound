using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Accuracy;
using Rolehound.Api.Features.Accounts.Admin;
using Rolehound.Api.Features.Ai;
using Rolehound.Api.Features.Ats;
using Rolehound.Api.Features.Collection;
using Rolehound.Api.Features.Companies;
using Rolehound.Api.Features.Funnel;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Features.Pitch;
using Rolehound.Api.Features.TailoredResume;
using Rolehound.Api.Features.Telegram;
using Rolehound.Api.Features.Dossier;
using Rolehound.Api.Features.Maintenance;
using Rolehound.Api.Features.Notifications;
using Rolehound.Api.Features.Onboarding;
using Rolehound.Api.Features.ScoreProfiles;
using Rolehound.Api.Infrastructure.Email;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Api.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddKeyRing(builder.Configuration, builder.Environment);
builder.Services.AddHttpDefaults(builder.Configuration);
builder.Services.AddEmail(builder.Configuration, builder.Environment);
builder.Services.AddAccounts(builder.Environment);
builder.Services.AddAi(builder.Configuration);
builder.Services.AddCollection(builder.Configuration);
builder.Services.AddTailoredResume(builder.Configuration);
builder.Services.AddTelegram(builder.Configuration);

var app = builder.Build();

app.UseHttpDefaults();
app.UseOnboardingGate();
app.MapHealthEndpoints();
app.MapAccountsEndpoints();
app.MapOnboardingEndpoints();
app.MapAiSettingsEndpoints();
app.MapDossierEndpoints();
app.MapScoreProfileEndpoints();
app.MapSourceEndpoints();
app.MapCompanyEndpoints();
app.MapJobEndpoints();
app.MapFunnelEndpoints();
app.MapPitchEndpoints();
app.MapResumeEndpoints();
app.MapAtsEndpoints();
app.MapTailoredResumeEndpoints();
app.MapAccuracyEndpoints();
app.MapNotificationEndpoints();
app.MapTelegramEndpoints();
app.MapSuggestedModelsEndpoints();
app.MapTaskEndpoints(app.Configuration);

await PrepareDatabaseAsync(app);
await TelegramModule.RegisterWebhookAsync(app.Services, CancellationToken.None);

await app.RunAsync();

// In development the schema follows the code on start. Elsewhere migrations
// run as an explicit deploy step. The first admin is seeded when configured.
static async Task PrepareDatabaseAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    if (app.Configuration.GetValue<bool>("Database:MigrateOnStart"))
    {
        await scope.ServiceProvider.GetRequiredService<RolehoundDbContext>().Database.MigrateAsync();
    }

    await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync(CancellationToken.None);
    await CollectionModule.SeedTargetsAsync(
        scope.ServiceProvider.GetRequiredService<RolehoundDbContext>(), app.Configuration, TimeProvider.System, CancellationToken.None);
}

// Exposed so integration tests can host the API with WebApplicationFactory.
public partial class Program;
