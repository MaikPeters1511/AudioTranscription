using AudioTranscription.Domain.Automation;
using FluentAssertions;

namespace AudioTranscription.Tests.Automation;

public class WatchFileRulesTests
{
    [Theory]
    [InlineData("a.mp3", "audio/mpeg")]
    [InlineData("A.MP3", "audio/mpeg")]
    [InlineData("a.wav", "audio/wav")]
    [InlineData("a.m4a", "audio/x-m4a")]
    [InlineData("a.ogg", "audio/ogg")]
    [InlineData("a.webm", "audio/webm")]
    [InlineData("a.mp4", "video/mp4")]
    [InlineData("a.mkv", "video/x-matroska")]
    [InlineData("a.mov", "video/quicktime")]
    public void ContentTypeFor_MapsAllowedExtensions(string name, string expected)
    {
        WatchFileRules.TryGetContentType(name, out var contentType).Should().BeTrue();
        contentType.Should().Be(expected);
    }

    [Theory]
    [InlineData("a.exe")]
    [InlineData("a.txt")]
    [InlineData("a")]
    [InlineData("mp3")]
    [InlineData(".mp3.exe")]
    [InlineData("a.mp3.sh")]
    public void ContentTypeFor_RejectsEverythingElse(string name)
    {
        WatchFileRules.TryGetContentType(name, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..\\..\\windows\\x.mp3", "x.mp3")]
    [InlineData("/abs/path/a.mp3", "a.mp3")]
    [InlineData("a\0b.mp3", "ab.mp3")]
    [InlineData("..", "file")]
    [InlineData(".", "file")]
    [InlineData("", "file")]
    [InlineData("   ", "file")]
    public void SafeFileName_KeepsOnlyTheLastSegmentWithoutControlCharacters(string input, string expected)
    {
        WatchFileRules.SafeFileName(input).Should().Be(expected);
    }

    [Fact]
    public void SafeFileName_ShortensVeryLongNamesButKeepsTheExtension()
    {
        var name = WatchFileRules.SafeFileName(new string('a', 400) + ".mp3");

        name.Length.Should().BeLessThanOrEqualTo(120);
        name.Should().EndWith(".mp3");
    }

    [Fact]
    public void JobIdFor_IsStableForTheSameFileAndDiffersOtherwise()
    {
        var id = WatchFileRules.JobIdFor("/in", "a.mp3", 100, 1234);

        WatchFileRules.JobIdFor("/in", "a.mp3", 100, 1234).Should().Be(id);
        WatchFileRules.JobIdFor("/in", "b.mp3", 100, 1234).Should().NotBe(id);
        WatchFileRules.JobIdFor("/in", "a.mp3", 101, 1234).Should().NotBe(id);
        WatchFileRules.JobIdFor("/in", "a.mp3", 100, 1235).Should().NotBe(id);
        WatchFileRules.JobIdFor("/other", "a.mp3", 100, 1234).Should().NotBe(id);
        id.Should().NotBe(Guid.Empty);
    }
}
