using AudioTranscription.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudioTranscription.Infrastructure.Data.Configurations;

public class TranscriptVariantConfiguration : IEntityTypeConfiguration<TranscriptVariant>
{
    public void Configure(EntityTypeBuilder<TranscriptVariant> builder)
    {
        builder.ToTable("TranscriptVariants");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Mode)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(x => x.TargetLanguage)
            .HasMaxLength(10);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(x => x.Text)
            .HasColumnType("nvarchar(max)");

        builder.Property(x => x.ErrorMessage)
            .HasColumnType("nvarchar(max)");

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired()
            .HasColumnType("datetime2");

        builder.Property(x => x.CompletedAtUtc)
            .HasColumnType("datetime2");

        // Belongs to its job and is removed by the database together with it
        builder.HasOne<AudioJob>()
            .WithMany()
            .HasForeignKey(x => x.AudioJobId)
            .OnDelete(DeleteBehavior.Cascade);

        // Not unique: SQL Server treats every NULL TargetLanguage as distinct, so "one variant per
        // (job, mode, language)" is enforced in the endpoint (look up, then update or insert) instead.
        builder.HasIndex(x => new { x.AudioJobId, x.Mode, x.TargetLanguage });
    }
}
