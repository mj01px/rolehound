using Microsoft.EntityFrameworkCore;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Accounts.Admin;
using Rolehound.Api.Infrastructure.Email;
using Rolehound.Api.Infrastructure.Http;
using Rolehound.Api.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddHttpDefaults(builder.Configuration);
builder.Services.AddEmail(builder.Configuration, builder.Environment);
builder.Services.AddAccounts(builder.Environment);

var app = builder.Build();

app.UseHttpDefaults();
app.MapHealthEndpoints();
app.MapAccountsEndpoints();

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
}

// Exposed so integration tests can host the API with WebApplicationFactory.
public partial class Program;
