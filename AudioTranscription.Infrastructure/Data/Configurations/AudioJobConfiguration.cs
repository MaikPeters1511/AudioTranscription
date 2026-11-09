using AudioTranscription.Domain.Entities;
using Microsoft.AspNetCore.Identity;
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

        builder.Property(x => x.ErrorMessage)
            .HasColumnType("nvarchar(max)");

        builder.Property(x => x.Language)
            .HasMaxLength(10);

        builder.Property(x => x.Model)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.RequestedLanguage)
            .HasMaxLength(10);

        builder.Property(x => x.DiarizationRequested)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.DurationSeconds);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired()
            .HasColumnType("datetime2");

        builder.Property(x => x.CompletedAtUtc)
            .HasColumnType("datetime2");

        builder.HasIndex(x => x.CreatedAtUtc)
            .IsDescending();

        builder.HasIndex(x => x.Status);

        // Nullable: jobs from before S19 stay unassigned until an admin gives them an owner
        builder.Property(x => x.OwnerId)
            .HasMaxLength(450); // same length as AspNetUsers.Id

        builder.HasOne<IdentityUser>()
            .WithMany()
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.OwnerId);
    }
}
