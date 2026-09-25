using System.Net;
using System.Text.Json;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudioTranscription.Tests.Integration;

/// <summary>EN-3: the OpenAPI document (AddOpenApi/MapOpenApi) describes the mapped API.</summary>
public class OpenApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OpenApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var dbDescriptors = services.Where(d =>
                d.ServiceType == typeof(AppDbContext) ||
                d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                (d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext))))
                .ToList();
            foreach (var d in dbDescriptors)
                services.Remove(d);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));

            services.AddTestAuthentication();
        }));
    }

    private async Task<JsonDocument> GetDocumentAsync()
    {
        var response = await _factory.CreateClient().GetAsync("/openapi/v1.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetOpenApiDocument_ListsTheMappedApiEndpoints()
    {
        using var document = await GetDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/audio-jobs", out _).Should().BeTrue();
        paths.TryGetProperty("/api/search", out _).Should().BeTrue();
        paths.TryGetProperty("/api/transcription-options", out _).Should().BeTrue();
        paths.TryGetProperty("/api/auth/me", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/audio-jobs", "post")]
    [InlineData("/api/audio-jobs", "get")]
    [InlineData("/api/search", "get")]
    [InlineData("/api/transcription-options", "get")]
    [InlineData("/api/auth/me", "get")]
    [InlineData("/api/auth/logout", "post")]
    public async Task GetOpenApiDocument_KnownOperationsHaveANonEmptySummary(string path, string method)
    {
        using var document = await GetDocumentAsync();

        var operation = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);

        operation.TryGetProperty("summary", out var summary).Should().BeTrue();
        summary.GetString().Should().NotBeNullOrWhiteSpace();
    }
}
