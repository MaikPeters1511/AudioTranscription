using AudioTranscription.Api.Search;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using AudioTranscription.Infrastructure.Embeddings;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudioTranscription.Tests.Search;

/// <summary>A fact that only runs when an environment variable is set; otherwise it is skipped with the reason.</summary>
public sealed class FactIfEnvironmentAttribute : FactAttribute
{
    public FactIfEnvironmentAttribute(string variable)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
            Skip = $"Set {variable} (e.g. http://localhost:11434, an Ollama server with the model bge-m3) to run the evaluation with a real embedding model.";
    }
}

/// <summary>
/// S23 evaluation set (DoD): searches with synonyms must find the expected passage with the real, local embedding model.
/// The CI has no model, so this runs only on a machine with Ollama and <c>EMBEDDING_EVAL_OLLAMA_URL</c> set.
/// </summary>
public class SemanticEvaluationTests
{
    private static readonly (string Query, string Expected)[] Cases =
    [
        ("Wann ging es ums Geld?", "In der Budgetrunde haben wir über die Kosten und den Etat gesprochen."),
        ("Wohin fahren wir in den Ferien?", "Für den Sommerurlaub planen wir eine Reise nach Italien."),
        ("Gab es Probleme mit der IT?", "Der Server ist gestern abgestürzt, die Software muss neu installiert werden."),
        ("Was gibt es zum Mittag?", "Zum Mittagessen bestellen wir Pizza für das ganze Team."),
        ("Wie wird das Wetter?", "Morgen soll es regnen, die Temperatur fällt auf acht Grad."),
    ];

    [FactIfEnvironment("EMBEDDING_EVAL_OLLAMA_URL")]
    public async Task ExampleSearchesWithSynonyms_FindTheExpectedPassage()
    {
        var generator = new OllamaEmbeddingGenerator(new Uri(Environment.GetEnvironmentVariable("EMBEDDING_EVAL_OLLAMA_URL")!), "bge-m3");
        var embeddings = new GeneratorEmbeddingService(generator, new EmbeddingModelInfo("Ollama", "bge-m3", 1024), NullLogger<GeneratorEmbeddingService>.Instance);
        var index = new InMemoryVectorIndex();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var job = new AudioJob { OwnerId = "u", FileName = "a.mp3", ContentType = "audio/mpeg", Model = "Base", Status = AudioJobStatus.Completed };
        db.AudioJobs.Add(job);
        for (var i = 0; i < Cases.Length; i++)
            db.TranscriptSegments.Add(new TranscriptSegment { AudioJobId = job.Id, Index = i, StartMs = i * 120_000L, EndMs = i * 120_000L + 20_000, Text = Cases[i].Expected });
        await db.SaveChangesAsync();
        await new SemanticIndexer(db, embeddings, index, NullLogger<SemanticIndexer>.Instance).IndexJobAsync(job.Id);

        foreach (var (query, expected) in Cases)
        {
            var vector = (await embeddings.EmbedAsync([query]))[0];
            var hits = await index.SearchAsync(vector, [job.Id], limit: 1);
            hits[0].Text.Should().Contain(expected, $"the query '{query}' means that passage");
        }
    }
}
