using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Accounts.Admin;
using Rolehound.Api.Features.Ai;
using Rolehound.Api.Features.Collection;
using Rolehound.Api.Features.Jobs;
using Rolehound.Api.Features.Dossier;
using Rolehound.Api.Features.Maintenance;
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
app.MapJobEndpoints();
app.MapTaskEndpoints(app.Configuration);

await PrepareDatabaseAsync(app);

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
