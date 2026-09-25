using AudioTranscription.Api.BackgroundServices;
using FluentAssertions;

namespace AudioTranscription.Tests.BackgroundServices;

public class JobCancellationRegistryTests
{
    private readonly JobCancellationRegistry _sut = new();

    [Fact]
    public void Cancel_TriggersTheTokenOfTheRunningJob()
    {
        var jobId = Guid.NewGuid();
        using var cts = _sut.Register(jobId, CancellationToken.None);

        _sut.Cancel(jobId).Should().BeTrue();

        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void Cancel_ForUnknownJob_ReturnsFalse()
    {
        _sut.Cancel(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void Register_LinksTheJobTokenToShutdown()
    {
        using var shutdown = new CancellationTokenSource();
        using var cts = _sut.Register(Guid.NewGuid(), shutdown.Token);

        shutdown.Cancel();

        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void Unregister_RemovesTheJob()
    {
        var jobId = Guid.NewGuid();
        using var cts = _sut.Register(jobId, CancellationToken.None);

        _sut.Unregister(jobId);

        _sut.Cancel(jobId).Should().BeFalse();
        cts.IsCancellationRequested.Should().BeFalse();
    }
}
