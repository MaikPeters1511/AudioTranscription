using AudioTranscription.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudioTranscription.Infrastructure.Data.Configurations;

public class JobShareConfiguration : IEntityTypeConfiguration<JobShare>
{
    public void Configure(EntityTypeBuilder<JobShare> builder)
    {
        builder.ToTable("JobShares");

        builder.HasKey(x => new { x.AudioJobId, x.UserId });

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasMaxLength(450); // same length as AspNetUsers.Id

        builder.Property(x => x.SharedAtUtc)
            .IsRequired()
            .HasColumnType("datetime2");

        // A share ends together with its job or its user
        builder.HasOne<AudioJob>()
            .WithMany()
            .HasForeignKey(x => x.AudioJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<IdentityUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.UserId);
    }
}
