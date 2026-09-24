using AudioTranscription.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudioTranscription.Infrastructure.Data.Configurations;

public class AudioJobConfiguration : IEntityTypeConfiguration<AudioJob>
{
    public void Configure(EntityTypeBuilder<AudioJob> builder)
    {
        builder.ToTable("AudioJobs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.FileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.FileSizeBytes)
            .IsRequired();

        builder.Property(x => x.ContentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(x => x.RawTranscript)
            .HasColumnType("nvarchar(max)");

        builder.Property(x => x.ProcessedTranscript)
            .HasColumnType("nvarchar(max)");

        builder.Property(x => x.ErrorMessage)
            .HasColumnType("nvarchar(max)");

        builder.Property(x => x.Language)
            .HasMaxLength(10);

        builder.Property(x => x.DurationSeconds);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired()
            .HasColumnType("datetime2");

        builder.Property(x => x.CompletedAtUtc)
            .HasColumnType("datetime2");

        builder.HasIndex(x => x.CreatedAtUtc)
            .IsDescending();

        builder.HasIndex(x => x.Status);
    }
}
