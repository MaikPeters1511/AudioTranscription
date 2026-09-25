using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace AudioTranscription.Tests.Data;

/// <summary>
/// Starts one SQL Server container per test class (requires a running Docker daemon).
/// Every test gets its own database on that server.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public string CreateConnectionString()
    {
        var builder = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "test_" + Guid.NewGuid().ToString("N")
        };
        return builder.ConnectionString;
    }
}
