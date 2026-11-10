using AudioTranscription.Infrastructure.Embeddings;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudioTranscription.Tests.Search;

/// <summary>EN-4: the embedding abstraction — batching, retries, dimension check, normalization.</summary>
public class EmbeddingServiceTests
{
    private static readonly EmbeddingModelInfo Info = new("Fake", "fake-model", 4);

    private sealed class FakeGenerator(Func<IList<string>, int, IEnumerable<float[]>> embed) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public int Calls;
        public List<int> BatchSizes { get; } = [];

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            var list = values.ToList();
            BatchSizes.Add(list.Count);
            var result = new GeneratedEmbeddings<Embedding<float>>(embed(list, ++Calls).Select(v => new Embedding<float>(v)));
            return Task.FromResult(result);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static GeneratorEmbeddingService Sut(FakeGenerator generator, int batchSize = 16) =>
        new(generator, Info, NullLogger<GeneratorEmbeddingService>.Instance, batchSize);

    [Fact]
    public async Task Embed_ReturnsOneNormalizedVectorPerText()
    {
        var generator = new FakeGenerator((texts, _) => texts.Select(_ => new float[] { 3, 0, 4, 0 }));

        var vectors = await Sut(generator).EmbedAsync(["a", "b"]);

        vectors.Should().HaveCount(2);
        vectors[0].Should().Equal(0.6f, 0f, 0.8f, 0f);
    }

    [Fact]
    public async Task Embed_SplitsLongListsIntoBatches_AndKeepsTheOrder()
    {
        var generator = new FakeGenerator((texts, _) => texts.Select(t => new float[] { int.Parse(t), 1, 0, 0 }));

        var vectors = await Sut(generator, batchSize: 3).EmbedAsync(Enumerable.Range(0, 7).Select(i => i.ToString()).ToList());

        generator.BatchSizes.Should().Equal(3, 3, 1);
        vectors.Select(v => (int)Math.Round(v[0] / v[1])).Should().Equal(Enumerable.Range(0, 7));
    }

    [Fact]
    public async Task Embed_NoTexts_MakesNoCall()
    {
        var generator = new FakeGenerator((texts, _) => []);

        (await Sut(generator).EmbedAsync([])).Should().BeEmpty();
        generator.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Embed_RetriesTransientFailures()
    {
        var generator = new FakeGenerator((texts, call) =>
            call == 1 ? throw new HttpRequestException("kurz weg") : texts.Select(_ => new float[] { 1, 0, 0, 0 }));

        var vectors = await Sut(generator).EmbedAsync(["a"]);

        vectors.Should().ContainSingle();
        generator.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Embed_GivesUpAfterTheRetries()
    {
        var generator = new FakeGenerator((_, _) => throw new HttpRequestException("weg"));

        var act = () => Sut(generator).EmbedAsync(["a"]);

        await act.Should().ThrowAsync<HttpRequestException>();
        generator.Calls.Should().Be(3, "one call and two retries");
    }

    [Fact]
    public async Task Embed_RejectsAVectorOfTheWrongDimension()
    {
        var generator = new FakeGenerator((texts, _) => texts.Select(_ => new float[] { 1, 0, 0 }));

        var act = () => Sut(generator).EmbedAsync(["a"]);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*dimension*");
    }

    [Fact]
    public async Task Embed_RejectsAnAnswerWithTheWrongNumberOfVectors()
    {
        var generator = new FakeGenerator((_, _) => [new float[] { 1, 0, 0, 0 }]);

        var act = () => Sut(generator).EmbedAsync(["a", "b"]);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Embed_NeverRetriesACancellation()
    {
        var generator = new FakeGenerator((_, _) => throw new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Sut(generator).EmbedAsync(["a"], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        generator.Calls.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Info_TellsProviderModelAndDimension() =>
        Sut(new FakeGenerator((_, _) => [])).Info.Should().Be(new EmbeddingModelInfo("Fake", "fake-model", 4));
}
