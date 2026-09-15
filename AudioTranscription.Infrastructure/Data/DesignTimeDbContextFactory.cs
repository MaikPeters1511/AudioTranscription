using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AudioTranscription.Infrastructure.Data;

/// <summary>
/// Factory for creating AppDbContext at design time (EF Core migrations).
/// Uses a placeholder connection string — actual connection is injected by Aspire at runtime.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=.;Database=AudioTranscription;Trusted_Connection=True;TrustServerCertificate=True;");
        return new AppDbContext(optionsBuilder.Options);
    }
}
