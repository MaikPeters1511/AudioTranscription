using AudioTranscription.Api.Validation;
using FluentAssertions;

namespace AudioTranscription.Tests.Validation;

public class MagicBytesValidatorTests
{
    [Fact]
    public void IsValid_WithValidMp3Bytes_ReturnsTrue()
    {
        // Arrange
        var mp3MagicBytes = "ID3"u8.ToArray();
        using var stream = new MemoryStream(mp3MagicBytes);

        // Act
        var result = MagicBytesValidator.IsValid("audio/mpeg", stream);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithInvalidBytes_ReturnsFalse()
    {
        // Arrange
        var randomBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        using var stream = new MemoryStream(randomBytes);

        // Act
        var result = MagicBytesValidator.IsValid("audio/mpeg", stream);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsValid_WithUnknownContentType_ReturnsFalse()
    {
        // Arrange
        var mp3MagicBytes = "ID3"u8.ToArray();
        using var stream = new MemoryStream(mp3MagicBytes);

        // Act
        var result = MagicBytesValidator.IsValid("audio/unknown", stream);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsValid_WithEbmlBytes_AcceptsAudioWebm()
    {
        // Arrange: EBML header, as MediaRecorder's audio/webm;codecs=opus produces (S12)
        var ebmlHeader = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x02, 0x03, 0x04 };
        using var stream = new MemoryStream(ebmlHeader);

        // Act
        var result = MagicBytesValidator.IsValid("audio/webm", stream);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithNonEbmlBytes_RejectsAudioWebm()
    {
        // Arrange
        var randomBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        using var stream = new MemoryStream(randomBytes);

        // Act
        var result = MagicBytesValidator.IsValid("audio/webm", stream);

        // Assert
        result.Should().BeFalse();
    }

    private static byte[] FtypBox(string brand) =>
        [0x00, 0x00, 0x00, 0x18, .. "ftyp"u8.ToArray(), .. System.Text.Encoding.ASCII.GetBytes(brand)];

    [Theory]
    [InlineData("video/mp4")]
    [InlineData("video/quicktime")]
    public void IsValid_WithFtypBox_AcceptsVideoContainer(string contentType)
    {
        // Arrange (S14): MP4/MOV both use an ftyp box at offset 4, like audio/mp4
        using var stream = new MemoryStream(FtypBox("isom"));

        // Act
        var result = MagicBytesValidator.IsValid(contentType, stream);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("video/mp4")]
    [InlineData("video/quicktime")]
    public void IsValid_WithoutFtypBox_RejectsVideoContainer(string contentType)
    {
        var randomBytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        using var stream = new MemoryStream(randomBytes);

        var result = MagicBytesValidator.IsValid(contentType, stream);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("video/webm")]
    [InlineData("video/x-matroska")]
    public void IsValid_WithEbmlBytes_AcceptsMatroskaBasedVideoContainer(string contentType)
    {
        // Arrange (S14): WebM and MKV are both Matroska-based, same EBML header as audio/webm
        var ebmlHeader = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x02, 0x03, 0x04 };
        using var stream = new MemoryStream(ebmlHeader);

        var result = MagicBytesValidator.IsValid(contentType, stream);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("video/webm")]
    [InlineData("video/x-matroska")]
    public void IsValid_WithNonEbmlBytes_RejectsMatroskaBasedVideoContainer(string contentType)
    {
        var randomBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        using var stream = new MemoryStream(randomBytes);

        var result = MagicBytesValidator.IsValid(contentType, stream);

        result.Should().BeFalse();
    }
}
