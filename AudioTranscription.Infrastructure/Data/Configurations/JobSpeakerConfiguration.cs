using AudioTranscription.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudioTranscription.Infrastructure.Data.Configurations;

public class JobSpeakerConfiguration : IEntityTypeConfiguration<JobSpeaker>
{
    public void Configure(EntityTypeBuilder<JobSpeaker> builder)
    {
        builder.ToTable("JobSpeakers");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.DisplayName)
            .HasMaxLength(100);

        // Belongs to its job and is removed by the database together with it
        builder.HasOne<AudioJob>()
            .WithMany()
            .HasForeignKey(x => x.AudioJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.AudioJobId, x.Index })
            .IsUnique();
    }
}
