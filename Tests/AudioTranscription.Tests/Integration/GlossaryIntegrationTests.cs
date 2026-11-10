using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Glossary;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudioTranscription.Tests.Integration;

/// <summary>S22: personal glossaries and their use as the Whisper initial prompt of an upload.</summary>
public class GlossaryIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly WebApplicationFactory<Program> _factory;
    private string _alice = null!;
    private string _bob = null!;

    public GlossaryIntegrationTests(WebApplicationFactory<Program> factory)
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
            services.Configure<AudioTranscription.Api.Configuration.UploadOptions>(o => o.TempStoragePath = _dir.Path);
        }));
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        async Task<string> Create(string email)
        {
            var user = new IdentityUser { UserName = email, Email = email };
            (await users.CreateAsync(user, TestCredentials.NewPassword())).Succeeded.Should().BeTrue();
            return user.Id;
        }
        _alice = Create("alice@example.com").GetAwaiter().GetResult();
        _bob = Create("bob@example.com").GetAwaiter().GetResult();
    }

    private HttpClient As(string userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId);
        return client;
    }

    private static SaveGlossaryRequest Request(string name = "Projekt Alpha", params string[] terms) =>
        new(name, terms.Length > 0 ? [.. terms] : ["Kubernetes", "Müller GmbH"]);

    private async Task<GlossaryDto> CreateAsync(string userId, SaveGlossaryRequest request)
    {
        var response = await As(userId).PostAsJsonAsync("/api/glossaries", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<GlossaryDto>())!;
    }

    // --- CRUD ------------------------------------------------------------------

    [Fact]
    public async Task Create_ReturnsTheGlossaryWithTokenEstimate()
    {
        var glossary = await CreateAsync(_alice, Request());

        glossary.Name.Should().Be("Projekt Alpha");
        glossary.Terms.Should().Equal("Kubernetes", "Müller GmbH");
        glossary.EstimatedTokens.Should().Be(GlossaryPrompt.EstimateTokens("Kubernetes, Müller GmbH"));
        glossary.MaxTokens.Should().Be(GlossaryPrompt.MaxTokens);
    }

    [Fact]
    public async Task Create_TrimsTermsAndDropsEmptyLines()
    {
        var glossary = await CreateAsync(_alice, new SaveGlossaryRequest("G", ["  Alpha ", "", "   ", "Beta"]));

        glossary.Terms.Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public async Task List_ShowsOnlyOwnGlossaries()
    {
        await CreateAsync(_alice, Request("Alices"));
        await CreateAsync(_bob, Request("Bobs"));

        var list = await As(_alice).GetFromJsonAsync<List<GlossaryDto>>("/api/glossaries");

        list!.Select(g => g.Name).Should().Equal("Alices");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithoutName_Returns400(string name)
    {
        var response = await As(_alice).PostAsJsonAsync("/api/glossaries", Request(name));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithTooLongName_Returns400()
    {
        var response = await As(_alice).PostAsJsonAsync("/api/glossaries", Request(new string('x', 101)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithTooLongTerm_Returns400()
    {
        var response = await As(_alice).PostAsJsonAsync("/api/glossaries", Request("G", new string('x', 101)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithDuplicateTerms_Returns400()
    {
        var response = await As(_alice).PostAsJsonAsync("/api/glossaries", Request("G", "Alpha", "alpha"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("terms");
    }

    [Fact]
    public async Task Create_WithTooManyTerms_Returns400()
    {
        var terms = Enumerable.Range(0, 201).Select(i => $"T{i}").ToArray();

        var response = await As(_alice).PostAsJsonAsync("/api/glossaries", Request("G", terms));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_ExceedingTheTokenLimit_IsReportedInsteadOfTruncated()
    {
        var terms = Enumerable.Range(0, 100).Select(i => $"Fachbegriff{i:000}").ToArray();

        var response = await As(_alice).PostAsJsonAsync("/api/glossaries", Request("G", terms));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("224");
    }

    [Fact]
    public async Task Create_WithSameNameAsOwnGlossary_Returns409_ButAnotherUserMayReuseTheName()
    {
        await CreateAsync(_alice, Request("Alpha"));

        (await As(_alice).PostAsJsonAsync("/api/glossaries", Request("alpha"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await As(_bob).PostAsJsonAsync("/api/glossaries", Request("Alpha"))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Update_ReplacesNameAndTerms()
    {
        var glossary = await CreateAsync(_alice, Request());

        var response = await As(_alice).PutAsJsonAsync($"/api/glossaries/{glossary.Id}", new SaveGlossaryRequest("Neu", ["Eins", "Zwei"]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<GlossaryDto>();
        updated!.Name.Should().Be("Neu");
        updated.Terms.Should().Equal("Eins", "Zwei");
        (await As(_alice).GetFromJsonAsync<List<GlossaryDto>>("/api/glossaries"))!.Single().Terms.Should().Equal("Eins", "Zwei");
    }

    [Fact]
    public async Task Update_KeepingItsOwnName_IsNotAConflict()
    {
        var glossary = await CreateAsync(_alice, Request("Alpha"));

        var response = await As(_alice).PutAsJsonAsync($"/api/glossaries/{glossary.Id}", Request("Alpha", "Neu"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateAndDelete_OfAnotherUsersGlossary_Return404()
    {
        var glossary = await CreateAsync(_alice, Request());

        (await As(_bob).PutAsJsonAsync($"/api/glossaries/{glossary.Id}", Request("Gehackt"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await As(_bob).DeleteAsync($"/api/glossaries/{glossary.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await As(_alice).GetFromJsonAsync<List<GlossaryDto>>("/api/glossaries"))!.Single().Name.Should().Be("Projekt Alpha");
    }

    [Fact]
    public async Task Delete_RemovesTheGlossary_ButKeepsThePromptOfExistingJobs()
    {
        var glossary = await CreateAsync(_alice, Request());
        var jobId = await UploadAsync(_alice, glossary.Id);

        (await As(_alice).DeleteAsync($"/api/glossaries/{glossary.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await As(_alice).GetFromJsonAsync<List<GlossaryDto>>("/api/glossaries"))!.Should().BeEmpty();
        (await PromptOfAsync(jobId)).Should().Be("Kubernetes, Müller GmbH");
    }

    // --- upload ------------------------------------------------------------------

    [Fact]
    public async Task Upload_WithGlossary_StoresThePromptOnTheJob()
    {
        var glossary = await CreateAsync(_alice, Request());

        var jobId = await UploadAsync(_alice, glossary.Id);

        (await PromptOfAsync(jobId)).Should().Be("Kubernetes, Müller GmbH");
    }

    [Fact]
    public async Task Upload_WithoutGlossary_HasNoPrompt()
    {
        var jobId = await UploadAsync(_alice, null);

        (await PromptOfAsync(jobId)).Should().BeNull();
    }

    [Fact]
    public async Task Upload_WithAnotherUsersGlossary_Returns400_AndStoresNothing()
    {
        var glossary = await CreateAsync(_alice, Request());

        var response = await PostUploadAsync(_bob, glossary.Id.ToString());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Directory.GetFiles(_dir.Path, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Theory]
    [InlineData("not-a-guid")]
    public async Task Upload_WithMalformedGlossaryId_Returns400(string glossaryId)
    {
        (await PostUploadAsync(_alice, glossaryId)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<HttpResponseMessage> PostUploadAsync(string userId, string? glossaryId)
    {
        using var content = new MultipartFormDataContent();
        if (glossaryId is not null)
            content.Add(new StringContent(glossaryId), "glossaryId");
        var file = new ByteArrayContent([.. "ID3"u8.ToArray(), 0, 0, 0, 0]);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        content.Add(file, "file", "new.mp3");
        return await As(userId).PostAsync("/api/audio-jobs", content);
    }

    private async Task<Guid> UploadAsync(string userId, Guid? glossaryId)
    {
        var response = await PostUploadAsync(userId, glossaryId?.ToString());
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (await response.Content.ReadFromJsonAsync<CreateAudioJobResponse>())!.Id;
    }

    private async Task<string?> PromptOfAsync(Guid jobId)
    {
        using var scope = _factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AudioJobs.SingleAsync(j => j.Id == jobId)).InitialPrompt;
    }

    public void Dispose()
    {
        _factory.Dispose();
        _dir.Dispose();
    }
}
