using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Search;
using AudioTranscription.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AudioTranscription.Api.Endpoints;

/// <summary>
/// Full-text search across raw transcripts, timed segments and post-processing variants (S13, ADR 0005).
/// </summary>
public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this WebApplication app)
    {
        app.MapGet("/api/search", Search)
            .WithName("Search")
            .WithSummary("Full-text search across all transcripts")
            .WithDescription("Full-text search across all transcripts, paginated");
    }

    private static async Task<Results<Ok<PaginatedResult<SearchHitDto>>, ValidationProblem>> Search(
        [FromQuery] string? q,
        AppDbContext dbContext,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["q"] = ["A search term is required."]
            });
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // FREETEXT treats the whole input as natural-language search terms (with German stemming, ADR
        // 0005): unlike CONTAINS, it has no operator syntax (AND, "phrase", *, FORMSOF, ...) for user
        // input to inject into, and no escaping is needed.
        var jobHits = await dbContext.AudioJobs
            .Where(j => j.RawTranscript != null && EF.Functions.FreeText(j.RawTranscript, q))
            .Select(j => new Hit(j.Id, j.FileName, j.RawTranscript!, null, j.CreatedAtUtc))
            .ToListAsync();

        var segmentHits = await dbContext.TranscriptSegments
            .Where(s => EF.Functions.FreeText(s.Text, q))
            .Join(dbContext.AudioJobs, s => s.AudioJobId, j => j.Id,
                (s, j) => new Hit(j.Id, j.FileName, s.Text, s.StartMs, j.CreatedAtUtc))
            .ToListAsync();

        var variantHits = await dbContext.TranscriptVariants
            .Where(v => v.Text != null && EF.Functions.FreeText(v.Text, q))
            .Join(dbContext.AudioJobs, v => v.AudioJobId, j => j.Id,
                (v, j) => new Hit(j.Id, j.FileName, v.Text!, null, j.CreatedAtUtc))
            .ToListAsync();

        var ordered = jobHits.Concat(segmentHits).Concat(variantHits)
            .OrderByDescending(h => h.CreatedAtUtc)
            .ToList();

        var totalCount = ordered.Count;
        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(h =>
            {
                var snippet = SearchSnippetBuilder.Build(h.Text, q);
                return new SearchHitDto(h.JobId, h.FileName, snippet.Text, snippet.HighlightStart, snippet.HighlightLength, h.SegmentStartMs);
            })
            .ToList();

        return TypedResults.Ok(new PaginatedResult<SearchHitDto>(items, totalCount, page, pageSize));
    }

    private record Hit(Guid JobId, string FileName, string Text, long? SegmentStartMs, DateTime CreatedAtUtc);
}
