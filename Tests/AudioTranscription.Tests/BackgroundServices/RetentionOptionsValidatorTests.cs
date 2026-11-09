using AudioTranscription.Api.Configuration;
using FluentAssertions;

namespace AudioTranscription.Tests.BackgroundServices;

/// <summary>S30: wrong retention settings stop the start with a clear message instead of misbehaving later.</summary>
public class RetentionOptionsValidatorTests
{
    private static bool IsValid(RetentionOptions options) => new RetentionOptionsValidator().Validate(null, options).Succeeded;

    [Fact]
    public void TheDefaults_AreValid() => IsValid(new RetentionOptions()).Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(36500)]
    public void DaysWithinTheRange_AreValid(int days)
    {
        IsValid(new RetentionOptions { TranscriptDays = days, AudioDays = days }).Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(36501)]
    [InlineData(int.MaxValue)]
    public void DaysOutsideTheRange_AreRejected(int days)
    {
        IsValid(new RetentionOptions { TranscriptDays = days }).Should().BeFalse();
        IsValid(new RetentionOptions { AudioDays = days }).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(169)]
    [InlineData(int.MaxValue)]
    public void AnIntervalOutsideOneToOneWeek_IsRejected(int hours)
    {
        IsValid(new RetentionOptions { RunIntervalHours = hours }).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_001)]
    public void AJobLimitOutsideTheRange_IsRejected(int limit)
    {
        IsValid(new RetentionOptions { MaxJobsPerRun = limit }).Should().BeFalse();
    }

    [Fact]
    public void TheMessage_NamesTheSetting()
    {
        var result = new RetentionOptionsValidator().Validate(null, new RetentionOptions { TranscriptDays = -1, RunIntervalHours = 0 });

        result.FailureMessage.Should().Contain("TranscriptDays").And.Contain("RunIntervalHours");
    }
}
