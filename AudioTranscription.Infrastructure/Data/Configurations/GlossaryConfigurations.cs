using AudioTranscription.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudioTranscription.Infrastructure.Data.Configurations;

public class GlossaryConfiguration : IEntityTypeConfiguration<Glossary>
{
    public void Configure(EntityTypeBuilder<Glossary> builder)
    {
        builder.ToTable("Glossaries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OwnerId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);

        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Terms).WithOne().HasForeignKey(x => x.GlossaryId).OnDelete(DeleteBehavior.Cascade);

        // Case-insensitive collation: "Alpha" and "alpha" are the same name
        builder.HasIndex(x => new { x.OwnerId, x.Name }).IsUnique();
    }
}

public class GlossaryTermConfiguration : IEntityTypeConfiguration<GlossaryTerm>
{
    public void Configure(EntityTypeBuilder<GlossaryTerm> builder)
    {
        builder.ToTable("GlossaryTerms");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Term).IsRequired().HasMaxLength(100);
        builder.HasIndex(x => new { x.GlossaryId, x.Position });
    }
}
