using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace AudioTranscription.Tests.Data;

/// <summary>
/// Like <see cref="SqlServerFixture"/>, but builds the SQL Server image from <c>docker/mssql-fts</c>
/// (the same Dockerfile Aspire and docker-compose use, ADR 0005) instead of the stock image, since
/// Full-Text Search is not included in the stock image. Building this image needs network access to
/// packages.microsoft.com; see S13's Umsetzungsnotizen for the sandbox limitation this ran into.
/// </summary>
public sealed class SearchSqlServerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public async Task InitializeAsync()
    {
        var image = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(CommonDirectoryPath.GetSolutionDirectory(), "docker/mssql-fts")
            .WithDockerfile("Dockerfile")
            .WithDeleteIfExists(false)
            .Build();
        await image.CreateAsync();

        _container = new MsSqlBuilder().WithImage(image).Build();
        await _container.StartAsync();
    }

    public Task DisposeAsync() => _container?.DisposeAsync().AsTask() ?? Task.CompletedTask;

    public string CreateConnectionString()
    {
        var builder = new SqlConnectionStringBuilder(_container!.GetConnectionString())
        {
            InitialCatalog = "test_" + Guid.NewGuid().ToString("N")
        };
        return builder.ConnectionString;
    }
}
