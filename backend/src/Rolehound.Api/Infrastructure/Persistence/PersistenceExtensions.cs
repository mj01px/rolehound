using Microsoft.EntityFrameworkCore;

namespace Rolehound.Api.Infrastructure.Persistence;

internal static class PersistenceExtensions
{
    private const string ConnectionStringName = "Rolehound";

    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is missing. Set ConnectionStrings__{ConnectionStringName}.");

        services.AddDbContext<RolehoundDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        return services;
    }
}
