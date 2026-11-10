using AudioTranscription.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudioTranscription.Infrastructure.Data.Configurations;

public class UploadSessionConfiguration : IEntityTypeConfiguration<UploadSession>
{
    public void Configure(EntityTypeBuilder<UploadSession> builder)
    {
        builder.ToTable("UploadSessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.RejectionDetail).HasMaxLength(1000);
        builder.Ignore(x => x.IsClosed);
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}
