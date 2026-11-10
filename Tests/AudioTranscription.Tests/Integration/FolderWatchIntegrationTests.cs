using AudioTranscription.Api.Automation;
using AudioTranscription.Api.BackgroundServices;
using AudioTranscription.Api.Storage;
using AudioTranscription.Domain.Automation;
using AudioTranscription.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AudioTranscription.Tests.Integration;

/// <summary>S28-T4 (ADR 0009): a watched folder turns dropped files into jobs of the target user.</summary>
public class FolderWatchIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly byte[] Mp3 = [0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x15, 0x01, 0x02, 0x03, 0x04];

    private readonly TempDirectory _watch = new();
    private readonly TempDirectory _outside = new();
    private readonly AutomationHost _host;
    private readonly WebApplicationFactory<Program> _baseFactory;

    public FolderWatchIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
        _host = new AutomationHost(factory, configure: services =>
        {
            services.Configure<WatchOptions>(o =>
            {
                o.StableSeconds = 5;
                o.Folders = [new WatchFolderOptions { Path = _watch.Path, User = AutomationHost.AliceEmail }];
            });
            services.Configure<AudioTranscription.Api.Configuration.UploadOptions>(o => o.MaxFileSizeBytes = 1000);
        });
    }

    public void Dispose()
    {
        _host.Dispose();
        _watch.Dispose();
        _outside.Dispose();
    }

    private string Drop(string name, byte[]? content = null, TimeSpan? age = null, string? folder = null)
    {
        var path = Path.Combine(folder ?? _watch.Path, name);
        File.WriteAllBytes(path, content ?? Mp3);
        File.SetLastWriteTimeUtc(path, _host.Clock.GetUtcNow().UtcDateTime - (age ?? TimeSpan.FromMinutes(1)));
        return path;
    }

    private async Task<int> ScanAsync()
    {
        using var scope = _host.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FolderWatchProcessor>().ScanOnceAsync(CancellationToken.None);
    }

    private string[] In(string subfolder) =>
        Directory.Exists(Path.Combine(_watch.Path, subfolder))
            ? [.. Directory.GetFiles(Path.Combine(_watch.Path, subfolder)).Select(Path.GetFileName)!]
            : [];

    private int Queued => _host.Factory.Services.GetRequiredService<TranscriptionQueue>().Count;

    [Fact]
    public async Task ADroppedFile_BecomesAJobOfTheTargetUser_AndIsMovedAway()
    {
        var path = Drop("Besprechung.mp3");

        var imported = await ScanAsync();

        imported.Should().Be(1);
        var job = await _host.WithDbAsync(db => db.AudioJobs.SingleAsync());
        job.OwnerId.Should().Be(_host.Alice);
        job.FileName.Should().Be("Besprechung.mp3");
        job.Status.Should().Be(AudioJobStatus.Pending);
        job.ContentType.Should().Be("audio/mpeg");
        job.FileSizeBytes.Should().Be(Mp3.Length);
        File.Exists(path).Should().BeFalse();
        In("processed").Should().ContainSingle().Which.Should().EndWith("Besprechung.mp3");
        var tempStore = _host.Factory.Services.GetRequiredService<ITempFileStore>();
        File.ReadAllBytes(tempStore.GetUploadPath(job.Id, job.FileName)).Should().Equal(Mp3);
        Queued.Should().Be(1);
    }

    [Fact]
    public async Task TheJobIsOnlyVisibleToItsOwner()
    {
        Drop("a.mp3");
        await ScanAsync();
        var job = await _host.WithDbAsync(db => db.AudioJobs.SingleAsync());

        (await _host.As(_host.Alice).GetAsync($"/api/audio-jobs/{job.Id}")).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        (await _host.As(_host.Bob).GetAsync($"/api/audio-jobs/{job.Id}")).StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AFileThatMightStillBeCopied_IsLeftAloneUntilItIsStable()
    {
        var path = Drop("big.mp3", age: TimeSpan.FromSeconds(2));

        (await ScanAsync()).Should().Be(0);
        File.Exists(path).Should().BeTrue();

        _host.Clock.Advance(TimeSpan.FromSeconds(10));
        (await ScanAsync()).Should().Be(1);
        File.Exists(path).Should().BeFalse();
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("program.exe")]
    [InlineData("noextension")]
    [InlineData("double.mp3.sh")]
    public async Task AFileOfADisallowedType_IsRejectedAndNeverBecomesAJob(string name)
    {
        var path = Drop(name);

        (await ScanAsync()).Should().Be(0);

        (await _host.WithDbAsync(db => db.AudioJobs.CountAsync())).Should().Be(0);
        File.Exists(path).Should().BeFalse();
        In("rejected").Should().ContainSingle();
        Queued.Should().Be(0);
    }

    [Fact]
    public async Task AFileWhoseContentIsNotAudio_IsRejectedDespiteItsExtension()
    {
        Drop("fake.mp3", "#!/bin/sh\nrm -rf /\n"u8.ToArray());

        (await ScanAsync()).Should().Be(0);

        (await _host.WithDbAsync(db => db.AudioJobs.CountAsync())).Should().Be(0);
        In("rejected").Should().ContainSingle();
    }

    [Fact]
    public async Task AFileOverTheSizeLimit_IsRejected()
    {
        Drop("huge.mp3", [.. Mp3, .. new byte[2000]]);

        (await ScanAsync()).Should().Be(0);

        (await _host.WithDbAsync(db => db.AudioJobs.CountAsync())).Should().Be(0);
        In("rejected").Should().ContainSingle();
    }

    [Fact]
    public async Task ASymbolicLink_IsNeverFollowed()
    {
        var secret = Path.Combine(_outside.Path, "secret.mp3");
        File.WriteAllBytes(secret, Mp3);
        var link = Path.Combine(_watch.Path, "link.mp3");
        File.CreateSymbolicLink(link, secret);

        (await ScanAsync()).Should().Be(0);

        (await _host.WithDbAsync(db => db.AudioJobs.CountAsync())).Should().Be(0);
        File.Exists(secret).Should().BeTrue("the target outside the folder must stay untouched");
        new FileInfo(link).LinkTarget.Should().NotBeNull("the link itself stays where it is");
        In("processed").Should().BeEmpty();
        In("rejected").Should().BeEmpty();
    }

    [Fact]
    public async Task SubfoldersAndHiddenFiles_AreIgnored()
    {
        Directory.CreateDirectory(Path.Combine(_watch.Path, "sub"));
        Drop("nested.mp3", folder: Path.Combine(_watch.Path, "sub"));
        Drop(".hidden.mp3");

        (await ScanAsync()).Should().Be(0);

        (await _host.WithDbAsync(db => db.AudioJobs.CountAsync())).Should().Be(0);
        File.Exists(Path.Combine(_watch.Path, "sub", "nested.mp3")).Should().BeTrue();
        File.Exists(Path.Combine(_watch.Path, ".hidden.mp3")).Should().BeTrue();
    }

    [Fact]
    public async Task APathLikeFileNameCannotLeaveTheFolder()
    {
        // On a real file system a name cannot contain a separator, but the stored name and every target path
        // are built from the last segment only
        Drop("evil..mp3");

        (await ScanAsync()).Should().Be(1);

        var job = await _host.WithDbAsync(db => db.AudioJobs.SingleAsync());
        job.FileName.Should().Be("evil..mp3");
        Directory.GetFiles(_watch.Path, "*", SearchOption.AllDirectories).Should().OnlyContain(f => f.StartsWith(_watch.Path));
        In("processed").Should().ContainSingle();
    }

    [Fact]
    public async Task ARestartAfterTheJobWasCreatedButBeforeTheMove_DoesNotProcessTheFileTwice()
    {
        var path = Drop("meeting.mp3");
        var modified = File.GetLastWriteTimeUtc(path);
        await ScanAsync();

        // The state after a crash: job exists, file is still in the folder (same name, size, time)
        File.WriteAllBytes(path, Mp3);
        File.SetLastWriteTimeUtc(path, modified);

        (await ScanAsync()).Should().Be(0);

        (await _host.WithDbAsync(db => db.AudioJobs.CountAsync())).Should().Be(1);
        Queued.Should().Be(1);
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task ScanningAgainWithoutNewFiles_DoesNothing()
    {
        Drop("a.mp3");
        await ScanAsync();

        (await ScanAsync()).Should().Be(0);

        (await _host.WithDbAsync(db => db.AudioJobs.CountAsync())).Should().Be(1);
    }

    [Fact]
    public async Task TwoFilesWithTheSameNameOverTime_BecomeTwoJobs()
    {
        Drop("same.mp3");
        await ScanAsync();
        var path = Drop("same.mp3", [.. Mp3, 9, 9, 9]);

        (await ScanAsync()).Should().Be(1);

        (await _host.WithDbAsync(db => db.AudioJobs.CountAsync())).Should().Be(2);
        In("processed").Should().HaveCount(2);
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task AnUnknownTargetUser_LeavesTheFilesUntouched()
    {
        var host = new AutomationHost(_baseFactory, configure: services => services.Configure<WatchOptions>(o =>
            o.Folders = [new WatchFolderOptions { Path = _watch.Path, User = "nobody@example.com" }]));
        var path = Drop("a.mp3");

        using var scope = host.Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<FolderWatchProcessor>().ScanOnceAsync(CancellationToken.None)).Should().Be(0);

        File.Exists(path).Should().BeTrue();
        (await host.WithDbAsync(db => db.AudioJobs.CountAsync())).Should().Be(0);
        host.Dispose();
    }

    [Fact]
    public async Task ADeactivatedTargetUser_LeavesTheFilesUntouched()
    {
        using (var scope = _host.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var alice = (await users.FindByIdAsync(_host.Alice))!;
            await users.SetLockoutEnabledAsync(alice, true);
            await users.SetLockoutEndDateAsync(alice, DateTimeOffset.MaxValue);
        }
        var path = Drop("a.mp3");

        (await ScanAsync()).Should().Be(0);

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task AMissingFolder_DoesNotStopTheOthers()
    {
        var other = new TempDirectory();
        var host = new AutomationHost(_baseFactory, configure: services => services.Configure<WatchOptions>(o =>
            o.Folders =
            [
                new WatchFolderOptions { Path = Path.Combine(_outside.Path, "does-not-exist"), User = AutomationHost.AliceEmail },
                new WatchFolderOptions { Path = other.Path, User = AutomationHost.BobEmail },
            ]));
        Drop("b.mp3", folder: other.Path);

        using var scope = host.Factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<FolderWatchProcessor>().ScanOnceAsync(CancellationToken.None)).Should().Be(1);

        (await host.WithDbAsync(db => db.AudioJobs.SingleAsync())).OwnerId.Should().Be(host.Bob);
        host.Dispose();
        other.Dispose();
    }

    [Fact]
    public async Task TheStoredFileNameIsOnlyTheNameOfTheFile()
    {
        Drop("Kundengespräch Müller (final).MP3");

        await ScanAsync();

        var job = await _host.WithDbAsync(db => db.AudioJobs.SingleAsync());
        job.FileName.Should().Be("Kundengespräch Müller (final).MP3");
        job.Id.Should().Be(WatchFileRules.JobIdFor(
            Path.GetFullPath(_watch.Path), "Kundengespräch Müller (final).MP3", Mp3.Length,
            (_host.Clock.GetUtcNow().UtcDateTime - TimeSpan.FromMinutes(1)).Ticks));
    }
}

public class WatchOptionsValidatorTests
{
    private static string Absolute => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "watch"));

    private static WatchOptions Valid() => new() { Folders = [new WatchFolderOptions { Path = Absolute, User = "a@example.com" }] };

    private static ValidateOptionsResult Validate(WatchOptions options) => new WatchOptionsValidator().Validate(null, options);

    [Fact]
    public void AValidConfiguration_Passes() => Validate(Valid()).Succeeded.Should().BeTrue();

    [Fact]
    public void NoFoldersAtAll_IsFine() => Validate(new WatchOptions()).Succeeded.Should().BeTrue();

    [Theory]
    [InlineData("relative/path")]
    [InlineData("../up")]
    [InlineData("")]
    public void ARelativeOrEmptyPath_IsRejected(string path)
    {
        var options = Valid();
        options.Folders[0].Path = path;

        Validate(options).Failed.Should().BeTrue();
    }

    [Fact]
    public void AFolderWithoutUser_IsRejected()
    {
        var options = Valid();
        options.Folders[0].User = " ";

        Validate(options).Failed.Should().BeTrue();
    }

    [Fact]
    public void TheSameFolderTwice_IsRejected()
    {
        var options = Valid();
        options.Folders.Add(new WatchFolderOptions { Path = Absolute + Path.DirectorySeparatorChar, User = "b@example.com" });

        Validate(options).Failed.Should().BeTrue();
    }

    [Fact]
    public void ANestedFolder_IsRejected_BecauseItWouldBeImportedTwice()
    {
        var options = Valid();
        options.Folders.Add(new WatchFolderOptions { Path = Path.Combine(Absolute, "inner"), User = "b@example.com" });

        Validate(options).Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(10, -1)]
    public void NonsensicalIntervals_AreRejected(int poll, int stable)
    {
        var options = Valid();
        options.PollIntervalSeconds = poll;
        options.StableSeconds = stable;

        Validate(options).Failed.Should().BeTrue();
    }
}
