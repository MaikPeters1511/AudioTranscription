using AudioTranscription.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudioTranscription.Infrastructure.Data.Configurations;

public class EmbeddingIndexStateConfiguration : IEntityTypeConfiguration<EmbeddingIndexState>
{
    public void Configure(EntityTypeBuilder<EmbeddingIndexState> builder)
    {
        builder.ToTable("EmbeddingIndexStates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Provider).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Model).IsRequired().HasMaxLength(200);
    }
}
