using AudioTranscription.Api.Access;
using AudioTranscription.Api.Auth;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Api.Endpoints;
using AudioTranscription.Domain.Search;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Embeddings;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Search;

/// <summary>Semantic and hybrid search over the transcripts (S23, ADR 0010) and the admin controls of the vector index.</summary>
public static class SemanticSearchEndpoints
{
    /// <summary>How many hits of each list take part in the fusion of the hybrid search.</summary>
    private const int FusionDepth = 50;
    private const int SnippetLength = 240;

    public static void MapSemanticSearchEndpoints(this WebApplication app)
    {
        var search = app.MapGroup("/api/search").WithTags("Search");
        search.MapGet("/modes", Modes)
            .WithName("GetSearchModes")
            .WithSummary("Which search modes are available")
            .WithDescription("Whether semantic and hybrid search are configured and usable (index built with the current model)");
        search.MapGet("/semantic", Semantic)
            .WithName("SemanticSearch")
            .WithSummary("Search by meaning")
            .WithDescription("q in natural language; mode=semantic (default) or hybrid (combined with the full-text search by Reciprocal Rank Fusion). 503: not configured, 409: index has to be rebuilt");

        var admin = app.MapGroup("/api/admin/search").WithTags("Admin").RequireAuthorization(Roles.AdminPolicy);
        admin.MapPost("/reindex", Reindex)
            .WithName("ReindexSemanticSearch")
            .WithSummary("Rebuild the semantic index")
            .WithDescription("Drops the vector index and indexes all completed jobs again with the configured model; progress is in the log and in the status");
        admin.MapGet("/status", Status)
            .WithName("GetSemanticIndexStatus")
            .WithSummary("State of the semantic index");
    }

    private static async Task<Ok<SearchModesDto>> Modes(IServiceProvider services)
    {
        var indexer = services.GetService<SemanticIndexer>();
        var configured = services.GetService<IEmbeddingService>() is not null && services.GetService<IVectorIndex>() is not null;
        if (!configured || indexer is null)
            return TypedResults.Ok(new SearchModesDto(false, "NotConfigured"));
        var status = await indexer.GetStatusAsync();
        return TypedResults.Ok(new SearchModesDto(status != IndexStatus.Stale, status.ToString()));
    }

    private static async Task<Results<Ok<PaginatedResult<SearchHitDto>>, ValidationProblem, ProblemHttpResult>> Semantic(
        [FromQuery] string? q,
        HttpContext http,
        AppDbContext dbContext,
        JobAccessService access,
        IServiceProvider services,
        [FromQuery] string? mode = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (string.IsNullOrWhiteSpace(q))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["q"] = ["A search term is required."] });
        var hybrid = string.Equals(mode, "hybrid", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(mode) && !hybrid && !string.Equals(mode, "semantic", StringComparison.OrdinalIgnoreCase))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["mode"] = ["Use mode=semantic or mode=hybrid."] });

        var embeddings = services.GetService<IEmbeddingService>();
        var index = services.GetService<IVectorIndex>();
        var indexer = services.GetService<SemanticIndexer>();
        if (embeddings is null || index is null || indexer is null)
        {
            return TypedResults.Problem(
                title: "Semantic search is not available",
                detail: "No embedding model or vector database is configured on this server.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var status = await indexer.GetStatusAsync(http.RequestAborted);
        if (status == IndexStatus.Stale)
        {
            return TypedResults.Problem(
                title: "The semantic index is outdated",
                detail: "The embedding model changed. An administrator has to rebuild the index.",
                statusCode: StatusCodes.Status409Conflict);
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Only jobs the user may read (own and shared, ADR 0007) can produce hits: the filter is applied inside the vector search
        var userId = JobAccessService.GetUserId(http.User) ?? string.Empty;
        var allowed = await access.VisibleJobIds(userId).ToListAsync(http.RequestAborted);
        if (allowed.Count == 0 || status == IndexStatus.NotBuilt)
            return TypedResults.Ok(new PaginatedResult<SearchHitDto>([], 0, page, pageSize));

        var vector = (await embeddings.EmbedAsync([q], http.RequestAborted))[0];

        if (!hybrid)
        {
            var hits = await index.SearchAsync(vector, allowed, pageSize + 1, (page - 1) * pageSize, http.RequestAborted);
            var hasMore = hits.Count > pageSize;
            var pageHits = hits.Take(pageSize).ToList();
            var names = await FileNamesAsync(dbContext, pageHits.Select(h => h.JobId), http.RequestAborted);
            var items = pageHits.Select(h => ToDto(h, names)).ToList();
            // The vector search has no total; report enough for the pager to know whether another page follows
            var total = (page - 1) * pageSize + items.Count + (hasMore ? 1 : 0);
            return TypedResults.Ok(new PaginatedResult<SearchHitDto>(items, total, page, pageSize));
        }

        var semantic = await index.SearchAsync(vector, allowed, FusionDepth, 0, http.RequestAborted);
        var visibleJobs = dbContext.AudioJobs.Where(j => allowed.Contains(j.Id));
        var fullText = await SearchEndpoints.FullTextHitsAsync(dbContext, visibleJobs, q);

        var fused = ReciprocalRankFusion.Fuse(
            fullText.Take(FusionDepth).Select(h => new RankedHit(h.JobId, h.SegmentStartMs, h.SegmentStartMs, h.Text)).ToList(),
            semantic.Select(h => new RankedHit(h.JobId, h.StartMs, h.EndMs, h.Text)).ToList());

        var fusedNames = await FileNamesAsync(dbContext, fused.Select(f => f.JobId), http.RequestAborted);
        var fusedItems = fused.Skip((page - 1) * pageSize).Take(pageSize).Select(f =>
        {
            if (f.FullText is { } text)
            {
                var snippet = SearchSnippetBuilder.Build(text.Text, q);
                return new SearchHitDto(f.JobId, fusedNames.GetValueOrDefault(f.JobId, string.Empty), snippet.Text, snippet.HighlightStart, snippet.HighlightLength, text.StartMs);
            }
            var s = f.Semantic!;
            return new SearchHitDto(f.JobId, fusedNames.GetValueOrDefault(f.JobId, string.Empty), Shorten(s.Text), 0, 0, s.StartMs);
        }).ToList();
        return TypedResults.Ok(new PaginatedResult<SearchHitDto>(fusedItems, fused.Count, page, pageSize));
    }

    private static Results<Accepted<SemanticIndexStatusDto>, ProblemHttpResult> Reindex(IServiceProvider services)
    {
        var progress = services.GetService<ReindexProgress>();
        var queue = services.GetService<IndexingQueue>();
        if (services.GetService<IEmbeddingService>() is null || services.GetService<IVectorIndex>() is null || progress is null || queue is null)
        {
            return TypedResults.Problem(
                title: "Semantic search is not available",
                detail: "No embedding model or vector database is configured on this server.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        if (!progress.TryBegin())
        {
            return TypedResults.Problem(
                title: "A rebuild is already running",
                detail: "Wait until the running rebuild is finished.",
                statusCode: StatusCodes.Status409Conflict);
        }

        queue.EnqueueReindex();
        return TypedResults.Accepted("/api/admin/search/status", new SemanticIndexStatusDto("Rebuilding", true, 0, 0, null, null, null));
    }

    private static async Task<Ok<SemanticIndexStatusDto>> Status(IServiceProvider services)
    {
        var progress = services.GetService<ReindexProgress>();
        var embeddings = services.GetService<IEmbeddingService>();
        var indexer = services.GetService<SemanticIndexer>();
        if (embeddings is null || indexer is null || progress is null)
            return TypedResults.Ok(new SemanticIndexStatusDto("NotConfigured", false, 0, 0, null, null, null));

        var status = await indexer.GetStatusAsync();
        return TypedResults.Ok(new SemanticIndexStatusDto(
            status.ToString(), progress.Running, progress.Done, progress.Total,
            embeddings.Info.Provider, embeddings.Info.Model, embeddings.Info.Dimensions));
    }

    private static SearchHitDto ToDto(VectorHit hit, IReadOnlyDictionary<Guid, string> names) =>
        new(hit.JobId, names.GetValueOrDefault(hit.JobId, string.Empty), Shorten(hit.Text), 0, 0, hit.StartMs);

    private static string Shorten(string text) => text.Length <= SnippetLength ? text : text[..SnippetLength].TrimEnd() + "…";

    private static async Task<Dictionary<Guid, string>> FileNamesAsync(AppDbContext db, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.Distinct().ToList();
        return await db.AudioJobs.AsNoTracking().Where(j => wanted.Contains(j.Id)).ToDictionaryAsync(j => j.Id, j => j.FileName, cancellationToken);
    }
}
