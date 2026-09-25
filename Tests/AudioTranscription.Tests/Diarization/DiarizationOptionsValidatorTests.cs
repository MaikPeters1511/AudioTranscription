using AudioTranscription.Infrastructure.Services;
using FluentAssertions;

namespace AudioTranscription.Tests.Diarization;

public class DiarizationOptionsValidatorTests
{
    private static DiarizationOptions Valid() => new()
    {
        Enabled = true,
        SegmentationModelPath = "models/segmentation.onnx",
        EmbeddingModelPath = "models/embedding.onnx",
        Threshold = 0.5f,
    };

    private static bool IsValid(DiarizationOptions options) =>
        new DiarizationOptionsValidator().Validate(null, options).Succeeded;

    [Fact]
    public void Validate_WithBothModelsConfigured_Succeeds() =>
        IsValid(Valid()).Should().BeTrue();

    [Fact]
    public void Validate_WhenDisabled_IgnoresMissingModelPaths() =>
        IsValid(new DiarizationOptions { Enabled = false }).Should().BeTrue();

    [Theory]
    [InlineData(null, "models/embedding.onnx")]
    [InlineData("", "models/embedding.onnx")]
    [InlineData("models/segmentation.onnx", null)]
    [InlineData("models/segmentation.onnx", "")]
    public void Validate_WhenEnabledWithoutBothModelPaths_Fails(string? segmentation, string? embedding)
    {
        var options = Valid();
        options.SegmentationModelPath = segmentation;
        options.EmbeddingModelPath = embedding;

        IsValid(options).Should().BeFalse();
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void Validate_WithThresholdOutsideZeroToOne_Fails(float threshold)
    {
        var options = Valid();
        options.Threshold = threshold;

        IsValid(options).Should().BeFalse();
    }

    [Fact]
    public void Validate_WithNonPositiveNumThreads_Fails()
    {
        var options = Valid();
        options.NumThreads = 0;

        IsValid(options).Should().BeFalse();
    }
}
