using System.Net;
using System.Net.Http.Json;
using AudioTranscription.Api.Auth;
using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Domain.Templates;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AudioTranscription.Tests.Integration;

/// <summary>S26: template management and template-based variants.</summary>
public class TemplateIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string ValidSchema = """{"type":"object","properties":{"summary":{"type":"string"}}}""";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Mock<ITemplateVariantGenerator> _generator = new();
    private readonly VariantQueue _queue = new();

    public TemplateIntegrationTests(WebApplicationFactory<Program> factory)
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
            services.AddSingleton(_generator.Object);
            services.AddSingleton(Mock.Of<IVariantGenerator>());
            services.AddSingleton(_queue);
        }));
        _client = _factory.CreateClient();
    }

    private static SaveTemplateRequest Valid(string name = "Eigene Vorlage") => new(name, "Beschreibung", "Fasse zusammen. Sprecher: {{SPEAKERS}}", ValidSchema);

    private async Task<HttpResponseMessage> AsAdmin(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.RolesHeader, Roles.Admin);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    // --- list --------------------------------------------------------------------

    [Fact]
    public async Task List_ContainsTheThreeBuiltInTemplates_ForEveryUser()
    {
        var templates = await _client.GetFromJsonAsync<List<TemplateDto>>("/api/templates");

        templates!.Where(t => t.IsBuiltIn).Select(t => t.Name).Should()
            .BeEquivalentTo("Besprechungsprotokoll", "Entscheidungsliste", "Aufgaben mit Verantwortlichen");
    }

    // --- create / update / delete -------------------------------------------------

    [Fact]
    public async Task Create_ByAnAdmin_StoresACustomTemplate()
    {
        var response = await AsAdmin(HttpMethod.Post, "/api/templates", Valid());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<TemplateDto>();
        created!.IsBuiltIn.Should().BeFalse();
        (await _client.GetFromJsonAsync<List<TemplateDto>>("/api/templates"))!.Should().Contain(t => t.Id == created.Id);
    }

    [Fact]
    public async Task Create_ByANormalUser_IsForbidden()
    {
        var response = await _client.PostAsJsonAsync("/api/templates", Valid());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_WithAnUnknownPlaceholder_Returns400()
    {
        var response = await AsAdmin(HttpMethod.Post, "/api/templates", Valid() with { Prompt = "Hallo {{PASSWORD}}" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("PASSWORD");
    }

    [Theory]
    [InlineData("")]
    [InlineData("nicht json")]
    [InlineData("""{"type":"banane"}""")]
    public async Task Create_WithAnInvalidSchema_Returns400(string schema)
    {
        var response = await AsAdmin(HttpMethod.Post, "/api/templates", Valid() with { OutputSchema = schema });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithATooLongPromptOrNoName_Returns400()
    {
        (await AsAdmin(HttpMethod.Post, "/api/templates", Valid() with { Prompt = new string('x', TemplatePromptRules.MaxPromptLength + 1) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AsAdmin(HttpMethod.Post, "/api/templates", Valid(" "))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithADuplicateName_Returns400()
    {
        await AsAdmin(HttpMethod.Post, "/api/templates", Valid("Doppelt"));

        (await AsAdmin(HttpMethod.Post, "/api/templates", Valid("Doppelt"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_ChangesACustomTemplate()
    {
        var created = await (await AsAdmin(HttpMethod.Post, "/api/templates", Valid())).Content.ReadFromJsonAsync<TemplateDto>();

        var response = await AsAdmin(HttpMethod.Put, $"/api/templates/{created!.Id}", Valid("Neuer Name"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<TemplateDto>())!.Name.Should().Be("Neuer Name");
    }

    [Fact]
    public async Task BuiltInTemplates_CannotBeChangedOrDeleted()
    {
        var id = BuiltInTemplates.MeetingMinutesId;

        (await AsAdmin(HttpMethod.Put, $"/api/templates/{id}", Valid())).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AsAdmin(HttpMethod.Delete, $"/api/templates/{id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Delete_RemovesACustomTemplate_ButKeepsItsVariants()
    {
        var created = await (await AsAdmin(HttpMethod.Post, "/api/templates", Valid())).Content.ReadFromJsonAsync<TemplateDto>();
        var job = await SeedJobAsync();
        await _client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants", new CreateVariantRequest(PostProcessingMode.Template, null, created!.Id));

        (await AsAdmin(HttpMethod.Delete, $"/api/templates/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var variants = await _client.GetFromJsonAsync<List<TranscriptVariantDto>>($"/api/audio-jobs/{job.Id}/variants");
        variants.Should().ContainSingle().Which.TemplateId.Should().BeNull();
    }

    // --- variants from a template ---------------------------------------------------

    [Fact]
    public async Task CreateVariant_FromATemplate_Returns202_WithTheTemplateName()
    {
        var job = await SeedJobAsync();

        var response = await _client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants",
            new CreateVariantRequest(PostProcessingMode.Template, null, BuiltInTemplates.TasksWithOwnersId));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var variant = await response.Content.ReadFromJsonAsync<TranscriptVariantDto>();
        variant!.TemplateName.Should().Be("Aufgaben mit Verantwortlichen");
        variant.Status.Should().Be(VariantStatus.Pending);
    }

    [Fact]
    public async Task CreateVariant_FromATemplate_WithoutAKnownTemplate_Returns400()
    {
        var job = await SeedJobAsync();

        (await _client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants", new CreateVariantRequest(PostProcessingMode.Template, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants", new CreateVariantRequest(PostProcessingMode.Template, null, Guid.NewGuid())))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateVariant_ForTwoTemplates_KeepsTwoVariants_AndARerunOverwritesItsOwn()
    {
        var job = await SeedJobAsync();
        async Task Request(Guid templateId) =>
            await _client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants", new CreateVariantRequest(PostProcessingMode.Template, null, templateId));

        await Request(BuiltInTemplates.MeetingMinutesId);
        await Request(BuiltInTemplates.DecisionListId);
        await Request(BuiltInTemplates.MeetingMinutesId);

        var variants = await _client.GetFromJsonAsync<List<TranscriptVariantDto>>($"/api/audio-jobs/{job.Id}/variants");
        variants!.Should().HaveCount(2);
    }

    [Fact]
    public async Task Worker_StoresTheMarkdownOfATemplate_WithOnlyNamedSpeakers()
    {
        var job = await SeedJobAsync(speakers: new Dictionary<int, string?> { [0] = "Anna", [1] = null });
        IReadOnlyCollection<string>? names = null;
        string? sentTranscript = null;
        _generator.Setup(g => g.GenerateAsync(It.IsAny<PostprocessingTemplate>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<PostprocessingTemplate, string, IReadOnlyCollection<string>, CancellationToken>((_, t, n, _) => { sentTranscript = t; names = n; })
            .ReturnsAsync("## Aufgaben\n\n- Angebot einholen — Verantwortlich: Anna\n");
        await _client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants", new CreateVariantRequest(PostProcessingMode.Template, null, BuiltInTemplates.TasksWithOwnersId));

        await RunWorkerAsync();

        var variant = (await _client.GetFromJsonAsync<List<TranscriptVariantDto>>($"/api/audio-jobs/{job.Id}/variants"))!.Single();
        variant.Status.Should().Be(VariantStatus.Completed);
        variant.Text.Should().Contain("Verantwortlich: Anna");
        names.Should().Equal("Anna");
        sentTranscript.Should().Contain("Anna: Ich hole das Angebot ein.");
        sentTranscript.Should().Contain("Und ich buche den Raum.").And.NotContain("Sprecher 2:");
    }

    [Fact]
    public async Task Worker_WithoutNamedSpeakers_SendsThePlainTranscript_AndNoNames()
    {
        var job = await SeedJobAsync();
        IReadOnlyCollection<string>? names = null;
        string? sentTranscript = null;
        _generator.Setup(g => g.GenerateAsync(It.IsAny<PostprocessingTemplate>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<PostprocessingTemplate, string, IReadOnlyCollection<string>, CancellationToken>((_, t, n, _) => { sentTranscript = t; names = n; })
            .ReturnsAsync("## Beschlüsse\n\n- Budget\n");
        await _client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants", new CreateVariantRequest(PostProcessingMode.Template, null, BuiltInTemplates.DecisionListId));

        await RunWorkerAsync();

        names.Should().BeEmpty();
        sentTranscript.Should().Be(job.RawTranscript);
    }

    [Fact]
    public async Task Worker_WithAnInvalidAnswer_MarksTheVariantFailed_WithAFixedMessage()
    {
        var job = await SeedJobAsync();
        _generator.Setup(g => g.GenerateAsync(It.IsAny<PostprocessingTemplate>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TemplateOutputException("$.tasks[0]: the property 'task' is missing."));
        await _client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants", new CreateVariantRequest(PostProcessingMode.Template, null, BuiltInTemplates.TasksWithOwnersId));

        await RunWorkerAsync();

        var variant = (await _client.GetFromJsonAsync<List<TranscriptVariantDto>>($"/api/audio-jobs/{job.Id}/variants"))!.Single();
        variant.Status.Should().Be(VariantStatus.Failed);
        variant.ErrorMessage.Should().Be(VariantWorker.InvalidOutputMessage);
    }

    [Fact]
    public async Task Worker_WhenTheModelIsUnreachable_DoesNotLeakDetails()
    {
        var job = await SeedJobAsync();
        _generator.Setup(g => g.GenerateAsync(It.IsAny<PostprocessingTemplate>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connect ECONNREFUSED 10.0.0.5:11434"));
        await _client.PostAsJsonAsync($"/api/audio-jobs/{job.Id}/variants", new CreateVariantRequest(PostProcessingMode.Template, null, BuiltInTemplates.TasksWithOwnersId));

        await RunWorkerAsync();

        var variant = (await _client.GetFromJsonAsync<List<TranscriptVariantDto>>($"/api/audio-jobs/{job.Id}/variants"))!.Single();
        variant.ErrorMessage.Should().Be(VariantWorker.GenerationFailureMessage).And.NotContain("10.0.0.5");
    }

    // --- helpers ---------------------------------------------------------------

    private async Task<AudioJob> SeedJobAsync(Dictionary<int, string?>? speakers = null)
    {
        var job = new AudioJob
        {
            OwnerId = TestAuthHandler.DefaultUserId,
            FileName = "meeting.mp3", ContentType = "audio/mpeg", Model = "Base",
            Status = AudioJobStatus.Completed, RawTranscript = "Ich hole das Angebot ein. Und ich buche den Raum.",
        };
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AudioJobs.Add(job);
        if (speakers is not null)
        {
            db.TranscriptSegments.AddRange(
                new TranscriptSegment { AudioJobId = job.Id, Index = 0, StartMs = 0, EndMs = 2_000, Text = "Ich hole das Angebot ein.", SpeakerIndex = 0 },
                new TranscriptSegment { AudioJobId = job.Id, Index = 1, StartMs = 2_000, EndMs = 4_000, Text = "Und ich buche den Raum.", SpeakerIndex = 1 });
            foreach (var (index, name) in speakers)
                db.JobSpeakers.Add(new JobSpeaker { AudioJobId = job.Id, Index = index, DisplayName = name });
        }
        await db.SaveChangesAsync();
        return job;
    }

    private async Task RunWorkerAsync()
    {
        var worker = new VariantWorker(_queue, _factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<VariantWorker>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var request in _queue.ReadAllAsync(cts.Token))
        {
            await worker.ProcessVariantAsync(request, CancellationToken.None);
            break;
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
