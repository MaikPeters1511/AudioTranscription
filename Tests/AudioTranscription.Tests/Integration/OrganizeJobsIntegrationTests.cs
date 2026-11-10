using System.Net;
using System.Net.Http.Json;
using AudioTranscription.Api.Dtos;
using AudioTranscription.Domain.Entities;
using AudioTranscription.Domain.Enums;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AudioTranscription.Tests.Integration;

/// <summary>
/// S20: tags, folders and favorites. They are personal: every user has their own, also on shared jobs, and
/// nobody sees anyone else's.
/// </summary>
public class OrganizeJobsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private string _alice = null!;
    private string _bob = null!;
    private string _carol = null!;
    private const string BobEmail = "bob@example.com";

    public OrganizeJobsIntegrationTests(WebApplicationFactory<Program> factory)
    {
        var dbName = Guid.NewGuid().ToString();
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var dbDescriptors = services.Where(d =>
                d.ServiceType == typeof(AppDbContext) ||
                d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                (d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext))))
                .ToList();
            foreach (var d in dbDescriptors)
                services.Remove(d);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
            services.AddTestAuthentication();
        }));
        SeedUsersAsync().GetAwaiter().GetResult();
    }

    private async Task SeedUsersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        async Task<string> Create(string email)
        {
            var user = new IdentityUser { UserName = email, Email = email };
            (await users.CreateAsync(user, TestCredentials.NewPassword())).Succeeded.Should().BeTrue();
            return user.Id;
        }
        _alice = await Create("alice@example.com");
        _bob = await Create(BobEmail);
        _carol = await Create("carol@example.com");
    }

    private HttpClient As(string userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId);
        return client;
    }

    private async Task<Guid> AddJobAsync(string ownerId, string fileName = "meeting.mp3", params string[] sharedWith)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new AudioJob
        {
            FileName = fileName,
            ContentType = "audio/mpeg",
            Status = AudioJobStatus.Completed,
            OwnerId = ownerId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.AudioJobs.Add(job);
        foreach (var user in sharedWith)
            db.JobShares.Add(new JobShare { AudioJobId = job.Id, UserId = user });
        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task<TagDto> CreateTagAsync(string userId, string name, string? color = null)
    {
        var response = await As(userId).PostAsJsonAsync("/api/tags", new { name, color });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TagDto>())!;
    }

    private async Task<FolderDto> CreateFolderAsync(string userId, string name, Guid? parentId = null)
    {
        var response = await As(userId).PostAsJsonAsync("/api/folders", new { name, parentId });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<FolderDto>())!;
    }

    private async Task<List<AudioJobListDto>> ListAsync(string userId, string query = "")
    {
        var page = await As(userId).GetFromJsonAsync<PaginatedResult<AudioJobListDto>>($"/api/audio-jobs{query}");
        return page!.Items.ToList();
    }

    // ---------------------------------------------------------------- tags

    [Fact]
    public async Task Tags_CanBeCreatedAndListed_AndAreOnlyVisibleToTheirOwner()
    {
        var budget = await CreateTagAsync(_alice, "Budget", "#ff8800");
        await CreateTagAsync(_bob, "Privat");

        var alicesTags = await As(_alice).GetFromJsonAsync<List<TagDto>>("/api/tags");

        alicesTags.Should().ContainSingle().Which.Should().BeEquivalentTo(budget);
        budget.Color.Should().Be("#ff8800");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ATagWithoutAName_IsRejected(string name)
    {
        (await As(_alice).PostAsJsonAsync("/api/tags", new { name })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ATagNameIsLimitedToFiftyCharacters()
    {
        (await As(_alice).PostAsJsonAsync("/api/tags", new { name = new string('x', 51) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await As(_alice).PostAsJsonAsync("/api/tags", new { name = new string('x', 50) })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#fff")]
    [InlineData("#gggggg")]
    [InlineData("javascript:alert(1)")]
    public async Task ATagColorMustBeAHexColor_SoItCanNeverCarryMarkup(string color)
    {
        (await As(_alice).PostAsJsonAsync("/api/tags", new { name = "Bunt", color })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ATagNameIsUniquePerUser_IgnoringCase_ButOtherUsersMayUseIt()
    {
        await CreateTagAsync(_alice, "Budget");

        (await As(_alice).PostAsJsonAsync("/api/tags", new { name = "budget" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await As(_bob).PostAsJsonAsync("/api/tags", new { name = "Budget" })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task DeletingATag_RemovesItFromAllJobs_AndOnlyTheOwnerMayDeleteIt()
    {
        var job = await AddJobAsync(_alice);
        var tag = await CreateTagAsync(_alice, "Budget");
        await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{job}/tags", new { tagIds = new[] { tag.Id } });

        (await As(_bob).DeleteAsync($"/api/tags/{tag.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await As(_alice).DeleteAsync($"/api/tags/{tag.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ListAsync(_alice)).Single().Tags.Should().BeEmpty();
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().JobTags.AnyAsync()).Should().BeFalse();
    }

    // ---------------------------------------------------------------- tagging jobs

    [Fact]
    public async Task AJobCanBeTagged_AndTheTagsReplaceTheEarlierOnes()
    {
        var job = await AddJobAsync(_alice);
        var budget = await CreateTagAsync(_alice, "Budget");
        var urgent = await CreateTagAsync(_alice, "Dringend");

        (await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{job}/tags", new { tagIds = new[] { budget.Id, urgent.Id } })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListAsync(_alice)).Single().Tags!.Select(t => t.Name).Should().BeEquivalentTo(["Budget", "Dringend"]);

        await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{job}/tags", new { tagIds = new[] { urgent.Id } });
        (await ListAsync(_alice)).Single().Tags!.Select(t => t.Name).Should().Equal("Dringend");

        await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{job}/tags", new { tagIds = Array.Empty<Guid>() });
        (await ListAsync(_alice)).Single().Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyOwnTagsCanBeUsed()
    {
        var job = await AddJobAsync(_alice);
        var bobsTag = await CreateTagAsync(_bob, "Fremd");

        (await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{job}/tags", new { tagIds = new[] { bobsTag.Id } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{job}/tags", new { tagIds = new[] { Guid.NewGuid() } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TagsArePersonal_AReaderTagsASharedJobWithTheirOwnTags_WithoutTheOwnerSeeingThem()
    {
        var job = await AddJobAsync(_alice, sharedWith: _bob);
        var ownersTag = await CreateTagAsync(_alice, "Alice-Tag");
        var readersTag = await CreateTagAsync(_bob, "Bob-Tag");
        await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{job}/tags", new { tagIds = new[] { ownersTag.Id } });

        (await As(_bob).PutAsJsonAsync($"/api/audio-jobs/{job}/tags", new { tagIds = new[] { readersTag.Id } })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ListAsync(_alice)).Single().Tags!.Select(t => t.Name).Should().Equal("Alice-Tag");
        (await ListAsync(_bob)).Single().Tags!.Select(t => t.Name).Should().Equal("Bob-Tag");
    }

    [Fact]
    public async Task Tagging_FollowsTheAccessRules_ForeignJobsAreNotFound()
    {
        var job = await AddJobAsync(_alice);
        var tag = await CreateTagAsync(_carol, "Neugier");

        (await As(_carol).PutAsJsonAsync($"/api/audio-jobs/{job}/tags", new { tagIds = new[] { tag.Id } })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheJobListCanBeFilteredByTag_AndAForeignTagFindsNothing()
    {
        var tagged = await AddJobAsync(_alice, "tagged.mp3");
        await AddJobAsync(_alice, "plain.mp3");
        var tag = await CreateTagAsync(_alice, "Budget");
        await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{tagged}/tags", new { tagIds = new[] { tag.Id } });

        (await ListAsync(_alice, $"?tag={tag.Id}")).Select(j => j.Id).Should().Equal(tagged);
        (await ListAsync(_bob, $"?tag={tag.Id}")).Should().BeEmpty("a tag of someone else matches nothing and reveals nothing");
        (await ListAsync(_alice)).Should().HaveCount(2);
    }

    // ---------------------------------------------------------------- favorites

    [Fact]
    public async Task AJobCanBeMarkedAsFavorite_AndTheMarkIsRemovable_AndIdempotent()
    {
        var job = await AddJobAsync(_alice);

        (await As(_alice).PutAsync($"/api/audio-jobs/{job}/favorite", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await As(_alice).PutAsync($"/api/audio-jobs/{job}/favorite", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListAsync(_alice)).Single().IsFavorite.Should().BeTrue();

        (await As(_alice).DeleteAsync($"/api/audio-jobs/{job}/favorite")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await As(_alice).DeleteAsync($"/api/audio-jobs/{job}/favorite")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListAsync(_alice)).Single().IsFavorite.Should().BeFalse();
    }

    [Fact]
    public async Task Favorites_ArePersonal_AReaderMayFavoriteASharedJob_WithoutTheOwnerSeeingIt()
    {
        var job = await AddJobAsync(_alice, sharedWith: _bob);

        (await As(_bob).PutAsync($"/api/audio-jobs/{job}/favorite", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ListAsync(_bob)).Single().IsFavorite.Should().BeTrue();
        (await ListAsync(_alice)).Single().IsFavorite.Should().BeFalse();
    }

    [Fact]
    public async Task Favorites_OfAForeignJobAreNotFound()
    {
        var job = await AddJobAsync(_alice);

        (await As(_carol).PutAsync($"/api/audio-jobs/{job}/favorite", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await As(_carol).DeleteAsync($"/api/audio-jobs/{job}/favorite")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheJobListCanBeFilteredByFavorites()
    {
        var favorite = await AddJobAsync(_alice, "favorite.mp3");
        await AddJobAsync(_alice, "other.mp3");
        await As(_alice).PutAsync($"/api/audio-jobs/{favorite}/favorite", null);

        (await ListAsync(_alice, "?favorite=true")).Select(j => j.Id).Should().Equal(favorite);
        (await ListAsync(_alice, "?favorite=false")).Should().HaveCount(2, "false means no filter, not 'not favorite'");
    }

    // ---------------------------------------------------------------- folders

    [Fact]
    public async Task Folders_CanBeCreatedAsATree_AndAreOnlyVisibleToTheirOwner()
    {
        var projects = await CreateFolderAsync(_alice, "Projekte");
        var alpha = await CreateFolderAsync(_alice, "Alpha", projects.Id);
        await CreateFolderAsync(_bob, "Privat");

        var folders = await As(_alice).GetFromJsonAsync<List<FolderDto>>("/api/folders");

        folders.Should().BeEquivalentTo([projects, alpha]);
        alpha.ParentId.Should().Be(projects.Id);
    }

    [Fact]
    public async Task AFolderNeedsAValidNameAndAParentOfTheSameUser()
    {
        var bobsFolder = await CreateFolderAsync(_bob, "Bobs");

        (await As(_alice).PostAsJsonAsync("/api/folders", new { name = " " })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await As(_alice).PostAsJsonAsync("/api/folders", new { name = new string('x', 101) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await As(_alice).PostAsJsonAsync("/api/folders", new { name = "Unter Bob", parentId = bobsFolder.Id })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await As(_alice).PostAsJsonAsync("/api/folders", new { name = "Ins Nichts", parentId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AFolderNameIsUniqueWithinItsParent_ButMayRepeatElsewhere()
    {
        var a = await CreateFolderAsync(_alice, "A");
        var b = await CreateFolderAsync(_alice, "B");
        await CreateFolderAsync(_alice, "Notizen", a.Id);

        (await As(_alice).PostAsJsonAsync("/api/folders", new { name = "notizen", parentId = a.Id })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await As(_alice).PostAsJsonAsync("/api/folders", new { name = "Notizen", parentId = b.Id })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await As(_alice).PostAsJsonAsync("/api/folders", new { name = "a" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AFolderCanBeRenamedAndMoved()
    {
        var a = await CreateFolderAsync(_alice, "A");
        var b = await CreateFolderAsync(_alice, "B");

        (await As(_alice).PutAsJsonAsync($"/api/folders/{b.Id}", new { name = "Beta", parentId = a.Id })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var folders = await As(_alice).GetFromJsonAsync<List<FolderDto>>("/api/folders");
        folders!.Single(f => f.Id == b.Id).Should().Match<FolderDto>(f => f.Name == "Beta" && f.ParentId == a.Id);
    }

    [Fact]
    public async Task AFolderCannotBeMovedIntoItselfOrItsOwnSubfolder()
    {
        var a = await CreateFolderAsync(_alice, "A");
        var b = await CreateFolderAsync(_alice, "B", a.Id);
        var c = await CreateFolderAsync(_alice, "C", b.Id);

        (await As(_alice).PutAsJsonAsync($"/api/folders/{a.Id}", new { name = "A", parentId = a.Id })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await As(_alice).PutAsJsonAsync($"/api/folders/{a.Id}", new { name = "A", parentId = c.Id })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await As(_alice).PutAsJsonAsync($"/api/folders/{b.Id}", new { name = "B", parentId = c.Id })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await As(_alice).PutAsJsonAsync($"/api/folders/{c.Id}", new { name = "C", parentId = a.Id })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ForeignFolders_AreNotFound_ForRenamingAndDeleting()
    {
        var bobs = await CreateFolderAsync(_bob, "Bobs");

        (await As(_alice).PutAsJsonAsync($"/api/folders/{bobs.Id}", new { name = "Mein" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await As(_alice).DeleteAsync($"/api/folders/{bobs.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------- filing jobs

    [Fact]
    public async Task AJobCanBeFiledInAFolder_AndTakenOutAgain()
    {
        var job = await AddJobAsync(_alice);
        var folder = await CreateFolderAsync(_alice, "Projekte");

        (await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{job}/folder", new { folderId = folder.Id })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListAsync(_alice)).Single().FolderId.Should().Be(folder.Id);

        (await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{job}/folder", new { folderId = (Guid?)null })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListAsync(_alice)).Single().FolderId.Should().BeNull();
    }

    [Fact]
    public async Task OnlyOwnFoldersCanBeUsed_AndOnlyTheOwnerOfAJobFilesIt()
    {
        var job = await AddJobAsync(_alice, sharedWith: _bob);
        var bobsFolder = await CreateFolderAsync(_bob, "Bobs");
        var alicesFolder = await CreateFolderAsync(_alice, "Alices");

        (await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{job}/folder", new { folderId = bobsFolder.Id })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await As(_bob).PutAsJsonAsync($"/api/audio-jobs/{job}/folder", new { folderId = bobsFolder.Id })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a reader cannot file someone else's job");
        (await As(_carol).PutAsJsonAsync($"/api/audio-jobs/{job}/folder", new { folderId = alicesFolder.Id })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ASharedJobIsNotShownInTheReadersFolders_AndTheFolderIsNotRevealedToThem()
    {
        var job = await AddJobAsync(_alice, sharedWith: _bob);
        var folder = await CreateFolderAsync(_alice, "Geheim");
        await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{job}/folder", new { folderId = folder.Id });

        (await ListAsync(_bob)).Single().FolderId.Should().BeNull();
        (await ListAsync(_bob, $"?folder={folder.Id}")).Should().BeEmpty();
    }

    [Fact]
    public async Task TheJobListCanBeFilteredByFolder_ShowingOnlyJobsDirectlyInIt()
    {
        var inFolder = await AddJobAsync(_alice, "in.mp3");
        var inSub = await AddJobAsync(_alice, "sub.mp3");
        await AddJobAsync(_alice, "loose.mp3");
        var projects = await CreateFolderAsync(_alice, "Projekte");
        var alpha = await CreateFolderAsync(_alice, "Alpha", projects.Id);
        await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{inFolder}/folder", new { folderId = projects.Id });
        await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{inSub}/folder", new { folderId = alpha.Id });

        (await ListAsync(_alice, $"?folder={projects.Id}")).Select(j => j.Id).Should().Equal(inFolder);
        (await ListAsync(_alice, $"?folder={alpha.Id}")).Select(j => j.Id).Should().Equal(inSub);
    }

    [Fact]
    public async Task DeletingAFolder_MovesItsJobsToTheTopLevel_AndItsSubfoldersUpOneLevel()
    {
        var job = await AddJobAsync(_alice);
        var projects = await CreateFolderAsync(_alice, "Projekte");
        var alpha = await CreateFolderAsync(_alice, "Alpha", projects.Id);
        var top = await CreateFolderAsync(_alice, "Oben");
        await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{job}/folder", new { folderId = projects.Id });
        await As(_alice).PutAsJsonAsync($"/api/folders/{projects.Id}", new { name = "Projekte", parentId = top.Id });

        (await As(_alice).DeleteAsync($"/api/folders/{projects.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ListAsync(_alice)).Single().FolderId.Should().BeNull("the job moves to the top level");
        var folders = await As(_alice).GetFromJsonAsync<List<FolderDto>>("/api/folders");
        folders!.Select(f => f.Id).Should().BeEquivalentTo([top.Id, alpha.Id]);
        folders.Single(f => f.Id == alpha.Id).ParentId.Should().Be(top.Id, "the subfolder moves up to the deleted folder's parent");
    }

    [Fact]
    public async Task DeletingAFolder_RenamesAMovedSubfolderWhoseNameIsTakenOnTheNewLevel()
    {
        var projects = await CreateFolderAsync(_alice, "Projekte");
        await CreateFolderAsync(_alice, "Notizen"); // top level
        var innerNotes = await CreateFolderAsync(_alice, "Notizen", projects.Id);

        (await As(_alice).DeleteAsync($"/api/folders/{projects.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var folders = await As(_alice).GetFromJsonAsync<List<FolderDto>>("/api/folders");
        folders!.Select(f => f.Name).Should().BeEquivalentTo(["Notizen", "Notizen (2)"]);
        folders.Single(f => f.Id == innerNotes.Id).ParentId.Should().BeNull();
    }

    // ---------------------------------------------------------------- combined filters

    [Fact]
    public async Task FiltersCombine_AllConditionsMustHold()
    {
        var both = await AddJobAsync(_alice, "both.mp3");
        var onlyTag = await AddJobAsync(_alice, "tag.mp3");
        var onlyFavorite = await AddJobAsync(_alice, "fav.mp3");
        var tag = await CreateTagAsync(_alice, "Budget");
        var folder = await CreateFolderAsync(_alice, "Projekte");
        foreach (var job in new[] { both, onlyTag })
            await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{job}/tags", new { tagIds = new[] { tag.Id } });
        foreach (var job in new[] { both, onlyFavorite })
            await As(_alice).PutAsync($"/api/audio-jobs/{job}/favorite", null);
        await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{both}/folder", new { folderId = folder.Id });

        (await ListAsync(_alice, $"?tag={tag.Id}&favorite=true")).Select(j => j.Id).Should().Equal(both);
        (await ListAsync(_alice, $"?tag={tag.Id}&favorite=true&folder={folder.Id}")).Select(j => j.Id).Should().Equal(both);
        (await ListAsync(_alice, $"?tag={tag.Id}&folder={Guid.NewGuid()}")).Should().BeEmpty();
    }

    [Fact]
    public async Task TheJobDetail_ShowsTheUsersTagsFavoriteAndFolder()
    {
        var job = await AddJobAsync(_alice);
        var tag = await CreateTagAsync(_alice, "Budget");
        var folder = await CreateFolderAsync(_alice, "Projekte");
        await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{job}/tags", new { tagIds = new[] { tag.Id } });
        await As(_alice).PutAsync($"/api/audio-jobs/{job}/favorite", null);
        await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{job}/folder", new { folderId = folder.Id });

        var detail = await As(_alice).GetFromJsonAsync<AudioJobDto>($"/api/audio-jobs/{job}");

        detail!.Tags!.Select(t => t.Id).Should().Equal(tag.Id);
        detail.IsFavorite.Should().BeTrue();
        detail.FolderId.Should().Be(folder.Id);
    }

    [Fact]
    public async Task TheListShowsAJobExactlyOnce_EvenWithSeveralTags()
    {
        var job = await AddJobAsync(_alice);
        var tags = new[] { await CreateTagAsync(_alice, "Eins"), await CreateTagAsync(_alice, "Zwei"), await CreateTagAsync(_alice, "Drei") };
        await As(_alice).PutAsJsonAsync($"/api/audio-jobs/{job}/tags", new { tagIds = tags.Select(t => t.Id).ToArray() });

        var page = await As(_alice).GetFromJsonAsync<PaginatedResult<AudioJobListDto>>($"/api/audio-jobs?tag={tags[0].Id}");

        page!.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle().Which.Tags.Should().HaveCount(3);
    }
}
