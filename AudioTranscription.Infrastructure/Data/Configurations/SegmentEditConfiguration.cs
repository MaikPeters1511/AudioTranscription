using AudioTranscription.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudioTranscription.Infrastructure.Data.Configurations;

public class SegmentEditConfiguration : IEntityTypeConfiguration<SegmentEdit>
{
    public void Configure(EntityTypeBuilder<SegmentEdit> builder)
    {
        builder.ToTable("SegmentEdits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Text).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(x => x.EditedBy).HasMaxLength(450);

        // The history goes away with its segment
        builder.HasOne<TranscriptSegment>()
            .WithMany()
            .HasForeignKey(x => x.SegmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.SegmentId, x.Version }).IsUnique();
    }
}
