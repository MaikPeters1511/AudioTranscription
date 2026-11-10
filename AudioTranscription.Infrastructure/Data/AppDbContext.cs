using AudioTranscription.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<AudioJob> AudioJobs => Set<AudioJob>();
    public DbSet<TranscriptSegment> TranscriptSegments => Set<TranscriptSegment>();
    public DbSet<SegmentEdit> SegmentEdits => Set<SegmentEdit>();
    public DbSet<UploadSession> UploadSessions => Set<UploadSession>();
    public DbSet<TranscriptVariant> TranscriptVariants => Set<TranscriptVariant>();
    public DbSet<JobSpeaker> JobSpeakers => Set<JobSpeaker>();
    public DbSet<JobShare> JobShares => Set<JobShare>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<JobTag> JobTags => Set<JobTag>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<JobFavorite> JobFavorites => Set<JobFavorite>();
    public DbSet<Glossary> Glossaries => Set<Glossary>();
    public DbSet<GlossaryTerm> GlossaryTerms => Set<GlossaryTerm>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity tables (AspNetUsers, AspNetRoles, ...)
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
