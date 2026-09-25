using System.Net;
using System.Net.Http.Json;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Tests.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudioTranscription.Tests.Integration;

/// <summary>
/// S13-T3: GET /api/search against a real SQL Server (the FTS index needs actual full-text search,
/// which the InMemory provider used by other endpoint tests cannot run). Requires the docker/mssql-fts
/// image to build; see ADR 0005 for the sandbox limitation this ran into.
/// </summary>
public class SearchIntegrationTests : IClassFixture<SearchSqlServerFixture>, IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly SearchSqlServerFixture _sqlServer;
    private readonly WebApplicationFactory<Program> _baseFactory;
    private WebApplicationFactory<Program> _factory = null!;
    private string _connectionString = null!;

    public SearchIntegrationTests(SearchSqlServerFixture sqlServer, WebApplicationFactory<Program> factory)
    {
        _sqlServer = sqlServer;
        _baseFactory = factory;
    }

    public async Task InitializeAsync()
    {
        _connectionString = _sqlServer.CreateConnectionString();
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_connectionString).Options))
        {
            await db.Database.MigrateWithBaselineAsync(NullLogger.Instance);
        }

        _factory = _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var dbDescriptors = services.Where(d =>
                d.ServiceType == typeof(AppDbContext) ||
                d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                (d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext))))
                .ToList();
            foreach (var d in dbDescriptors)
                services.Remove(d);
            services.AddDbContext<AppDbContext>(o => o.UseSqlServer(_connectionString));

            services.AddTestAuthentication();
        }));
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Guid> SeedJobAsync(string fileName, string? rawTranscript, (int Index, string Text)? segment = null, string? variantText = null)
    {
        var jobId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AudioJobs.Add(new AudioJob
        {
            Id = jobId, FileName = fileName, ContentType = "audio/mpeg", Model = "Base",
            Status = AudioJobStatus.Completed, RawTranscript = rawTranscript, CreatedAtUtc = DateTime.UtcNow,
        });
        if (segment is { } s)
        {
            db.TranscriptSegments.Add(new TranscriptSegment
            {
                AudioJobId = jobId, Index = s.Index, StartMs = s.Index * 1000, EndMs = s.Index * 1000 + 900, Text = s.Text,
            });
        }
        if (variantText is not null)
        {
            db.TranscriptVariants.Add(new TranscriptVariant
            {
                AudioJobId = jobId, Mode = PostProcessingMode.Cleanup, Status = VariantStatus.Completed, Text = variantText,
            });
        }
        await db.SaveChangesAsync();
        await WaitForFullTextPopulationAsync(db, "AudioJobs");
        await WaitForFullTextPopulationAsync(db, "TranscriptSegments");
        await WaitForFullTextPopulationAsync(db, "TranscriptVariants");
        return jobId;
    }

    private static async Task WaitForFullTextPopulationAsync(AppDbContext db, string tableName)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var status = await db.Database.SqlQuery<int>(
                $"SELECT OBJECTPROPERTYEX(OBJECT_ID({tableName}), 'TableFulltextPopulateStatus') AS Value")
                .SingleAsync();
            if (status == 0)
                return;
            await Task.Delay(200);
        }
        throw new TimeoutException($"Full-text population on {tableName} did not finish in time.");
    }

    [Fact]
    public async Task Search_FindsAHitInTheRawTranscript()
    {
        var jobId = await SeedJobAsync("meeting.mp3", "Der Zauberer wanderte durch den dunklen Wald.");

        var result = await _factory.CreateClient().GetFromJsonAsync<PaginatedResult<SearchHitDto>>("/api/search?q=Zauberer");

        result!.Items.Should().ContainSingle();
        var hit = result.Items[0];
        hit.JobId.Should().Be(jobId);
        hit.FileName.Should().Be("meeting.mp3");
        hit.Snippet.Should().Contain("Zauberer");
        hit.SegmentStartMs.Should().BeNull();
    }

    [Fact]
    public async Task Search_FindsAHitInASegment_AndReturnsItsStartMs()
    {
        var jobId = await SeedJobAsync("interview.mp3", "Einleitung.", segment: (1, "Ein Kobold lauerte hinter dem Baum."));

        var result = await _factory.CreateClient().GetFromJsonAsync<PaginatedResult<SearchHitDto>>("/api/search?q=Kobold");

        var hit = result!.Items.Should().ContainSingle().Which;
        hit.JobId.Should().Be(jobId);
        hit.SegmentStartMs.Should().Be(1000);
    }

    [Fact]
    public async Task Search_FindsAHitInAVariant()
    {
        var jobId = await SeedJobAsync("call.mp3", "Rohtext ohne Drachen.", variantText: "Zusammenfassung: Drachen bewachen die Höhle.");

        var result = await _factory.CreateClient().GetFromJsonAsync<PaginatedResult<SearchHitDto>>("/api/search?q=Höhle");

        var hit = result!.Items.Should().ContainSingle().Which;
        hit.JobId.Should().Be(jobId);
        hit.SegmentStartMs.Should().BeNull();
    }

    [Fact]
    public async Task Search_WithNoMatch_ReturnsAnEmptyPage()
    {
        await SeedJobAsync("meeting.mp3", "Nichts Interessantes hier.");

        var result = await _factory.CreateClient().GetFromJsonAsync<PaginatedResult<SearchHitDto>>("/api/search?q=Dinosaurier");

        result!.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Search_SupportsGermanStemming()
    {
        await SeedJobAsync("meeting.mp3", "Die Kinder spielten fröhlich im Garten.");

        var result = await _factory.CreateClient().GetFromJsonAsync<PaginatedResult<SearchHitDto>>("/api/search?q=spielen");

        result!.Items.Should().ContainSingle();
    }

    [Theory]
    [InlineData("\"; DROP TABLE AudioJobs; --")]
    [InlineData("AND OR NOT NEAR \"quoted\" * FORMSOF")]
    [InlineData("Zauberer\" OR \"1")]
    public async Task Search_WithFtsSpecialCharacters_DoesNotThrowAndTreatsThemAsPlainText(string term)
    {
        await SeedJobAsync("meeting.mp3", "Ein harmloser Satz ohne besondere Zeichen.");

        var response = await _factory.CreateClient().GetAsync($"/api/search?q={Uri.EscapeDataString(term)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PaginatedResult<SearchHitDto>>();
        result!.Items.Should().BeEmpty("the special characters are plain search words, none of which occur in the seeded text");
    }

    [Fact]
    public async Task Search_WithoutAQuery_Returns400()
    {
        var response = await _factory.CreateClient().GetAsync("/api/search?q=");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Should().ContainKey("q");
    }

    [Fact]
    public async Task Search_Paginates()
    {
        for (var i = 0; i < 5; i++)
            await SeedJobAsync($"meeting{i}.mp3", "Wiederkehrendes Suchwort Bergsteiger in jedem Job.");

        var page1 = await _factory.CreateClient().GetFromJsonAsync<PaginatedResult<SearchHitDto>>("/api/search?q=Bergsteiger&page=1&pageSize=2");
        var page2 = await _factory.CreateClient().GetFromJsonAsync<PaginatedResult<SearchHitDto>>("/api/search?q=Bergsteiger&page=2&pageSize=2");

        page1!.Items.Should().HaveCount(2);
        page1.TotalCount.Should().Be(5);
        page2!.Items.Should().HaveCount(2);
        page1.Items.Select(i => i.JobId).Should().NotIntersectWith(page2.Items.Select(i => i.JobId));
    }
}
