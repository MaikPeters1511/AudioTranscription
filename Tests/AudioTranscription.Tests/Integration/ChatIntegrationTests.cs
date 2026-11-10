using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AudioTranscription.Api.Chat;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Embeddings;
using AudioTranscription.Tests.Search;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Tests.Integration;

/// <summary>S24-T3/T5: the chat endpoints — streaming, permissions, private conversations, rate limit.</summary>
public class ChatIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string Other = "other-user";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly FakeEmbeddingService _embeddings = new();
    private readonly InMemoryVectorIndex _index = new();
    private readonly ScriptedChatClient _llm = new();

    public ChatIntegrationTests(WebApplicationFactory<Program> factory)
    {
        var dbName = Guid.NewGuid().ToString();
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Chat:RequestsPerMinute", "3");
            builder.ConfigureTestServices(services =>
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
                services.AddSingleton(new ChatModelInfo("Ollama", "llama3.2"));
                services.AddScoped(_ => new ChatService(_embeddings, _index, _llm, Options.Create(new ChatSettings { MinScore = 0.2 }), NullLogger<ChatService>.Instance));
            });
        });
        _client = _factory.CreateClient();
    }

    private async Task<Guid> SeedJobAsync(string owner, string text, Guid? folder = null)
    {
        var job = new AudioJob { OwnerId = owner, FileName = "a.mp3", ContentType = "audio/mpeg", Model = "Base", Status = AudioJobStatus.Completed, FolderId = folder };
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AudioJobs.Add(job);
        await db.SaveChangesAsync();
        var vector = (await _embeddings.EmbedAsync([text]))[0];
        await _index.ReplaceJobAsync(job.Id, owner, [new IndexedChunk(0, 60_000, 90_000, text, vector)]);
        return job.Id;
    }

    private record Sse(string Event, JsonElement Data);

    private static List<Sse> ParseSse(string body) =>
        body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Select(block =>
        {
            var lines = block.Split('\n');
            return new Sse(lines[0]["event: ".Length..], JsonDocument.Parse(lines[1]["data: ".Length..]).RootElement.Clone());
        }).ToList();

    private async Task<(HttpResponseMessage Response, List<Sse> Events)> AskAsync(object body, string? user = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat") { Content = JsonContent.Create(body) };
        if (user is not null) request.Headers.Add(TestAuthHandler.UserIdHeader, user);
        var response = await _client.SendAsync(request);
        var text = response.Content.Headers.ContentType?.MediaType == "text/event-stream" ? await response.Content.ReadAsStringAsync() : "";
        return (response, text.Length == 0 ? [] : ParseSse(text));
    }

    // --- asking ------------------------------------------------------------------------

    [Fact]
    public async Task Ask_StreamsMetaDeltasAndDone_WithCitations()
    {
        var job = await SeedJobAsync(TestAuthHandler.DefaultUserId, "In der Budgetrunde ging es um die Kosten.");
        _llm.Script = (_, _) => ["Es ging ums Geld ", "[1]."];

        var (response, events) = await AskAsync(new ChatRequest("Wann ging es ums Geld?"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");
        events.Select(e => e.Event).Should().Equal("meta", "delta", "delta", "done");
        var done = events[^1].Data;
        done.GetProperty("text").GetString().Should().Be("Es ging ums Geld [1].");
        var citation = done.GetProperty("citations").EnumerateArray().Single();
        citation.GetProperty("jobId").GetGuid().Should().Be(job);
        citation.GetProperty("startMs").GetInt64().Should().Be(60_000);
    }

    [Fact]
    public async Task Ask_StoresTheConversation_AndItCanBeRead()
    {
        await SeedJobAsync(TestAuthHandler.DefaultUserId, "Die Kosten steigen.");
        var (_, events) = await AskAsync(new ChatRequest("Wie steht es um das Geld?"));
        var conversationId = events[0].Data.GetProperty("conversationId").GetGuid();

        var conversation = await _client.GetFromJsonAsync<ChatConversationDto>($"/api/chat/{conversationId}");

        conversation!.Title.Should().Be("Wie steht es um das Geld?");
        conversation.Messages!.Select(m => m.Role).Should().Equal("user", "assistant");
        conversation.Messages![1].Citations.Should().ContainSingle();
        (await _client.GetFromJsonAsync<List<ChatConversationDto>>("/api/chat"))!.Should().ContainSingle();
    }

    [Fact]
    public async Task Ask_AFollowUp_ContinuesTheConversation()
    {
        await SeedJobAsync(TestAuthHandler.DefaultUserId, "Die Kosten steigen.");
        var (_, first) = await AskAsync(new ChatRequest("Wie steht es um das Geld?"));
        var conversationId = first[0].Data.GetProperty("conversationId").GetGuid();

        var (_, second) = await AskAsync(new ChatRequest("Und die Kosten?", ConversationId: conversationId));

        second[0].Data.GetProperty("conversationId").GetGuid().Should().Be(conversationId);
        _llm.Requests[^1].Select(m => m.Text).Should().Contain("Wie steht es um das Geld?");
        (await _client.GetFromJsonAsync<ChatConversationDto>($"/api/chat/{conversationId}"))!.Messages.Should().HaveCount(4);
    }

    [Fact]
    public async Task Ask_NeverUsesTheJobsOfAnotherUser()
    {
        await SeedJobAsync(Other, "Das geheime Budget des anderen.");

        var (_, events) = await AskAsync(new ChatRequest("Wie hoch ist das Budget?"));

        events[^1].Data.GetProperty("text").GetString().Should().Be("Dazu finde ich nichts im Material.");
        _llm.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Ask_ForARecordingOfSomeoneElse_Returns404()
    {
        var foreign = await SeedJobAsync(Other, "Das Budget.");

        var (response, _) = await AskAsync(new ChatRequest("Budget?", new ChatScope("job", foreign)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ask_ForASharedRecording_IsAllowed()
    {
        var shared = await SeedJobAsync(Other, "Die Kosten steigen.");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.JobShares.Add(new JobShare { AudioJobId = shared, UserId = TestAuthHandler.DefaultUserId });
            await db.SaveChangesAsync();
        }

        var (response, events) = await AskAsync(new ChatRequest("Geld?", new ChatScope("job", shared)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        events[^1].Data.GetProperty("citations").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Ask_WithAFolderScope_OnlyUsesThatFolder()
    {
        var folder = new Folder { OwnerId = TestAuthHandler.DefaultUserId, Name = "Projekt" };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
        }
        var inside = await SeedJobAsync(TestAuthHandler.DefaultUserId, "Das Budget im Ordner.", folder.Id);
        await SeedJobAsync(TestAuthHandler.DefaultUserId, "Das Budget außerhalb.");

        var (_, events) = await AskAsync(new ChatRequest("Geld?", new ChatScope("folder", folder.Id)));

        events[^1].Data.GetProperty("citations").EnumerateArray().Single().GetProperty("jobId").GetGuid().Should().Be(inside);
    }

    [Fact]
    public async Task Ask_WithAFolderOfSomeoneElse_Returns404()
    {
        var folder = new Folder { OwnerId = Other, Name = "Fremd" };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Folders.Add(folder);
            await db.SaveChangesAsync();
        }

        var (response, _) = await AskAsync(new ChatRequest("Geld?", new ChatScope("folder", folder.Id)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Ask_WithoutAQuestion_Returns400(string question)
    {
        var (response, _) = await AskAsync(new ChatRequest(question));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Ask_WithATooLongQuestion_Returns400()
    {
        var (response, _) = await AskAsync(new ChatRequest(new string('f', 2_001)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Ask_WithAnUnknownScope_Returns404()
    {
        var (response, _) = await AskAsync(new ChatRequest("Geld?", new ChatScope("planet", Guid.NewGuid())));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ask_WhenTheModelFails_SendsACleanErrorEvent()
    {
        await SeedJobAsync(TestAuthHandler.DefaultUserId, "Die Kosten steigen.");
        _llm.Script = (_, _) => throw new HttpRequestException("connect ECONNREFUSED 10.0.0.5:11434");

        var (_, events) = await AskAsync(new ChatRequest("Geld?"));

        events[^1].Event.Should().Be("error");
        events[^1].Data.GetProperty("message").GetString().Should().Be(ChatService.UnavailableMessage).And.NotContain("10.0.0.5");
    }

    [Fact]
    public async Task Ask_IsLimitedPerUser_Returns429AfterTheLimit()
    {
        await SeedJobAsync(TestAuthHandler.DefaultUserId, "Die Kosten steigen.");

        for (var i = 0; i < 3; i++)
            (await AskAsync(new ChatRequest("Geld?"))).Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var fourth = await AskAsync(new ChatRequest("Geld?"));
        var someoneElse = await AskAsync(new ChatRequest("Geld?"), user: Other);

        fourth.Response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        someoneElse.Response.StatusCode.Should().Be(HttpStatusCode.OK, "each user has their own limit");
    }

    [Fact]
    public async Task Ask_WhenTheClientGivesUp_TheHalfAnswerIsNotStored()
    {
        await SeedJobAsync(TestAuthHandler.DefaultUserId, "Die Kosten steigen.");
        var firstWord = new TaskCompletionSource();
        _llm.Script = (_, _) => SlowAnswer(firstWord);
        using var cts = new CancellationTokenSource();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat") { Content = JsonContent.Create(new ChatRequest("Geld?")) };

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cts.Token) is { } line && !line.StartsWith("event: delta")) { }
        // The user closes the page: the connection is dropped while the model is still answering
        cts.Cancel();
        reader.Dispose();
        stream.Dispose();
        response.Dispose();
        firstWord.TrySetResult();
        await Task.Delay(500);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ChatMessages.CountAsync(m => m.Role == "assistant")).Should().Be(0);
    }

    private static IEnumerable<string> SlowAnswer(TaskCompletionSource gate)
    {
        yield return "Anfang ";
        gate.Task.Wait(TimeSpan.FromSeconds(2));
        yield return "Rest [1].";
    }

    // --- privacy of conversations ------------------------------------------------------------

    [Fact]
    public async Task Conversations_ArePrivate()
    {
        await SeedJobAsync(TestAuthHandler.DefaultUserId, "Die Kosten steigen.");
        var (_, events) = await AskAsync(new ChatRequest("Geld?"));
        var id = events[0].Data.GetProperty("conversationId").GetGuid();

        using var get = new HttpRequestMessage(HttpMethod.Get, $"/api/chat/{id}");
        get.Headers.Add(TestAuthHandler.UserIdHeader, Other);
        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/chat/{id}");
        delete.Headers.Add(TestAuthHandler.UserIdHeader, Other);
        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/chat");
        list.Headers.Add(TestAuthHandler.UserIdHeader, Other);

        (await _client.SendAsync(get)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.SendAsync(delete)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await (await _client.SendAsync(list)).Content.ReadFromJsonAsync<List<ChatConversationDto>>())!.Should().BeEmpty();
        var (continued, _) = await AskAsync(new ChatRequest("Weiter?", ConversationId: id), user: Other);
        continued.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_RemovesTheConversationAndItsMessages()
    {
        await SeedJobAsync(TestAuthHandler.DefaultUserId, "Die Kosten steigen.");
        var (_, events) = await AskAsync(new ChatRequest("Geld?"));
        var id = events[0].Data.GetProperty("conversationId").GetGuid();

        (await _client.DeleteAsync($"/api/chat/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await _client.GetAsync($"/api/chat/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().ChatMessages.CountAsync()).Should().Be(0);
    }

    // --- provider ---------------------------------------------------------------------------------

    [Fact]
    public async Task Info_NamesTheActiveProvider_AndSaysWhetherItIsExternal()
    {
        var info = await _client.GetFromJsonAsync<ChatInfoDto>("/api/chat/info");

        info.Should().Be(new ChatInfoDto(true, "Ollama", "llama3.2", false));
        new ChatModelInfo("OpenAI", "gpt-x").External.Should().BeTrue();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
