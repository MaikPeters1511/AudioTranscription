using AudioTranscription.Api.BackgroundServices;
using FluentAssertions;

namespace AudioTranscription.Tests.BackgroundServices;

/// <summary>S19-F1: the recipients of progress messages are looked up again from time to time, so a revoked share stops them.</summary>
public class ProgressRecipientsTests
{
    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }

    private readonly ManualTimeProvider _time = new();
    private int _lookups;
    private IReadOnlyList<string> _current = ["owner", "reader"];

    private ProgressRecipients Create() => new(
        _ =>
        {
            _lookups++;
            return Task.FromResult(_current);
        },
        _time, TimeSpan.FromSeconds(10), initial: ["owner", "reader"]);

    [Fact]
    public async Task Initially_UsesTheRecipientsItWasGiven_WithoutALookup()
    {
        var recipients = Create();

        (await recipients.GetAsync()).Should().Equal("owner", "reader");
        _lookups.Should().Be(0);
    }

    [Fact]
    public async Task WithinTheInterval_DoesNotLookAgain()
    {
        var recipients = Create();
        _time.Advance(TimeSpan.FromSeconds(5));

        await recipients.GetAsync();
        await recipients.GetAsync();

        _lookups.Should().Be(0);
    }

    [Fact]
    public async Task AfterTheInterval_LooksAgain_AndTheRevokedUserIsGone()
    {
        var recipients = Create();
        _current = ["owner"];
        _time.Advance(TimeSpan.FromSeconds(11));

        (await recipients.GetAsync()).Should().Equal("owner");
        _lookups.Should().Be(1);
    }

    [Fact]
    public async Task LooksUpOncePerInterval_NotOnEveryCall()
    {
        var recipients = Create();

        for (var i = 0; i < 5; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(11));
            await recipients.GetAsync();
            await recipients.GetAsync();
        }

        _lookups.Should().Be(5);
    }

    [Fact]
    public async Task WhenTheLookupFails_KeepsTheLastKnownRecipients_AndTriesAgainLater()
    {
        var fail = true;
        var calls = 0;
        var recipients = new ProgressRecipients(
            _ =>
            {
                calls++;
                return fail ? throw new InvalidOperationException("db weg") : Task.FromResult<IReadOnlyList<string>>(["owner"]);
            },
            _time, TimeSpan.FromSeconds(10), initial: ["owner", "reader"]);
        _time.Advance(TimeSpan.FromSeconds(11));

        (await recipients.GetAsync()).Should().Equal("owner", "reader");

        fail = false;
        _time.Advance(TimeSpan.FromSeconds(11));
        (await recipients.GetAsync()).Should().Equal("owner");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task ConcurrentCalls_ShareOneLookup()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<string>>();
        var calls = 0;
        var recipients = new ProgressRecipients(_ => { calls++; return gate.Task; }, _time, TimeSpan.FromSeconds(10), initial: ["owner"]);
        _time.Advance(TimeSpan.FromSeconds(11));

        var first = recipients.GetAsync();
        var second = recipients.GetAsync();
        gate.SetResult(["owner", "new"]);

        (await first).Should().Equal("owner", "new");
        (await second).Should().Equal("owner", "new");
        calls.Should().Be(1);
    }
}
