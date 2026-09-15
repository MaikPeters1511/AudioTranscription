using AudioTranscription.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudioTranscription.Tests.Services;

public class OllamaPostProcessorTests
{
    private readonly Mock<IChatClient> _chatClientMock;
    private readonly Mock<ILogger<OllamaPostProcessor>> _loggerMock;
    private readonly OllamaPostProcessor _sut;

    public OllamaPostProcessorTests()
    {
        _chatClientMock = new Mock<IChatClient>();
        _loggerMock = new Mock<ILogger<OllamaPostProcessor>>();
        _sut = new OllamaPostProcessor(_chatClientMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task ProcessAsync_WithValidTranscript_ReturnsCleanedText()
    {
        // Arrange
        var rawText = "uhm so this is a test";
        var cleanedText = "So this is a test.";

        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, cleanedText)));

        // Act
        var result = await _sut.ProcessAsync(rawText);

        // Assert
        result.Should().Be(cleanedText);
    }

    [Fact]
    public async Task ProcessAsync_WhenChatClientThrows_ReturnsRawText()
    {
        // Arrange
        var rawText = "test";
        _chatClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Ollama is offline"));

        // Act
        var result = await _sut.ProcessAsync(rawText);

        // Assert
        result.Should().Be(rawText);
    }
}
