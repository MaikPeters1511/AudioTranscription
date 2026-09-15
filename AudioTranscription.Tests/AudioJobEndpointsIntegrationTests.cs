using System.Net;
using System.Net.Http.Json;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudioTranscription.Tests.Integration;

public class AudioJobEndpointsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AudioJobEndpointsIntegrationTests(WebApplicationFactory<Program> factory)
    {
        // Override DbContext to use InMemory database
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptors = services.Where(d => 
                    d.ServiceType == typeof(AppDbContext) || 
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    (d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext)))
                ).ToList();

                foreach (var d in descriptors)
                {
                    services.Remove(d);
                }

                services.AddDbContext<AppDbContext>(options =>
                {
                    options.UseInMemoryDatabase("IntegrationTestsDb");
                });
            });
        });
    }

    [Fact]
    public async Task GetAudioJobs_ReturnsPagedList()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        // Seed some data
        db.Database.EnsureCreated();
        db.AudioJobs.RemoveRange(db.AudioJobs);
        db.AudioJobs.Add(new AudioJob
        {
            Id = Guid.NewGuid(),
            FileName = "test.mp3",
            FileSizeBytes = 1024,
            ContentType = "audio/mpeg",
            Status = AudioJobStatus.Completed,
            TranscriptText = "Hello world",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/audio-jobs?page=1&pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<PaginatedResult<AudioJobListDto>>();
        result.Should().NotBeNull();
        result!.TotalCount.Should().Be(1);
        result.Items.Should().HaveCount(1);
        result.Items.First().FileName.Should().Be("test.mp3");
    }
}
