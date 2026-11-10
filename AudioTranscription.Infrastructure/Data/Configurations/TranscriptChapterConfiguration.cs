using AudioTranscription.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudioTranscription.Infrastructure.Data.Configurations;

public class TranscriptChapterConfiguration : IEntityTypeConfiguration<TranscriptChapter>
{
    public void Configure(EntityTypeBuilder<TranscriptChapter> builder)
    {
        builder.ToTable("TranscriptChapters");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Summary).IsRequired().HasMaxLength(1000);

        // Chapters belong to their job and are removed by the database together with it
        builder.HasOne<AudioJob>().WithMany().HasForeignKey(x => x.AudioJobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.AudioJobId, x.Position });
    }
}

public class ChapterRunConfiguration : IEntityTypeConfiguration<ChapterRun>
{
    public void Configure(EntityTypeBuilder<ChapterRun> builder)
    {
        builder.ToTable("ChapterRuns");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ErrorMessage).HasMaxLength(500);

        builder.HasOne<AudioJob>().WithMany().HasForeignKey(x => x.AudioJobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.AudioJobId).IsUnique(); // one run per job; a new request overwrites it
    }
}
