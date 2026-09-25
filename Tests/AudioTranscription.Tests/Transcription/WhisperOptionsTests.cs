using AudioTranscription.Infrastructure.Services;
using FluentAssertions;

namespace AudioTranscription.Tests.Transcription;

public class WhisperOptionsTests
{
    private static WhisperOptions ValidOptions() => new()
    {
        DefaultModel = "Base",
        AllowedModels = ["Tiny", "Base", "Small"],
        SupportedLanguages = ["de", "en"]
    };

    private static bool IsValid(WhisperOptions options) =>
        new WhisperOptionsValidator().Validate(null, options).Succeeded;

    [Fact]
    public void Validate_WithValidOptions_Succeeds() =>
        IsValid(ValidOptions()).Should().BeTrue();

    [Theory]
    [InlineData("Huge")]      // no Whisper model
    [InlineData("Medium")]    // a Whisper model, but not allowed
    [InlineData("")]
    public void Validate_WithInvalidDefaultModel_Fails(string defaultModel)
    {
        var options = ValidOptions();
        options.DefaultModel = defaultModel;

        IsValid(options).Should().BeFalse();
    }

    [Fact]
    public void Validate_WithUnknownAllowedModel_Fails()
    {
        var options = ValidOptions();
        options.AllowedModels = ["Base", "Huge"];

        IsValid(options).Should().BeFalse();
    }

    [Fact]
    public void Validate_WithoutAllowedModels_Fails()
    {
        var options = ValidOptions();
        options.AllowedModels = [];

        IsValid(options).Should().BeFalse();
    }

    [Theory]
    [InlineData("german")]
    [InlineData("DE")]
    [InlineData("auto")]
    [InlineData("")]
    public void Validate_WithInvalidLanguageCode_Fails(string language)
    {
        var options = ValidOptions();
        options.SupportedLanguages = ["en", language];

        IsValid(options).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "Base")]
    [InlineData("", "Base")]
    [InlineData("small", "Small")]
    [InlineData("TINY", "Tiny")]
    public void TryResolveModel_WithAllowedOrMissingModel_ReturnsCanonicalName(string? requested, string expected)
    {
        ValidOptions().TryResolveModel(requested, out var model).Should().BeTrue();
        model.Should().Be(expected);
    }

    [Theory]
    [InlineData("Huge")]
    [InlineData("Medium")]    // a Whisper model, but not allowed
    public void TryResolveModel_WithUnknownModel_Fails(string requested) =>
        ValidOptions().TryResolveModel(requested, out _).Should().BeFalse();

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("auto", null)]
    [InlineData("AUTO", null)]
    [InlineData("de", "de")]
    [InlineData("EN", "en")]
    public void TryResolveLanguage_WithSupportedOrAutomatic_ReturnsCodeOrNullForDetection(string? requested, string? expected)
    {
        ValidOptions().TryResolveLanguage(requested, out var language).Should().BeTrue();
        language.Should().Be(expected);
    }

    [Theory]
    [InlineData("fr")]        // valid code, but not supported
    [InlineData("german")]
    public void TryResolveLanguage_WithUnsupportedLanguage_Fails(string requested) =>
        ValidOptions().TryResolveLanguage(requested, out _).Should().BeFalse();

    // --- runtime order (S15) ---------------------------------------------------

    [Fact]
    public void Validate_WithoutRuntimeOrder_Succeeds() =>
        IsValid(ValidOptions()).Should().BeTrue();

    [Theory]
    [InlineData("Cuda")]
    [InlineData("cuda")]
    [InlineData("Cuda12")]
    [InlineData("Vulkan")]
    [InlineData("CoreML")]
    [InlineData("OpenVino")]
    [InlineData("Cpu")]
    [InlineData("CpuNoAvx")]
    public void Validate_WithKnownRuntimeOrderEntries_Succeeds(string runtime)
    {
        var options = ValidOptions();
        options.RuntimeOrder = [runtime, "Cpu"];

        IsValid(options).Should().BeTrue();
    }

    [Theory]
    [InlineData("Metal")]     // not a Whisper.net.LibraryLoader.RuntimeLibrary value
    [InlineData("")]
    [InlineData("Cuda,Cpu")]  // Enum.TryParse would accept a comma-separated combination; must be rejected
    public void Validate_WithUnknownRuntimeOrderEntry_Fails(string runtime)
    {
        var options = ValidOptions();
        options.RuntimeOrder = [runtime];

        IsValid(options).Should().BeFalse();
    }

    [Theory]
    [InlineData("Cuda", true)]
    [InlineData("cpu", true)]
    [InlineData("CORE ML", false)]
    [InlineData("Metal", false)]
    [InlineData("", false)]
    public void TryParseRuntimeLibrary_ParsesKnownWhisperNetRuntimes(string name, bool expected) =>
        WhisperOptions.TryParseRuntimeLibrary(name, out _).Should().Be(expected);
}
