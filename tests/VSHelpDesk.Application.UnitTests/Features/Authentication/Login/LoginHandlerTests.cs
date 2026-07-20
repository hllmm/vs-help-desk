using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Features.Authentication.Login;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.UnitTests.Features.Authentication.Login;

public sealed class LoginHandlerTests
{
    private const string GenericFailure = "Invalid username or password.";
    private static readonly DateTimeOffset LoginTime = new(2026, 7, 20, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task UC001_ValidActiveCredentials_ReturnTokenAndRecordLogin()
    {
        var user = CreateUser();
        var context = new FakeApplicationDbContext(user);
        var tokenService = new FakeTokenService("access-token");
        var passwordHasher = new FakePasswordHasher("correct-password");
        var handler = CreateHandler(context, passwordHasher, tokenService);

        var result = await handler.HandleAsync(new LoginCommand(user.Username, "correct-password"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var login = Assert.IsType<LoginResult>(result.Value);
        Assert.Equal("access-token", login.AccessToken);
        Assert.Equal(user.Id, login.UserId);
        Assert.Equal(user.FullName, login.FullName);
        Assert.Equal(user.Username, login.Username);
        Assert.Equal(LoginTime.UtcDateTime, user.LastLoginAt);
        Assert.Equal(1, context.SaveChangesCallCount);
        Assert.Equal(1, tokenService.CreateTokenCallCount);
        Assert.Equal(1, passwordHasher.VerifyCallCount);
    }

    [Fact]
    public async Task UC001_WrongPassword_ReturnsGenericFailure()
    {
        var user = CreateUser();
        var context = new FakeApplicationDbContext(user);
        var tokenService = new FakeTokenService("access-token");
        var passwordHasher = new FakePasswordHasher("correct-password");
        var handler = CreateHandler(context, passwordHasher, tokenService);

        var result = await handler.HandleAsync(new LoginCommand(user.Username, "wrong-password"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(GenericFailure, result.Error);
        Assert.Null(user.LastLoginAt);
        Assert.Equal(0, context.SaveChangesCallCount);
        Assert.Equal(0, tokenService.CreateTokenCallCount);
        Assert.Equal(1, passwordHasher.VerifyCallCount);
    }

    [Fact]
    public async Task BR015_InactiveUser_ReturnsSameGenericFailure()
    {
        // BR-015: inactive accounts must not reveal their status.
        var user = CreateUser();
        user.Deactivate();
        var context = new FakeApplicationDbContext(user);
        var tokenService = new FakeTokenService("access-token");
        var passwordHasher = new FakePasswordHasher("correct-password");
        var handler = CreateHandler(context, passwordHasher, tokenService);

        var result = await handler.HandleAsync(new LoginCommand(user.Username, "correct-password"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(GenericFailure, result.Error);
        Assert.Null(user.LastLoginAt);
        Assert.Equal(0, context.SaveChangesCallCount);
        Assert.Equal(0, tokenService.CreateTokenCallCount);
        Assert.Equal(1, passwordHasher.VerifyCallCount);
    }

    [Fact]
    public async Task UC001_UnknownWrongAndInactiveCredentials_ReturnSameGenericFailure()
    {
        var activeUser = CreateUser();
        var inactiveUser = CreateUser("inactive.user");
        inactiveUser.Deactivate();
        var context = new FakeApplicationDbContext(activeUser, inactiveUser);
        var tokenService = new FakeTokenService("access-token");
        var passwordHasher = new FakePasswordHasher("correct-password");
        var handler = CreateHandler(context, passwordHasher, tokenService);

        var unknown = await handler.HandleAsync(new LoginCommand("unknown.user", "correct-password"), CancellationToken.None);
        Assert.Equal(1, passwordHasher.VerifyCallCount);
        var wrong = await handler.HandleAsync(new LoginCommand(activeUser.Username, "wrong-password"), CancellationToken.None);
        Assert.Equal(2, passwordHasher.VerifyCallCount);
        var inactive = await handler.HandleAsync(new LoginCommand(inactiveUser.Username, "correct-password"), CancellationToken.None);
        Assert.Equal(3, passwordHasher.VerifyCallCount);

        Assert.Equal(GenericFailure, unknown.Error);
        Assert.Equal(unknown.Error, wrong.Error);
        Assert.Equal(unknown.Error, inactive.Error);
        Assert.Equal(0, context.SaveChangesCallCount);
        Assert.Equal(0, tokenService.CreateTokenCallCount);
    }

    private static LoginHandler CreateHandler(
        FakeApplicationDbContext context,
        FakePasswordHasher passwordHasher,
        FakeTokenService tokenService) =>
        new(context, passwordHasher, tokenService, new FixedTimeProvider(LoginTime));

    private static User CreateUser(string username = "active.user") =>
        new("Active User", username, $"{username}@example.test", "stored-password-hash");

    private sealed class FakeApplicationDbContext(params User[] users) : IApplicationDbContext
    {
        public int SaveChangesCallCount { get; private set; }

        public IQueryable<User> Users { get; } = users.AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakePasswordHasher(string validPassword) : IPasswordHasher
    {
        public int VerifyCallCount { get; private set; }

        public string Hash(string password) => password;

        public bool Verify(string password, string? passwordHash)
        {
            VerifyCallCount++;
            return password == validPassword && passwordHash == "stored-password-hash";
        }
    }

    private sealed class FakeTokenService(string token) : ITokenService
    {
        public int CreateTokenCallCount { get; private set; }

        public string CreateToken(User user)
        {
            CreateTokenCallCount++;
            return token;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
