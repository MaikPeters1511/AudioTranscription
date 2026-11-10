using System.Text.Json;
using AudioTranscription.Api.Access;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Chat;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Api.Chat;

/// <summary>The part of the user's material a question refers to.</summary>
public record ChatScope(string Kind = "all", Guid? Id = null);

public record ChatRequest(string Question, ChatScope? Scope = null, Guid? ConversationId = null);

public record ChatCitationDto(int Number, Guid JobId, long StartMs, long EndMs);
public record ChatMessageDto(Guid Id, string Role, string Text, List<ChatCitationDto> Citations, DateTime CreatedAtUtc);
public record ChatConversationDto(Guid Id, string Title, DateTime UpdatedAtUtc, List<ChatMessageDto>? Messages = null);

/// <summary>Which model answers: <c>External</c> is true only when an external provider was configured explicitly (ADR 0011).</summary>
public record ChatInfoDto(bool Enabled, string? Provider, string? Model, bool External);

/// <summary>Which language model answers the chat; registered together with the chat.</summary>
public record ChatModelInfo(string Provider, string Model)
{
    public bool External => !string.Equals(Provider, ChatSettings.OllamaProvider, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Chat with the user's transcripts (S24, ADR 0011): streamed answers with citations, private conversations.</summary>
public static class ChatEndpoints
{
    public const string RateLimitPolicy = "chat";
    private const int MaxQuestionLength = ChatPrompt.MaxQuestionLength;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapChatEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/chat").WithTags("Chat");

        group.MapGet("/info", Info)
            .WithName("GetChatInfo")
            .WithSummary("Whether the chat is available and which model answers");
        group.MapPost("", Ask)
            .RequireRateLimiting(RateLimitPolicy)
            .WithName("AskChat")
            .WithSummary("Ask a question about transcripts (streamed)")
            .WithDescription("Server-Sent Events: meta (conversationId), delta (text), done (text, citations, messageId) or error (message). scope.kind is all, job or folder.");
        group.MapGet("", List).WithName("ListChatConversations").WithSummary("The user's conversations, newest first");
        group.MapGet("{id:guid}", Get).WithName("GetChatConversation").WithSummary("A conversation with its messages");
        group.MapDelete("{id:guid}", Delete).WithName("DeleteChatConversation").WithSummary("Delete a conversation");
    }

    private static Ok<ChatInfoDto> Info(IServiceProvider services)
    {
        var model = services.GetService<ChatModelInfo>();
        return TypedResults.Ok(model is null
            ? new ChatInfoDto(false, null, null, false)
            : new ChatInfoDto(true, model.Provider, model.Model, model.External));
    }

    private static async Task Ask(HttpContext http, ChatRequest request, AppDbContext db, JobAccessService access, IOptions<ChatSettings> options)
    {
        var cancellationToken = http.RequestAborted;
        var userId = JobAccessService.GetUserId(http.User);
        var chat = http.RequestServices.GetService<ChatService>();
        if (string.IsNullOrEmpty(userId))
        {
            await Problem(http, "Not signed in", "The user of this request could not be determined.", StatusCodes.Status401Unauthorized);
            return;
        }
        if (chat is null)
        {
            await Problem(http, "Chat is not available", "No embedding model, vector database or language model is configured on this server.", StatusCodes.Status503ServiceUnavailable);
            return;
        }

        var question = request.Question?.Trim() ?? string.Empty;
        if (question.Length == 0 || question.Length > MaxQuestionLength)
        {
            await Problem(http, "Invalid question", $"The question must not be empty and at most {MaxQuestionLength} characters long.", StatusCodes.Status400BadRequest);
            return;
        }

        // The scope is checked against the user's current rights at every question
        var scope = request.Scope ?? new ChatScope();
        var allowed = await AllowedJobsAsync(db, access, userId, scope, cancellationToken);
        if (allowed is null)
        {
            await Problem(http, "Not found", "The recording or folder does not exist.", StatusCodes.Status404NotFound);
            return;
        }

        ChatConversation? conversation = null;
        if (request.ConversationId is { } conversationId)
        {
            conversation = await db.ChatConversations.SingleOrDefaultAsync(c => c.Id == conversationId && c.OwnerId == userId, cancellationToken);
            if (conversation is null)
            {
                await Problem(http, "Not found", "The conversation does not exist.", StatusCodes.Status404NotFound);
                return;
            }
        }

        var history = conversation is null
            ? []
            : (await db.ChatMessages.Where(m => m.ConversationId == conversation.Id)
                .OrderByDescending(m => m.CreatedAtUtc).Take(ChatPrompt.MaxHistoryMessages).ToListAsync(cancellationToken))
                .OrderBy(m => m.CreatedAtUtc).Select(m => new PromptMessage(m.Role, m.Text)).ToList();

        if (conversation is null)
        {
            conversation = new ChatConversation { OwnerId = userId, Title = question.Length <= 80 ? question : question[..80].TrimEnd() };
            db.ChatConversations.Add(conversation);
        }
        db.ChatMessages.Add(new ConversationMessage { ConversationId = conversation.Id, Role = "user", Text = question });
        conversation.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no"; // nginx: send the events as they come
        await Send(http, "meta", new { conversationId = conversation.Id });

        try
        {
            await foreach (var chatEvent in chat.AnswerAsync(question, allowed, history, cancellationToken))
            {
                switch (chatEvent)
                {
                    case ChatTextEvent text:
                        await Send(http, "delta", new { text = text.Text });
                        break;
                    case ChatDoneEvent done:
                        var citations = done.Citations.Select(c => new ChatCitationDto(c.Number, c.JobId, c.StartMs, c.EndMs)).ToList();
                        var message = new ConversationMessage
                        {
                            ConversationId = conversation.Id,
                            Role = "assistant",
                            Text = done.Text,
                            CitationsJson = citations.Count == 0 ? null : JsonSerializer.Serialize(citations, Json),
                        };
                        db.ChatMessages.Add(message);
                        conversation.UpdatedAtUtc = DateTime.UtcNow;
                        await db.SaveChangesAsync(cancellationToken);
                        await Send(http, "done", new { messageId = message.Id, text = done.Text, citations });
                        break;
                    case ChatErrorEvent error:
                        await Send(http, "error", new { message = error.Message });
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The client went away: the model request is cancelled and the half answer is not stored
        }
    }

    private static async Task<Ok<List<ChatConversationDto>>> List(HttpContext http, AppDbContext db)
    {
        var userId = JobAccessService.GetUserId(http.User) ?? string.Empty;
        var conversations = await db.ChatConversations.AsNoTracking()
            .Where(c => c.OwnerId == userId).OrderByDescending(c => c.UpdatedAtUtc).Take(50)
            .Select(c => new ChatConversationDto(c.Id, c.Title, c.UpdatedAtUtc, null))
            .ToListAsync();
        return TypedResults.Ok(conversations);
    }

    private static async Task<Results<Ok<ChatConversationDto>, NotFound>> Get(Guid id, HttpContext http, AppDbContext db)
    {
        var userId = JobAccessService.GetUserId(http.User) ?? string.Empty;
        var conversation = await db.ChatConversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.OwnerId == userId);
        if (conversation is null)
            return TypedResults.NotFound();

        var messages = await db.ChatMessages.AsNoTracking().Where(m => m.ConversationId == id).OrderBy(m => m.CreatedAtUtc).ToListAsync();
        return TypedResults.Ok(new ChatConversationDto(
            conversation.Id, conversation.Title, conversation.UpdatedAtUtc,
            messages.Select(m => new ChatMessageDto(
                m.Id, m.Role, m.Text,
                m.CitationsJson is null ? [] : JsonSerializer.Deserialize<List<ChatCitationDto>>(m.CitationsJson, Json) ?? [],
                m.CreatedAtUtc)).ToList()));
    }

    private static async Task<Results<NoContent, NotFound>> Delete(Guid id, HttpContext http, AppDbContext db)
    {
        var userId = JobAccessService.GetUserId(http.User) ?? string.Empty;
        var conversation = await db.ChatConversations.SingleOrDefaultAsync(c => c.Id == id && c.OwnerId == userId);
        if (conversation is null)
            return TypedResults.NotFound();

        db.ChatMessages.RemoveRange(db.ChatMessages.Where(m => m.ConversationId == id));
        db.ChatConversations.Remove(conversation);
        await db.SaveChangesAsync();
        return TypedResults.NoContent();
    }

    /// <summary>The jobs a question may draw on, or null when the scope names something the user cannot see.</summary>
    private static async Task<List<Guid>?> AllowedJobsAsync(AppDbContext db, JobAccessService access, string userId, ChatScope scope, CancellationToken cancellationToken)
    {
        switch (scope.Kind.ToLowerInvariant())
        {
            case "all":
                return await access.VisibleJobIds(userId).ToListAsync(cancellationToken);
            case "job" when scope.Id is { } jobId:
                return await access.GetAccessAsync(jobId, userId) == JobAccess.None ? null : [jobId];
            case "folder" when scope.Id is { } folderId:
                var inFolder = await db.AudioJobs.Where(j => j.FolderId == folderId && j.OwnerId == userId).Select(j => j.Id).ToListAsync(cancellationToken);
                // A folder of someone else is as good as a missing one
                return inFolder.Count > 0 || await db.Folders.AnyAsync(f => f.Id == folderId && f.OwnerId == userId, cancellationToken) ? inFolder : null;
            default:
                return null;
        }
    }

    private static async Task Send(HttpContext http, string name, object data)
    {
        await http.Response.WriteAsync($"event: {name}\ndata: {JsonSerializer.Serialize(data, Json)}\n\n", http.RequestAborted);
        await http.Response.Body.FlushAsync(http.RequestAborted);
    }

    private static Task Problem(HttpContext http, string title, string detail, int status) =>
        Results.Problem(title: title, detail: detail, statusCode: status).ExecuteAsync(http);
}
