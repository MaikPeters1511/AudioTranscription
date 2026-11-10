using AudioTranscription.Api.Queue;
using FluentAssertions;

namespace AudioTranscription.Tests.Queue;

/// <summary>S34-T2: waiting time estimate from the average processing speed of finished jobs.</summary>
public class QueueEstimatorTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Speed_is_total_processing_seconds_per_total_byte()
    {
        var speed = QueueEstimator.SecondsPerByte(
        [
            new FinishedRun(1_000_000, TimeSpan.FromSeconds(10)),
            new FinishedRun(3_000_000, TimeSpan.FromSeconds(50))
        ]);

        speed.Should().BeApproximately(60.0 / 4_000_000, 1e-12);
    }

    [Fact]
    public void Speed_is_unknown_without_history()
    {
        QueueEstimator.SecondsPerByte([]).Should().BeNull();
    }

    [Fact]
    public void Runs_without_size_or_time_are_ignored()
    {
        QueueEstimator.SecondsPerByte(
        [
            new FinishedRun(0, TimeSpan.FromSeconds(10)),
            new FinishedRun(1_000, TimeSpan.Zero)
        ]).Should().BeNull();
    }

    [Fact]
    public void Wait_of_a_job_is_the_sum_of_everything_ahead_of_it()
    {
        // 1 s per 1000 bytes
        var estimates = QueueEstimator.Estimate(
            speed: 0.001,
            running: new RunningJob(5_000, Now.AddSeconds(-2)),
            waiting: [4_000, 2_000, 1_000],
            now: Now);

        // Running job: 5 s expected, 2 s elapsed -> 3 s left
        estimates.Running!.RemainingSeconds.Should().BeApproximately(3, 1e-9);
        estimates.Waiting.Select(w => w.WaitSeconds).Should().BeEquivalentTo(new[] { 3.0, 7.0, 9.0 });
        estimates.Waiting.Select(w => w.ProcessingSeconds).Should().BeEquivalentTo(new[] { 4.0, 2.0, 1.0 });
    }

    [Fact]
    public void An_overrunning_job_counts_as_finishing_now()
    {
        var estimates = QueueEstimator.Estimate(
            0.001, new RunningJob(1_000, Now.AddSeconds(-30)), [1_000], Now);

        estimates.Running!.RemainingSeconds.Should().Be(0);
        estimates.Waiting.Single().WaitSeconds.Should().Be(0);
    }

    [Fact]
    public void Without_a_running_job_the_first_waiting_job_has_no_wait()
    {
        var estimates = QueueEstimator.Estimate(0.001, null, [2_000, 1_000], Now);

        estimates.Running.Should().BeNull();
        estimates.Waiting.Select(w => w.WaitSeconds).Should().BeEquivalentTo(new[] { 0.0, 2.0 });
    }

    [Fact]
    public void Without_a_speed_there_are_no_estimates()
    {
        var estimates = QueueEstimator.Estimate(null, new RunningJob(1_000, Now), [1_000], Now);

        estimates.Running!.RemainingSeconds.Should().BeNull();
        estimates.Waiting.Single().WaitSeconds.Should().BeNull();
        estimates.Waiting.Single().ProcessingSeconds.Should().BeNull();
    }
}
