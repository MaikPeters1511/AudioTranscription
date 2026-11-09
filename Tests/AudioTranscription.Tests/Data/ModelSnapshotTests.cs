using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Tests.Data;

/// <summary>
/// Guards the migrations: the model snapshot has to match the entity model, otherwise the next
/// "dotnet ef migrations add" would generate a surprising migration. Needs no database connection.
/// </summary>
public class ModelSnapshotTests
{
    [Fact]
    public void ModelSnapshot_MatchesTheEntityModel()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;")
            .Options;
        using var db = new AppDbContext(options);

        db.Database.HasPendingModelChanges().Should().BeFalse(
            "every model change needs a migration (dotnet ef migrations add), and the snapshot has to be up to date");
    }
}
