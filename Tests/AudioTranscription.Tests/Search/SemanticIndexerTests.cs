using AudioTranscription.Api.Search;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Embeddings;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudioTranscription.Tests.Search;

/// <summary>S23-T3: indexing a job, the state of the index and the rebuild.</summary>
public class SemanticIndexerTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeEmbeddingService _embeddings = new();
    private readonly InMemoryVectorIndex _index = new();

    private SemanticIndexer Sut() => new(_db, _embeddings, _index, NullLogger<SemanticIndexer>.Instance);

    private async Task<AudioJob> SeedJobAsync(string owner = "anna", AudioJobStatus status = AudioJobStatus.Completed, params string[] segmentTexts)
    {
        var job = new AudioJob { OwnerId = owner, FileName = "a.mp3", ContentType = "audio/mpeg", Model = "Base", Status = status };
        _db.AudioJobs.Add(job);
        for (var i = 0; i < segmentTexts.Length; i++)
            _db.TranscriptSegments.Add(new TranscriptSegment { AudioJobId = job.Id, Index = i, StartMs = i * 5_000L, EndMs = (i + 1) * 5_000L, Text = segmentTexts[i] });
        await _db.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task IndexJob_StoresTheChunksOfTheJob_WithItsOwner()
    {
        var job = await SeedJobAsync("anna", AudioJobStatus.Completed, "Wir sprechen über das Budget.", "Dann geht es um den Urlaub.");

        await Sut().IndexJobAsync(job.Id);

        var entry = _index.Entries.Should().ContainSingle().Subject;
        entry.JobId.Should().Be(job.Id);
        entry.OwnerId.Should().Be("anna");
        entry.Chunk.Text.Should().Contain("Budget").And.Contain("Urlaub");
        (entry.Chunk.StartMs, entry.Chunk.EndMs).Should().Be((0L, 10_000L));
    }

    [Fact]
    public async Task IndexJob_UsesTheCorrectedText()
    {
        var job = await SeedJobAsync("anna", AudioJobStatus.Completed, "Wir sprechen über das Wetter.");
        (await _db.TranscriptSegments.SingleAsync()).CurrentText = "Wir sprechen über die Finanzen.";
        await _db.SaveChangesAsync();

        await Sut().IndexJobAsync(job.Id);

        _index.Entries.Single().Chunk.Text.Should().Contain("Finanzen").And.NotContain("Wetter");
    }

    [Fact]
    public async Task IndexJob_AgainAfterACorrection_ReplacesTheOldChunks()
    {
        var job = await SeedJobAsync("anna", AudioJobStatus.Completed, "Kurzer Satz.");
        await Sut().IndexJobAsync(job.Id);
        (await _db.TranscriptSegments.SingleAsync()).CurrentText = "Anderer Satz.";
        await _db.SaveChangesAsync();

        await Sut().IndexJobAsync(job.Id);

        _index.Entries.Should().ContainSingle().Which.Chunk.Text.Should().Be("Anderer Satz.");
    }

    [Fact]
    public async Task IndexJob_OfAnUnfinishedOrUnknownJob_IndexesNothing()
    {
        var running = await SeedJobAsync("anna", AudioJobStatus.Processing, "Text");

        await Sut().IndexJobAsync(running.Id);
        await Sut().IndexJobAsync(Guid.NewGuid());

        _index.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task IndexJob_OfAJobWithoutSegments_RemovesWhatWasThere()
    {
        var job = await SeedJobAsync("anna", AudioJobStatus.Completed);
        _index.Entries.Add(new InMemoryVectorIndex.Entry(job.Id, "anna", new IndexedChunk(0, 0, 1, "alt", [1, 0, 0, 0, 0])));

        await Sut().IndexJobAsync(job.Id);

        _index.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task IndexJob_RecordsTheModelTheIndexWasBuiltWith()
    {
        var job = await SeedJobAsync("anna", AudioJobStatus.Completed, "Text");

        await Sut().IndexJobAsync(job.Id);

        var state = await _db.EmbeddingIndexStates.SingleAsync();
        (state.Provider, state.Model, state.Dimensions).Should().Be(("Fake", "fake-bow", FakeEmbeddingService.Concepts.Length));
        _index.Dimensions.Should().Be(FakeEmbeddingService.Concepts.Length);
    }

    [Fact]
    public async Task IndexJob_AfterAModelChange_RefusesInsteadOfMixingVectors()
    {
        var job = await SeedJobAsync("anna", AudioJobStatus.Completed, "Text");
        await Sut().IndexJobAsync(job.Id);
        _embeddings.Info = new EmbeddingModelInfo("Fake", "anderes-modell", 5);

        var act = () => Sut().IndexJobAsync(job.Id);

        await act.Should().ThrowAsync<SemanticIndexStaleException>();
        (await Sut().GetStatusAsync()).Should().Be(IndexStatus.Stale);
    }

    [Fact]
    public async Task Status_IsNotBuiltUntilSomethingWasIndexed_ThenReady()
    {
        (await Sut().GetStatusAsync()).Should().Be(IndexStatus.NotBuilt);
        var job = await SeedJobAsync("anna", AudioJobStatus.Completed, "Text");

        await Sut().IndexJobAsync(job.Id);

        (await Sut().GetStatusAsync()).Should().Be(IndexStatus.Ready);
    }

    [Fact]
    public async Task IndexJob_WhenTheEmbeddingServiceFails_ThrowsAndLeavesTheOldChunks()
    {
        var job = await SeedJobAsync("anna", AudioJobStatus.Completed, "Text");
        await Sut().IndexJobAsync(job.Id);
        _embeddings.Fail = true;

        var act = () => Sut().IndexJobAsync(job.Id);

        await act.Should().ThrowAsync<HttpRequestException>();
        _index.Entries.Should().ContainSingle("a failed run must not wipe the index of the job");
    }

    [Fact]
    public async Task Reindex_RebuildsEverythingWithTheCurrentModel_AndReportsProgress()
    {
        var a = await SeedJobAsync("anna", AudioJobStatus.Completed, "Budget");
        var b = await SeedJobAsync("ben", AudioJobStatus.Completed, "Urlaub");
        await SeedJobAsync("ben", AudioJobStatus.Failed, "Fehler");
        await Sut().IndexJobAsync(a.Id);
        _embeddings.Info = new EmbeddingModelInfo("Fake", "neues-modell", FakeEmbeddingService.Concepts.Length);
        var progress = new List<(int Done, int Total)>();

        await Sut().ReindexAllAsync(p => progress.Add(p));

        _index.Recreated.Should().Be(1);
        _index.Entries.Select(e => e.JobId).Should().BeEquivalentTo([a.Id, b.Id]);
        (await _db.EmbeddingIndexStates.SingleAsync()).Model.Should().Be("neues-modell");
        (await Sut().GetStatusAsync()).Should().Be(IndexStatus.Ready);
        progress.Last().Should().Be((2, 2));
    }

    [Fact]
    public async Task Reindex_KeepsGoingWhenOneJobFails()
    {
        var a = await SeedJobAsync("anna", AudioJobStatus.Completed, "Budget");
        var b = await SeedJobAsync("anna", AudioJobStatus.Completed, "Urlaub");
        var calls = 0;
        var failing = new FailingOnceEmbedding(_embeddings, () => ++calls == 1);
        var sut = new SemanticIndexer(_db, failing, _index, NullLogger<SemanticIndexer>.Instance);

        await sut.ReindexAllAsync(_ => { });

        _index.Entries.Should().ContainSingle("one of two jobs failed, the other one is indexed");
    }

    private sealed class FailingOnceEmbedding(IEmbeddingService inner, Func<bool> failNow) : IEmbeddingService
    {
        public EmbeddingModelInfo Info => inner.Info;

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
            failNow() ? throw new HttpRequestException("einmal weg") : inner.EmbedAsync(texts, cancellationToken);
    }

    public void Dispose() => _db.Dispose();
}
