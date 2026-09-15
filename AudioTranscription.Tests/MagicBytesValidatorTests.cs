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
}
