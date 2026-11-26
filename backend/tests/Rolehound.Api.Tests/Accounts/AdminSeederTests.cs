using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rolehound.Api.Features.Accounts;
using Rolehound.Api.Features.Accounts.Admin;
using Rolehound.Api.Infrastructure.Persistence;
using Rolehound.Api.Tests.Infrastructure;

namespace Rolehound.Api.Tests.Accounts;

public sealed class AdminSeederTests(ApiFactory factory) : ApiTest(factory)
{
    [Fact]
    public async Task A_first_admin_without_password_or_usable_link_gets_a_new_link_once()
    {
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin, password: null);

        await SeedAsync();
        Assert.Single(Factory.Emails.To("rafael@exemplo.test"));

        // The link just sent is still usable: starting again sends nothing.
        await SeedAsync();
        Assert.Single(Factory.Emails.To("rafael@exemplo.test"));
    }

    [Fact]
    public async Task An_admin_who_set_a_password_gets_nothing()
    {
        await CreateUserAsync("rafael@exemplo.test", UserRole.Admin);

        await SeedAsync();

        Assert.Empty(Factory.Emails.To("rafael@exemplo.test"));
    }

    private async Task SeedAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:Email"] = "rafael@exemplo.test", ["Admin:Username"] = "rafael" })
            .Build();
        var seeder = new AdminSeeder(
            configuration,
            services.GetRequiredService<RolehoundDbContext>(),
            services.GetRequiredService<UserManager<User>>(),
            services.GetRequiredService<AccountTokens>(),
            services.GetRequiredService<AccountEmails>(),
            services.GetRequiredService<TimeProvider>(),
            NullLogger<AdminSeeder>.Instance);
        await seeder.SeedAsync(Ct);
    }
}
