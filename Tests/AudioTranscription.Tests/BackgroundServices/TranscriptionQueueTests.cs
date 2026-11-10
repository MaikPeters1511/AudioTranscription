using AudioTranscription.Api.BackgroundServices;
using FluentAssertions;

namespace AudioTranscription.Tests.BackgroundServices;

/// <summary>S34-T1: the queue hands out higher priorities first and keeps FIFO order among equals.</summary>
public class TranscriptionQueueTests
{
    private static TranscriptionJobRequest Request(Guid id, int priority = 0) =>
        new(id, $"{id}.mp3", priority);

    private static async Task<List<Guid>> DrainAsync(TranscriptionQueue queue)
    {
        var ids = new List<Guid>();
        while (queue.TryRead(out var request))
            ids.Add(request.JobId);
        await Task.CompletedTask;
        return ids;
    }

    [Fact]
    public async Task Jobs_with_equal_priority_come_out_in_arrival_order()
    {
        var queue = new TranscriptionQueue();
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids)
            await queue.EnqueueAsync(Request(id));

        (await DrainAsync(queue)).Should().Equal(ids);
    }

    [Fact]
    public async Task Higher_priority_comes_out_first_and_ties_stay_fifo()
    {
        var queue = new TranscriptionQueue();
        var low1 = Guid.NewGuid();
        var low2 = Guid.NewGuid();
        var high1 = Guid.NewGuid();
        var high2 = Guid.NewGuid();
        await queue.EnqueueAsync(Request(low1));
        await queue.EnqueueAsync(Request(high1, 5));
        await queue.EnqueueAsync(Request(low2));
        await queue.EnqueueAsync(Request(high2, 5));

        (await DrainAsync(queue)).Should().Equal(high1, high2, low1, low2);
    }

    [Fact]
    public async Task Changing_the_priority_of_a_waiting_job_reorders_it()
    {
        var queue = new TranscriptionQueue();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        await queue.EnqueueAsync(Request(first));
        await queue.EnqueueAsync(Request(second));
        await queue.EnqueueAsync(Request(third));

        queue.SetPriority(third, 3).Should().BeTrue();

        (await DrainAsync(queue)).Should().Equal(third, first, second);
    }

    [Fact]
    public async Task Lowering_the_priority_again_restores_the_arrival_order()
    {
        var queue = new TranscriptionQueue();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await queue.EnqueueAsync(Request(first));
        await queue.EnqueueAsync(Request(second));

        queue.SetPriority(second, 9);
        queue.SetPriority(second, 0);

        (await DrainAsync(queue)).Should().Equal(first, second);
    }

    [Fact]
    public void Setting_the_priority_of_an_unknown_job_reports_false()
    {
        new TranscriptionQueue().SetPriority(Guid.NewGuid(), 1).Should().BeFalse();
    }

    [Fact]
    public async Task Snapshot_lists_the_waiting_jobs_in_processing_order_with_their_priority()
    {
        var queue = new TranscriptionQueue();
        var low = Guid.NewGuid();
        var high = Guid.NewGuid();
        await queue.EnqueueAsync(Request(low));
        await queue.EnqueueAsync(Request(high, 2));

        var snapshot = queue.Snapshot();

        snapshot.Select(e => e.JobId).Should().Equal(high, low);
        snapshot.Select(e => e.Priority).Should().Equal(2, 0);
    }

    [Fact]
    public async Task Count_follows_enqueue_and_read()
    {
        var queue = new TranscriptionQueue();
        await queue.EnqueueAsync(Request(Guid.NewGuid()));
        await queue.EnqueueAsync(Request(Guid.NewGuid()));
        queue.Count.Should().Be(2);

        queue.TryRead(out _).Should().BeTrue();

        queue.Count.Should().Be(1);
    }

    [Fact]
    public async Task ReadAllAsync_waits_for_a_job_and_returns_the_highest_priority_one()
    {
        var queue = new TranscriptionQueue();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reader = Task.Run(async () =>
        {
            await foreach (var request in queue.ReadAllAsync(cts.Token))
                return request.JobId;
            return Guid.Empty;
        });
        await Task.Delay(50);
        var high = Guid.NewGuid();
        await queue.EnqueueAsync(Request(high, 7));

        (await reader).Should().Be(high);
    }

    [Fact]
    public async Task ReadAllAsync_ends_with_cancellation_when_the_queue_is_empty()
    {
        var queue = new TranscriptionQueue();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = async () =>
        {
            await foreach (var _ in queue.ReadAllAsync(cts.Token)) { }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
