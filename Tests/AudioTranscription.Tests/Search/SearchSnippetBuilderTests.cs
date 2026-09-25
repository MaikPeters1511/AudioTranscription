using AudioTranscription.Domain.Search;
using FluentAssertions;

namespace AudioTranscription.Tests.Search;

public class SearchSnippetBuilderTests
{
    [Fact]
    public void Build_WithMatchInShortText_ReturnsWholeTextWithoutEllipsis()
    {
        var snippet = SearchSnippetBuilder.Build("Der Zauberer wanderte durch den Wald.", "Zauberer");

        snippet.Text.Should().Be("Der Zauberer wanderte durch den Wald.");
        snippet.HighlightStart.Should().Be(4);
        snippet.HighlightLength.Should().Be(8);
    }

    [Fact]
    public void Build_IsCaseInsensitive()
    {
        var snippet = SearchSnippetBuilder.Build("Der Zauberer wanderte.", "zauberer");

        snippet.HighlightStart.Should().Be(4);
        snippet.HighlightLength.Should().Be(8);
    }

    [Fact]
    public void Build_WithMatchFarIntoLongText_AddsLeadingEllipsisAndAdjustsOffset()
    {
        var prefix = new string('x', 200);
        var text = prefix + " Kobold hinter dem Baum.";

        var snippet = SearchSnippetBuilder.Build(text, "Kobold");

        snippet.Text.Should().StartWith("…");
        snippet.Text.Should().Contain("Kobold");
        snippet.Text[snippet.HighlightStart..(snippet.HighlightStart + snippet.HighlightLength)].Should().Be("Kobold");
    }

    [Fact]
    public void Build_WithMatchNearTheEnd_AddsTrailingEllipsis()
    {
        var suffix = new string('y', 200);
        var text = "Ein Kobold lauerte. " + suffix;

        var snippet = SearchSnippetBuilder.Build(text, "Kobold");

        snippet.Text.Should().EndWith("…");
    }

    [Fact]
    public void Build_TriesEachQueryWordUntilOneMatches()
    {
        var snippet = SearchSnippetBuilder.Build("Drachen bewachen die Höhle.", "Einhörner Drachen");

        snippet.HighlightLength.Should().Be(7);
        snippet.Text[snippet.HighlightStart..(snippet.HighlightStart + snippet.HighlightLength)].Should().Be("Drachen");
    }

    [Fact]
    public void Build_WithNoLiteralMatch_FallsBackToTheStartWithoutHighlight()
    {
        // FREETEXT can match a stemmed form ("spielen" -> "spielten") that never appears literally
        var snippet = SearchSnippetBuilder.Build("Die Kinder spielten im Garten.", "spielen");

        snippet.HighlightLength.Should().Be(0);
        snippet.Text.Should().StartWith("Die Kinder spielten");
    }

    [Fact]
    public void Build_WithVeryLongTextAndNoMatch_TruncatesWithEllipsis()
    {
        var text = new string('z', 500);

        var snippet = SearchSnippetBuilder.Build(text, "nichtvorhanden");

        snippet.Text.Should().EndWith("…");
        snippet.Text.Length.Should().BeLessThan(text.Length);
    }
}
