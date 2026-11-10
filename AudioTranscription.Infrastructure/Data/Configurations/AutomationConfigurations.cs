using AudioTranscription.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AudioTranscription.Infrastructure.Data.Configurations;

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("ApiKeys");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Prefix).IsRequired().HasMaxLength(8);
        builder.Property(x => x.KeyHash).IsRequired().HasMaxLength(32);
        builder.Property(x => x.Scopes).IsRequired().HasMaxLength(100);

        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.Prefix).IsUnique();
        builder.HasIndex(x => x.OwnerId);
    }
}

public class WebhookConfiguration : IEntityTypeConfiguration<Webhook>
{
    public void Configure(EntityTypeBuilder<Webhook> builder)
    {
        builder.ToTable("Webhooks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OwnerId).IsRequired().HasMaxLength(450);
        builder.Property(x => x.Url).IsRequired().HasMaxLength(2048);
        builder.Property(x => x.ProtectedSecret).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.Events).IsRequired().HasMaxLength(200);

        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<WebhookDelivery>().WithOne().HasForeignKey(x => x.WebhookId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.OwnerId);
    }
}

public class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("WebhookDeliveries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Event).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Payload).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(100);
        builder.Property(x => x.Status).HasConversion<int>();

        // The delivery service looks for due rows; the history lists the newest of one webhook
        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        builder.HasIndex(x => new { x.WebhookId, x.CreatedAtUtc });
    }
}
