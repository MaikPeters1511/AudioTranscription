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
public class SemanticEvaluationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly string[] Passages =
    [
        "In der Budgetrunde haben wir über die Kosten und den Etat für das nächste Quartal gesprochen.",
        "Für den Sommerurlaub planen wir eine Reise nach Italien, die Flüge sind schon gebucht.",
        "Der Server ist gestern Nacht abgestürzt, die Software muss neu installiert werden.",
        "Zum Mittagessen bestellen wir Pizza für das ganze Team.",
        "Morgen soll es regnen, die Temperatur fällt auf acht Grad.",
        "Frau Schneider übernimmt die Leitung des Projekts ab dem ersten Dezember.",
        "Die Kundenzufriedenheit ist laut der Umfrage von 72 auf 81 Prozent gestiegen.",
        "Wir müssen den Vertrag mit dem Lieferanten bis Ende des Monats kündigen.",
        "Die neue Version der App wird nächste Woche im Store veröffentlicht.",
        "Bitte denkt daran, die Reisekosten bis Freitag einzureichen.",
        "Der Betriebsrat hat dem neuen Schichtmodell zugestimmt.",
        "Im Büro im dritten Stock fällt seit Montag die Heizung aus.",
        "Die Datenbank wurde auf die neue Version migriert, es gab keine Ausfälle.",
        "Herr Becker hat sich für die nächste Woche krankgemeldet.",
        "Wir haben beschlossen, die Konferenz im Frühjahr nach Hamburg zu verlegen.",
    ];

    /// <summary>Questions in other words than the passage, with the index of the passage that answers them.</summary>
    private static readonly (string Query, int Expected)[] Cases =
    [
        ("Wann ging es ums Geld?", 0), ("Wohin fahren wir in den Ferien?", 1), ("Gab es Probleme mit der IT?", 2),
        ("Was gibt es zum Mittag?", 3), ("Wie wird das Wetter?", 4), ("Wer leitet bald das Projekt?", 5),
        ("Sind die Kunden zufriedener geworden?", 6), ("Was ist mit dem Lieferantenvertrag?", 7),
        ("Wann erscheint das Update?", 8), ("Bis wann muss ich die Spesen abrechnen?", 9),
        ("Was hat die Arbeitnehmervertretung entschieden?", 10), ("Warum ist es im Büro kalt?", 11),
        ("Gab es Ausfallzeiten beim Datenbank-Umzug?", 12), ("Wer fehlt diese Woche?", 13), ("Wo findet die Tagung statt?", 14),
    ];

    /// <summary>Questions the material does not answer; the chat must not be given a passage for them.</summary>
    private static readonly string[] Unanswerable = ["Wie ist die Lage beim Fußball?", "Was kostet ein Flug nach Mars?"];

    [FactIfEnvironment("EMBEDDING_EVAL_OLLAMA_URL")]
    public async Task ExampleSearchesWithSynonyms_FindTheExpectedPassage_InTheTopThree()
    {
        var generator = new OllamaEmbeddingGenerator(new Uri(Environment.GetEnvironmentVariable("EMBEDDING_EVAL_OLLAMA_URL")!), "bge-m3");
        var embeddings = new GeneratorEmbeddingService(generator, new EmbeddingModelInfo("Ollama", "bge-m3", 1024), NullLogger<GeneratorEmbeddingService>.Instance);
        var index = new InMemoryVectorIndex();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var job = new AudioJob { OwnerId = "u", FileName = "a.mp3", ContentType = "audio/mpeg", Model = "Base", Status = AudioJobStatus.Completed };
        db.AudioJobs.Add(job);
        // One recording of contiguous 20 s segments: the index cuts it into ~45 s chunks that mix several topics, like real speech does
        for (var i = 0; i < Passages.Length; i++)
            db.TranscriptSegments.Add(new TranscriptSegment { AudioJobId = job.Id, Index = i, StartMs = i * 20_000L, EndMs = (i + 1) * 20_000L, Text = Passages[i] });
        await db.SaveChangesAsync();
        await new SemanticIndexer(db, embeddings, index, NullLogger<SemanticIndexer>.Instance).IndexJobAsync(job.Id);

        var rightScores = new List<float>();
        var topOne = 0;
        foreach (var (query, expected) in Cases)
        {
            var hits = await index.SearchAsync((await embeddings.EmbedAsync([query]))[0], [job.Id], limit: 3);
            var rank = hits.ToList().FindIndex(h => h.Text.Contains(Passages[expected]));
            output.WriteLine($"rank {rank + 1} score {hits[Math.Max(rank, 0)].Score:F3} {query}");
            rank.Should().BeGreaterThanOrEqualTo(0, $"the query '{query}' means a passage that has to be among the three best chunks");
            topOne += rank == 0 ? 1 : 0;
            rightScores.Add(hits[rank].Score);
        }

        var unrelated = new List<float>();
        foreach (var query in Unanswerable)
            unrelated.Add((await index.SearchAsync((await embeddings.EmbedAsync([query]))[0], [job.Id], limit: 1))[0].Score);
        // Documented, not asserted: the scores of right and unrelated chunks overlap, so the minimum score is only a noise cut-off (ADR 0011)
        output.WriteLine($"top-1 {topOne}/{Cases.Length}; right chunks {rightScores.Min():F3}..{rightScores.Max():F3}; best unrelated {unrelated.Max():F3}");
        topOne.Should().BeGreaterThanOrEqualTo(Cases.Length - 2, "almost every question finds its passage in the best chunk");
    }
}
