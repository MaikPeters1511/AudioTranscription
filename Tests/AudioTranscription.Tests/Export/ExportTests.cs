using System.Text;
using System.Text.Json;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Export;
using AudioTranscription.Infrastructure.Export;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;

namespace AudioTranscription.Tests.Export;

/// <summary>S21: document model, Markdown, JSON, DOCX and PDF exporters.</summary>
public class ExportTests
{
    private static readonly DateTime Created = new(2026, 11, 10, 8, 30, 0, DateTimeKind.Utc);
    private static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string> { [0] = "Anna", [1] = "Bernd" };

    private static List<TranscriptSegment> Segments() =>
    [
        new() { Index = 0, StartMs = 0, EndMs = 1_000, Text = "Hallo zusammen.", SpeakerIndex = 0 },
        new() { Index = 1, StartMs = 1_000, EndMs = 2_000, Text = "Willkommen.", SpeakerIndex = 0 },
        new() { Index = 2, StartMs = 65_000, EndMs = 66_000, Text = "Danke, schön hier!", SpeakerIndex = 1 },
    ];

    // --- document builder -------------------------------------------------------

    [Fact]
    public void Builder_MergesConsecutiveSegmentsOfTheSameSpeaker()
    {
        var doc = ExportDocumentBuilder.FromSegments("a", Created, Segments(), Names, new ExportOptions());

        doc.Blocks.Should().Equal(
            new ExportBlock("Anna", 0, "Hallo zusammen. Willkommen."),
            new ExportBlock("Bernd", 65_000, "Danke, schön hier!"));
    }

    [Fact]
    public void Builder_WithoutSpeakers_KeepsOneBlockPerSegment()
    {
        var doc = ExportDocumentBuilder.FromSegments("a", Created, Segments(), Names, new ExportOptions(Speakers: false));

        doc.Blocks.Should().HaveCount(3).And.OnlyContain(b => b.Speaker == null);
    }

    [Fact]
    public void Builder_WithoutTimestamps_OmitsStartTimes()
    {
        var doc = ExportDocumentBuilder.FromSegments("a", Created, Segments(), Names, new ExportOptions(Timestamps: false));

        doc.Blocks.Should().OnlyContain(b => b.StartMs == null);
    }

    [Fact]
    public void Builder_SkipsEmptySegments()
    {
        var segments = Segments();
        segments.Add(new TranscriptSegment { Index = 3, StartMs = 70_000, EndMs = 71_000, Text = "   " });

        ExportDocumentBuilder.FromSegments("a", Created, segments, Names, new ExportOptions()).Blocks.Should().HaveCount(2);
    }

    [Fact]
    public void Builder_VariantText_BecomesOneBlockPerParagraph()
    {
        var doc = ExportDocumentBuilder.FromVariantText("a", Created, "Summary", "Erster Absatz.\n\nZweiter Absatz.");

        doc.Variant.Should().Be("Summary");
        doc.Blocks.Select(b => b.Text).Should().Equal("Erster Absatz.", "Zweiter Absatz.");
        doc.Blocks.Should().OnlyContain(b => b.Speaker == null && b.StartMs == null);
    }

    // --- markdown ----------------------------------------------------------------

    [Fact]
    public void Markdown_MatchesGoldenFile()
    {
        var doc = ExportDocumentBuilder.FromSegments("Meeting", Created, Segments(), Names, new ExportOptions());

        var markdown = Encoding.UTF8.GetString(new MarkdownExporter().Export(doc));

        markdown.Should().Be(
            "# Meeting\n\n" +
            "*2026-11-10*\n\n" +
            "**Anna** `00:00:00`\n\nHallo zusammen. Willkommen.\n\n" +
            "**Bernd** `00:01:05`\n\nDanke, schön hier!\n");
    }

    [Fact]
    public void Markdown_WithoutSpeakersAndTimestamps_IsPlainParagraphs()
    {
        var doc = ExportDocumentBuilder.FromSegments("Meeting", Created, Segments(), Names, new ExportOptions(false, false));

        var markdown = Encoding.UTF8.GetString(new MarkdownExporter().Export(doc));

        markdown.Should().Be("# Meeting\n\n*2026-11-10*\n\nHallo zusammen.\n\nWillkommen.\n\nDanke, schön hier!\n");
    }

    [Fact]
    public void Markdown_EscapesMarkupInTextAndTitle()
    {
        var doc = new ExportDocument("A *b*", Created, null,
            [new ExportBlock(null, null, "# not a heading `code` [x](y) <b> a_b - 1. ")]);

        var markdown = Encoding.UTF8.GetString(new MarkdownExporter().Export(doc));

        markdown.Should().Contain("# A \\*b\\*");
        markdown.Should().Contain("\\# not a heading \\`code\\` \\[x\\](y) \\<b\\> a\\_b - 1.");
    }

    [Fact]
    public void Markdown_EscapesLeadingListMarkers()
    {
        var doc = new ExportDocument("t", Created, null, [new ExportBlock(null, null, "- eins"), new ExportBlock(null, null, "2. zwei")]);

        var markdown = Encoding.UTF8.GetString(new MarkdownExporter().Export(doc));

        markdown.Should().Contain("\n\\- eins\n").And.Contain("\n2\\. zwei\n");
    }

    [Fact]
    public void Markdown_VariantHasLabelInHeading()
    {
        var doc = ExportDocumentBuilder.FromVariantText("Meeting", Created, "Summary", "Text.");

        Encoding.UTF8.GetString(new MarkdownExporter().Export(doc)).Should().StartWith("# Meeting\n\n*2026-11-10 · Summary*\n");
    }

    // --- json --------------------------------------------------------------------

    [Fact]
    public void Json_HasStableVersionedSchema()
    {
        var doc = ExportDocumentBuilder.FromSegments("Meeting", Created, Segments(), Names, new ExportOptions());

        using var json = JsonDocument.Parse(new JsonExporter().Export(doc));
        var root = json.RootElement;

        root.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        root.GetProperty("title").GetString().Should().Be("Meeting");
        root.GetProperty("createdAtUtc").GetDateTime().Should().Be(Created);
        root.TryGetProperty("variant", out _).Should().BeFalse();
        var blocks = root.GetProperty("blocks");
        blocks.GetArrayLength().Should().Be(2);
        blocks[0].GetProperty("speaker").GetString().Should().Be("Anna");
        blocks[0].GetProperty("startMs").GetInt64().Should().Be(0);
        blocks[1].GetProperty("text").GetString().Should().Be("Danke, schön hier!");
    }

    [Fact]
    public void Json_OmitsDisabledFieldsAndKeepsUmlautsReadable()
    {
        var doc = ExportDocumentBuilder.FromSegments("Meeting", Created, Segments(), Names, new ExportOptions(false, false));

        var raw = Encoding.UTF8.GetString(new JsonExporter().Export(doc));

        raw.Should().Contain("schön").And.NotContain("speaker").And.NotContain("startMs");
    }

    // --- docx --------------------------------------------------------------------

    [Fact]
    public void Docx_CanBeReadBackWithHeadingsPerSpeakerChange()
    {
        var doc = ExportDocumentBuilder.FromSegments("Meeting", Created, Segments(), Names, new ExportOptions());

        using var stream = new MemoryStream(new DocxExporter().Export(doc));
        using var word = WordprocessingDocument.Open(stream, false);
        var paragraphs = word.MainDocumentPart!.Document.Body!.Elements<Paragraph>().ToList();
        var text = paragraphs.Select(p => p.InnerText).ToList();

        text.Should().Contain("Meeting").And.Contain("Hallo zusammen. Willkommen.").And.Contain("Danke, schön hier!");
        var headings = paragraphs
            .Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value is "Heading1" or "Heading2")
            .Select(p => p.InnerText).ToList();
        headings.Should().Equal("Meeting", "Anna (00:00:00)", "Bernd (00:01:05)");
    }

    [Fact]
    public void Docx_VariantWithoutSpeakers_HasNoSpeakerHeadings()
    {
        var doc = ExportDocumentBuilder.FromVariantText("Meeting", Created, "Summary", "Eins.\n\nZwei.");

        using var stream = new MemoryStream(new DocxExporter().Export(doc));
        using var word = WordprocessingDocument.Open(stream, false);
        var headings = word.MainDocumentPart!.Document.Body!.Elements<Paragraph>()
            .Where(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value == "Heading2").ToList();

        headings.Should().BeEmpty();
    }

    // --- pdf ---------------------------------------------------------------------

    [Fact]
    public void Pdf_IsValidAndSplitsLongTranscriptsAcrossPages()
    {
        var blocks = Enumerable.Range(0, 120)
            .Select(i => new ExportBlock("Anna", i * 1_000L, "Äpfel, Öl und Übermut – schöne Grüße " + i + ". " + string.Join(' ', Enumerable.Repeat("Wort", 40))))
            .ToList();
        var doc = new ExportDocument("Größe & Maß.mp3", Created, null, blocks);

        var bytes = new PdfExporter().Export(doc);

        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        CountPages(bytes).Should().BeGreaterThan(1);
    }

    [Fact]
    public void Pdf_ForSmallTranscript_HasOnePage()
    {
        var doc = ExportDocumentBuilder.FromSegments("Meeting", Created, Segments(), Names, new ExportOptions());

        CountPages(new PdfExporter().Export(doc)).Should().Be(1);
    }

    private static int CountPages(byte[] pdf) =>
        System.Text.RegularExpressions.Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b(?!s)").Count;
}
