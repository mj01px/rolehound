using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Rolehound.Api.Tests;

/// <summary>
/// Hosts the API in memory. The connection string points nowhere on purpose:
/// tests that need a database will get their own fixture.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Rolehound", "Host=localhost;Port=1;Database=none;Username=none;Password=none");
    }
}
