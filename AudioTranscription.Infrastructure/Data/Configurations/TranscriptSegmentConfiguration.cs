using AudioTranscription.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudioTranscription.Infrastructure.Data.Configurations;

public class TranscriptSegmentConfiguration : IEntityTypeConfiguration<TranscriptSegment>
{
    public void Configure(EntityTypeBuilder<TranscriptSegment> builder)
    {
        builder.ToTable("TranscriptSegments");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Text)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        // Segments belong to their job and are removed by the database together with it
        builder.HasOne<AudioJob>()
            .WithMany()
            .HasForeignKey(x => x.AudioJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.AudioJobId, x.Index });
    }
}
