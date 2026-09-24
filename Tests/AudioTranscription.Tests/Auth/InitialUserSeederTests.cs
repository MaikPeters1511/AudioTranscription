using AudioTranscription.Api.Auth;
using AudioTranscription.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AudioTranscription.Tests.Auth;

public class InitialUserSeederTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly Mock<ILogger<InitialUserSeeder>> _logger = new();
    private readonly string _password = TestCredentials.NewPassword();

    public InitialUserSeederTests()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddIdentityCore<IdentityUser>().AddEntityFrameworkStores<AppDbContext>();
        _provider = services.BuildServiceProvider();
    }

    private InitialUserSeeder CreateSut(string? email, string? password) => new(
        _provider.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new AuthOptions { InitialUser = { Email = email, Password = password } }),
        _logger.Object);

    private async Task<List<IdentityUser>> UsersAsync()
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>().Users.ToListAsync();
    }

    [Fact]
    public async Task CreatesConfiguredUser_WhenNoUserExists()
    {
        await CreateSut("admin@example.com", _password).StartAsync(CancellationToken.None);

        var user = (await UsersAsync()).Should().ContainSingle().Subject;
        user.Email.Should().Be("admin@example.com");
        user.EmailConfirmed.Should().BeTrue();

        using var scope = _provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        (await users.CheckPasswordAsync(user, _password)).Should().BeTrue();
    }

    [Fact]
    public async Task DoesNothing_WhenAUserAlreadyExists()
    {
        await CreateSut("first@example.com", _password).StartAsync(CancellationToken.None);

        await CreateSut("second@example.com", TestCredentials.NewPassword()).StartAsync(CancellationToken.None);

        (await UsersAsync()).Select(u => u.Email).Should().Equal("first@example.com");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task LogsWarningAndCreatesNoUser_WhenNotConfigured(bool hasEmail, bool hasPassword)
    {
        await CreateSut(hasEmail ? "admin@example.com" : null, hasPassword ? _password : null)
            .StartAsync(CancellationToken.None);

        (await UsersAsync()).Should().BeEmpty();
        VerifyLogged(LogLevel.Warning);
    }

    [Fact]
    public async Task LogsErrorWithoutPassword_WhenPasswordViolatesPolicy()
    {
        await CreateSut("admin@example.com", "weak").StartAsync(CancellationToken.None);

        (await UsersAsync()).Should().BeEmpty();
        _logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) => !state.ToString()!.Contains("weak")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    private void VerifyLogged(LogLevel level) =>
        _logger.Verify(l => l.Log(
            level,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

    public void Dispose() => _provider.Dispose();
}
