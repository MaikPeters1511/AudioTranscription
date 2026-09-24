using AudioTranscription.Infrastructure.Services;
using FluentAssertions;

namespace AudioTranscription.Tests.Transcription;

public class KeyedAsyncCacheTests
{
    private sealed class FakeModel(string key) : IDisposable
    {
        public string Key { get; } = key;
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }

    private int _loads;

    private Task<FakeModel> Load(string key, CancellationToken _)
    {
        Interlocked.Increment(ref _loads);
        return Task.FromResult(new FakeModel(key));
    }

    [Fact]
    public async Task GetAsync_WithSameKey_LoadsOnceAndSharesTheValue()
    {
        await using var cache = new KeyedAsyncCache<string, FakeModel>(Load);

        var first = await cache.GetAsync("Base");
        var second = await cache.GetAsync("Base");

        second.Should().BeSameAs(first);
        _loads.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_WithDifferentKeys_LoadsOneValuePerKey()
    {
        await using var cache = new KeyedAsyncCache<string, FakeModel>(Load);

        var small = await cache.GetAsync("Small");
        var tiny = await cache.GetAsync("Tiny");

        small.Should().NotBeSameAs(tiny);
        small.Key.Should().Be("Small");
        tiny.Key.Should().Be("Tiny");
        _loads.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_WhenCalledConcurrently_LoadsOnce()
    {
        var release = new TaskCompletionSource();
        await using var cache = new KeyedAsyncCache<string, FakeModel>(async (key, ct) =>
        {
            Interlocked.Increment(ref _loads);
            await release.Task;
            return new FakeModel(key);
        });

        var calls = Enumerable.Range(0, 8).Select(_ => Task.Run(() => cache.GetAsync("Base"))).ToList();
        release.SetResult();
        var values = await Task.WhenAll(calls);

        values.Should().AllSatisfy(v => v.Should().BeSameAs(values[0]));
        _loads.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_WhenLoadFails_DoesNotCacheTheFailure()
    {
        var fail = true;
        await using var cache = new KeyedAsyncCache<string, FakeModel>((key, ct) =>
        {
            Interlocked.Increment(ref _loads);
            return fail
                ? Task.FromException<FakeModel>(new HttpRequestException("download failed"))
                : Task.FromResult(new FakeModel(key));
        });

        await FluentActions.Awaiting(() => cache.GetAsync("Base")).Should().ThrowAsync<HttpRequestException>();
        fail = false;
        var value = await cache.GetAsync("Base");

        value.Key.Should().Be("Base");
        _loads.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_WhenCallerCancels_KeepsLoadingForOtherCallers()
    {
        var release = new TaskCompletionSource();
        await using var cache = new KeyedAsyncCache<string, FakeModel>(async (key, ct) =>
        {
            Interlocked.Increment(ref _loads);
            await release.Task;
            return new FakeModel(key);
        });
        using var cancelled = new CancellationTokenSource();

        var cancelledCall = cache.GetAsync("Base", cancelled.Token);
        cancelled.Cancel();
        await FluentActions.Awaiting(() => cancelledCall).Should().ThrowAsync<OperationCanceledException>();

        var otherCall = cache.GetAsync("Base");
        release.SetResult();
        (await otherCall).Key.Should().Be("Base");
        _loads.Should().Be(1, "the shared load is not aborted by one caller");
    }

    [Fact]
    public async Task DisposeAsync_DisposesLoadedValuesAndCancelsRunningLoads()
    {
        CancellationToken loadToken = default;
        var cache = new KeyedAsyncCache<string, FakeModel>(async (key, ct) =>
        {
            if (key == "Slow")
            {
                loadToken = ct;
                await Task.Delay(Timeout.Infinite, ct);
            }
            return new FakeModel(key);
        });
        var loaded = await cache.GetAsync("Base");
        var running = cache.GetAsync("Slow");

        // Hangs if running loads are not cancelled
        await cache.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        loaded.IsDisposed.Should().BeTrue();
        loadToken.IsCancellationRequested.Should().BeTrue();
        await FluentActions.Awaiting(() => running).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => cache.GetAsync("Base")).Should().ThrowAsync<ObjectDisposedException>();
    }
}
