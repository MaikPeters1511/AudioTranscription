using AudioTranscription.Infrastructure.Embeddings;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Qdrant.Client;
using Testcontainers.Qdrant;

namespace AudioTranscription.Tests.Search;

/// <summary>S23-T3: the vector index against a real Qdrant (Testcontainers).</summary>
public class QdrantVectorIndexTests : IAsyncLifetime
{
    private readonly QdrantContainer _container = new QdrantBuilder("qdrant/qdrant:v1.12.4").Build();
    private QdrantClient _client = null!;
    private QdrantVectorIndex _index = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _client = new QdrantClient(_container.Hostname, _container.GetMappedPublicPort(6334));
        _index = new QdrantVectorIndex(_client, NullLogger<QdrantVectorIndex>.Instance);
        await _index.EnsureCollectionAsync(3);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _container.DisposeAsync();
    }

    private static IndexedChunk Chunk(int index, float x, float y, float z, string text = "text") =>
        new(index, index * 1_000L, index * 1_000L + 900, text, [x, y, z]);

    [Fact]
    public async Task Search_FindsTheNearestChunk_WithItsTimeRange()
    {
        var job = Guid.NewGuid();
        await _index.ReplaceJobAsync(job, "owner", [Chunk(0, 1, 0, 0, "Geld"), Chunk(1, 0, 1, 0, "Wetter")]);

        var hits = await _index.SearchAsync([0.9f, 0.1f, 0], [job], limit: 5);

        hits[0].Text.Should().Be("Geld");
        hits[0].JobId.Should().Be(job);
        (hits[0].StartMs, hits[0].EndMs).Should().Be((0L, 900L));
        hits.Should().HaveCount(2);
        hits[0].Score.Should().BeGreaterThan(hits[1].Score);
    }

    [Fact]
    public async Task Search_NeverReturnsJobsThatAreNotAllowed()
    {
        var mine = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        await _index.ReplaceJobAsync(mine, "me", [Chunk(0, 0, 1, 0, "meins")]);
        await _index.ReplaceJobAsync(foreign, "other", [Chunk(0, 1, 0, 0, "fremd")]);

        var hits = await _index.SearchAsync([1, 0, 0], [mine], limit: 10);

        hits.Select(h => h.JobId).Should().OnlyContain(id => id == mine);
        hits.Should().NotContain(h => h.Text == "fremd");
    }

    [Fact]
    public async Task Search_WithoutAllowedJobs_ReturnsNothing()
    {
        await _index.ReplaceJobAsync(Guid.NewGuid(), "me", [Chunk(0, 1, 0, 0)]);

        (await _index.SearchAsync([1, 0, 0], [], limit: 10)).Should().BeEmpty();
    }

    [Fact]
    public async Task Replace_OverwritesTheEarlierChunksOfAJob_InsteadOfDuplicatingThem()
    {
        var job = Guid.NewGuid();
        await _index.ReplaceJobAsync(job, "me", [Chunk(0, 1, 0, 0, "alt"), Chunk(1, 0, 1, 0, "alt zwei")]);

        await _index.ReplaceJobAsync(job, "me", [Chunk(0, 1, 0, 0, "neu")]);

        var hits = await _index.SearchAsync([1, 0, 0], [job], limit: 10);
        hits.Should().ContainSingle().Which.Text.Should().Be("neu");
    }

    [Fact]
    public async Task DeleteJob_RemovesItsChunks_AndOnlyTheirs()
    {
        var gone = Guid.NewGuid();
        var kept = Guid.NewGuid();
        await _index.ReplaceJobAsync(gone, "me", [Chunk(0, 1, 0, 0)]);
        await _index.ReplaceJobAsync(kept, "me", [Chunk(0, 1, 0, 0)]);

        await _index.DeleteJobAsync(gone);

        var hits = await _index.SearchAsync([1, 0, 0], [gone, kept], limit: 10);
        hits.Select(h => h.JobId).Should().Equal(kept);
    }

    [Fact]
    public async Task Search_Pages_ThroughLimitAndOffset()
    {
        var job = Guid.NewGuid();
        await _index.ReplaceJobAsync(job, "me", Enumerable.Range(0, 6).Select(i => Chunk(i, 1, i * 0.1f, 0)).ToList());

        var first = await _index.SearchAsync([1, 0, 0], [job], limit: 3, offset: 0);
        var second = await _index.SearchAsync([1, 0, 0], [job], limit: 3, offset: 3);

        first.Should().HaveCount(3);
        second.Should().HaveCount(3);
        first.Select(h => h.StartMs).Should().NotIntersectWith(second.Select(h => h.StartMs));
    }

    [Fact]
    public async Task Recreate_EmptiesTheIndex_AndAcceptsANewDimension()
    {
        var job = Guid.NewGuid();
        await _index.ReplaceJobAsync(job, "me", [Chunk(0, 1, 0, 0)]);

        await _index.RecreateCollectionAsync(2);
        await _index.ReplaceJobAsync(job, "me", [new IndexedChunk(0, 0, 900, "zwei", [1, 0])]);

        (await _index.SearchAsync([1, 0], [job], limit: 5)).Should().ContainSingle().Which.Text.Should().Be("zwei");
    }

    [Fact]
    public async Task IsHealthy_IsTrueForAReachableQdrant() =>
        (await _index.IsHealthyAsync()).Should().BeTrue();
}
