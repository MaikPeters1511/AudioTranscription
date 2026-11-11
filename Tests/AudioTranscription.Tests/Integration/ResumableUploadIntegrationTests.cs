using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Configuration;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudioTranscription.Tests.Integration;

/// <summary>S27 / ADR 0006: resumable uploads over the tus protocol (POST creates, HEAD asks the offset, PATCH appends).</summary>
public class ResumableUploadIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const long MaxFileSize = 1_000;
    private readonly TempDirectory _dir = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ResumableUploadIntegrationTests(WebApplicationFactory<Program> factory)
    {
        var dbName = Guid.NewGuid().ToString();
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var dbDescriptors = services.Where(d =>
                d.ServiceType == typeof(AppDbContext) ||
                d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                (d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext))))
                .ToList();
            foreach (var d in dbDescriptors)
                services.Remove(d);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));

            services.AddTestAuthentication();
            // These tests cover the upload protocol only. A running worker would pick the new job up
            // concurrently (Pending -> Processing, upload file consumed) and make the assertions race.
            foreach (var worker in services.Where(d => d.ImplementationType == typeof(TranscriptionWorker)).ToList())
                services.Remove(worker);
            services.Configure<UploadOptions>(o =>
            {
                o.TempStoragePath = _dir.Path;
                o.MaxFileSizeBytes = MaxFileSize;
                o.IncompleteSessionRetentionHours = 24;
            });
        }));
        _client = _factory.CreateClient();
    }

    // --- happy path and resume -------------------------------------------------

    [Fact]
    public async Task Upload_InTwoChunks_CreatesExactlyOneJob_WithTheAssembledFile()
    {
        var content = Mp3(300);
        var (location, _) = await CreateAsync(content.Length);

        (await PatchAsync(location, 0, content[..120])).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetJobsAsync()).Should().BeEmpty();
        (await PatchAsync(location, 120, content[120..])).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var jobs = await GetJobsAsync();
        var job = jobs.Should().ContainSingle().Subject;
        job.OwnerId.Should().Be(TestAuthHandler.DefaultUserId);
        job.FileName.Should().Be("meeting.mp3");
        job.FileSizeBytes.Should().Be(content.Length);
        job.Status.Should().Be(AudioTranscription.Domain.Enums.AudioJobStatus.Pending);
        File.ReadAllBytes(Directory.GetFiles(_dir.Path, $"{job.Id}.*").Single()).Should().Equal(content);
    }

    [Fact]
    public async Task Upload_CanBeResumedAtTheOffsetTheServerReports()
    {
        var content = Mp3(300);
        var (location, _) = await CreateAsync(content.Length);
        await PatchAsync(location, 0, content[..100]);

        var head = await HeadAsync(location);

        head.StatusCode.Should().Be(HttpStatusCode.OK);
        head.Headers.GetValues("Upload-Offset").Single().Should().Be("100");
        (await PatchAsync(location, 100, content[100..])).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetJobsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task UploadLocation_IsARelativePath_SoItSurvivesAReverseProxy()
    {
        var (_, response) = await CreateAsync(100);

        response.Headers.Location!.IsAbsoluteUri.Should().BeFalse();
        response.Headers.Location.ToString().Should().StartWith("/api/uploads/");
    }

    [Fact]
    public async Task JobEndpoint_ReturnsTheJobIdOfACompletedUpload()
    {
        var content = Mp3(100);
        var (location, _) = await CreateAsync(content.Length);
        await PatchAsync(location, 0, content);

        var response = await _client.GetAsync($"{location}/job");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CreateAudioJobResponse>();
        (await GetJobsAsync()).Single().Id.Should().Be(body!.Id);
    }

    [Fact]
    public async Task JobEndpoint_OfAnIncompleteUpload_Returns409()
    {
        var (location, _) = await CreateAsync(100);

        var response = await _client.GetAsync($"{location}/job");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // --- bad chunks --------------------------------------------------------------

    [Fact]
    public async Task Chunk_WithWrongOffset_IsRejectedWith409_AndNothingIsAppended()
    {
        var content = Mp3(300);
        var (location, _) = await CreateAsync(content.Length);
        await PatchAsync(location, 0, content[..100]);

        var response = await PatchAsync(location, 50, content[50..150]);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await HeadAsync(location)).Headers.GetValues("Upload-Offset").Single().Should().Be("100");
    }

    [Fact]
    public async Task DuplicateChunk_IsRejectedWith409_AndDoesNotCreateASecondJob()
    {
        var content = Mp3(200);
        var (location, _) = await CreateAsync(content.Length);
        await PatchAsync(location, 0, content);

        var again = await PatchAsync(location, 0, content);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetJobsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Chunk_BeyondTheDeclaredLength_IsRejected()
    {
        var (location, _) = await CreateAsync(100);

        var response = await PatchAsync(location, 0, Mp3(150));

        response.IsSuccessStatusCode.Should().BeFalse();
        (await GetJobsAsync()).Should().BeEmpty();
    }

    // --- validation ---------------------------------------------------------------

    [Fact]
    public async Task Creating_AboveTheSizeLimit_IsRejectedWith413()
    {
        var response = await CreateRawAsync(MaxFileSize + 1);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Creating_WithADisallowedContentType_IsRejected()
    {
        var response = await CreateRawAsync(100, fileType: "application/pdf");

        response.IsSuccessStatusCode.Should().BeFalse();
        response.StatusCode.Should().NotBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Creating_WithoutAFileName_IsRejected()
    {
        var response = await CreateRawAsync(100, fileName: null);

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Creating_WithAnUnknownModel_IsRejected()
    {
        var response = await CreateRawAsync(100, extra: ("model", "does-not-exist"));

        response.IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task AssembledFile_WithWrongMagicBytes_CreatesNoJob_AndTheJobEndpointExplainsWhy()
    {
        var content = Enumerable.Repeat((byte)0x41, 100).ToArray();
        var (location, _) = await CreateAsync(content.Length);

        (await PatchAsync(location, 0, content)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetJobsAsync()).Should().BeEmpty();
        var response = await _client.GetAsync($"{location}/job");
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Directory.GetFiles(_dir.Path).Should().BeEmpty();
    }

    // --- ownership -------------------------------------------------------------------

    [Fact]
    public async Task Session_IsBoundToItsUser()
    {
        var content = Mp3(200);
        var (location, _) = await CreateAsync(content.Length);
        await PatchAsync(location, 0, content[..50]);

        var head = await SendAsync(HttpMethod.Head, location, userId: "someone-else");
        var patch = await PatchAsync(location, 50, content[50..], userId: "someone-else");
        var job = await SendAsync(HttpMethod.Get, $"{location}/job", userId: "someone-else");
        var delete = await SendAsync(HttpMethod.Delete, location, userId: "someone-else");

        head.StatusCode.Should().Be(HttpStatusCode.NotFound);
        patch.StatusCode.Should().Be(HttpStatusCode.NotFound);
        job.StatusCode.Should().Be(HttpStatusCode.NotFound);
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await HeadAsync(location)).Headers.GetValues("Upload-Offset").Single().Should().Be("50");
    }

    [Fact]
    public async Task Creating_WithoutAUser_Returns401()
    {
        var response = await CreateRawAsync(100, userId: TestAuthHandler.NoUser);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Termination_DeletesTheSessionAndItsPartialFile()
    {
        var (location, _) = await CreateAsync(200);
        await PatchAsync(location, 0, Mp3(50));

        var delete = await SendAsync(HttpMethod.Delete, location);

        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await HeadAsync(location)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // --- cleanup of incomplete sessions ------------------------------------------------

    [Fact]
    public async Task Cleanup_RemovesIncompleteSessionsOlderThanTheRetention_ButKeepsRecentOnes()
    {
        var (oldLocation, _) = await CreateAsync(200);
        await PatchAsync(oldLocation, 0, Mp3(50));
        var (recentLocation, _) = await CreateAsync(200);
        await PatchAsync(recentLocation, 0, Mp3(50));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var old = await db.UploadSessions.SingleAsync(s => s.Id == IdOf(oldLocation));
            old.CreatedAtUtc = DateTime.UtcNow.AddHours(-25);
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IncompleteUploadCleanupService>().CleanupAsync(CancellationToken.None);

        (await HeadAsync(oldLocation)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await HeadAsync(recentLocation)).Headers.GetValues("Upload-Offset").Single().Should().Be("50");
    }

    // --- the simple upload endpoint stays ---------------------------------------------

    [Fact]
    public async Task TheSimpleMultipartUpload_StillWorks()
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Mp3(50));
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        form.Add(file, "file", "klein.mp3");

        var response = await _client.PostAsync("/api/audio-jobs", form);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    // --- helpers ---------------------------------------------------------------------

    private static byte[] Mp3(int length)
    {
        var bytes = new byte[length];
        bytes[0] = 0x49; bytes[1] = 0x44; bytes[2] = 0x33; // "ID3"
        for (var i = 3; i < length; i++) bytes[i] = (byte)(i % 251);
        return bytes;
    }

    private static string IdOf(string location) => location[(location.LastIndexOf('/') + 1)..];

    private static string Meta(string key, string value) => $"{key} {Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}";

    private Task<HttpResponseMessage> CreateRawAsync(
        long length, string? fileName = "meeting.mp3", string fileType = "audio/mpeg", (string Key, string Value)? extra = null, string? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/uploads");
        request.Headers.Add("Tus-Resumable", "1.0.0");
        request.Headers.Add("Upload-Length", length.ToString());
        var metadata = new List<string> { Meta("filetype", fileType) };
        if (fileName is not null) metadata.Add(Meta("filename", fileName));
        if (extra is { } e) metadata.Add(Meta(e.Key, e.Value));
        request.Headers.Add("Upload-Metadata", string.Join(",", metadata));
        if (userId is not null) request.Headers.Add(TestAuthHandler.UserIdHeader, userId);
        return _client.SendAsync(request);
    }

    private async Task<(string Location, HttpResponseMessage Response)> CreateAsync(long length)
    {
        var response = await CreateRawAsync(length);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var location = response.Headers.Location!.ToString();
        return (location, response);
    }

    private Task<HttpResponseMessage> PatchAsync(string location, long offset, byte[] chunk, string? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, location) { Content = new ByteArrayContent(chunk) };
        request.Headers.Add("Tus-Resumable", "1.0.0");
        request.Headers.Add("Upload-Offset", offset.ToString());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
        if (userId is not null) request.Headers.Add(TestAuthHandler.UserIdHeader, userId);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> HeadAsync(string location) => SendAsync(HttpMethod.Head, location);

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? userId = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Tus-Resumable", "1.0.0");
        if (userId is not null) request.Headers.Add(TestAuthHandler.UserIdHeader, userId);
        return _client.SendAsync(request);
    }

    private async Task<List<AudioJob>> GetJobsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AudioJobs.ToListAsync();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _dir.Dispose();
    }
}
