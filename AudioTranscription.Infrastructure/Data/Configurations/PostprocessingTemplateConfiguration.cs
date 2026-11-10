using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudioTranscription.Infrastructure.Data.Configurations;

public class PostprocessingTemplateConfiguration : IEntityTypeConfiguration<PostprocessingTemplate>
{
    public void Configure(EntityTypeBuilder<PostprocessingTemplate> builder)
    {
        builder.ToTable("PostprocessingTemplates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).IsRequired().HasMaxLength(TemplatePromptRules.MaxNameLength);
        builder.Property(x => x.Description).IsRequired().HasMaxLength(TemplatePromptRules.MaxDescriptionLength);
        builder.Property(x => x.Prompt).IsRequired().HasColumnType("nvarchar(max)");
        builder.Property(x => x.OutputSchema).IsRequired().HasColumnType("nvarchar(max)");
        builder.HasIndex(x => x.Name).IsUnique();

        // The templates every installation ships with
        builder.HasData(BuiltInTemplates.All);
    }
}
