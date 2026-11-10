using AudioTranscription.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudioTranscription.Infrastructure.Data.Configurations;

public class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OwnerId).IsRequired().HasMaxLength(450); // same length as AspNetUsers.Id
        builder.Property(x => x.Name).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Color).HasMaxLength(7);

        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);

        // The database collation is case-insensitive, so "Budget" and "budget" are the same name
        builder.HasIndex(x => new { x.OwnerId, x.Name }).IsUnique();
    }
}

public class JobTagConfiguration : IEntityTypeConfiguration<JobTag>
{
    public void Configure(EntityTypeBuilder<JobTag> builder)
    {
        builder.ToTable("JobTags");
        builder.HasKey(x => new { x.AudioJobId, x.TagId });

        // An assignment ends together with its job or its tag
        builder.HasOne<AudioJob>().WithMany().HasForeignKey(x => x.AudioJobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TagId);
    }
}

public class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable("Folders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OwnerId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);

        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);

        // SQL Server does not allow a cascading self reference: the application re-parents the subfolders
        // of a deleted folder before it removes it
        builder.HasOne<Folder>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.OwnerId, x.ParentId, x.Name }).IsUnique();
    }
}

public class JobFavoriteConfiguration : IEntityTypeConfiguration<JobFavorite>
{
    public void Configure(EntityTypeBuilder<JobFavorite> builder)
    {
        builder.ToTable("JobFavorites");
        builder.HasKey(x => new { x.AudioJobId, x.UserId });

        builder.Property(x => x.UserId).IsRequired().HasMaxLength(450);

        builder.HasOne<AudioJob>().WithMany().HasForeignKey(x => x.AudioJobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.UserId);
    }
}
