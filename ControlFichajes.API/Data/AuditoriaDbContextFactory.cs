using Microsoft.EntityFrameworkCore;

namespace ControlFichajes.API.Data;

public sealed class AuditoriaDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;

    public AuditoriaDbContextFactory(DbContextOptions<AppDbContext> options)
    {
        _options = options;
    }

    public AppDbContext CreateDbContext() => new(_options);
}
