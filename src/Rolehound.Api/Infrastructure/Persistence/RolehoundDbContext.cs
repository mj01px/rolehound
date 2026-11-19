using Microsoft.EntityFrameworkCore;

namespace Rolehound.Api.Infrastructure.Persistence;

/// <summary>
/// The single database context. Each feature module adds its entities and
/// configures them in its own <c>IEntityTypeConfiguration</c> classes, which
/// are picked up from this assembly.
/// </summary>
public sealed class RolehoundDbContext(DbContextOptions<RolehoundDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RolehoundDbContext).Assembly);
    }
}
