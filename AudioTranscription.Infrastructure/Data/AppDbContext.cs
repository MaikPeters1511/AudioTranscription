using AudioTranscription.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AudioJob> AudioJobs => Set<AudioJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
