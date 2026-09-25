using AudioTranscription.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<AudioJob> AudioJobs => Set<AudioJob>();
    public DbSet<TranscriptSegment> TranscriptSegments => Set<TranscriptSegment>();
    public DbSet<TranscriptVariant> TranscriptVariants => Set<TranscriptVariant>();
    public DbSet<JobSpeaker> JobSpeakers => Set<JobSpeaker>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity tables (AspNetUsers, AspNetRoles, ...)
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
