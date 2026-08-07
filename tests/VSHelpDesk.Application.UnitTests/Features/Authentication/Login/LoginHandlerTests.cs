using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Common.Localization;
using VSHelpDesk.Application.Features.Authentication.Login;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Application.UnitTests.Features.Authentication.Login;

public sealed class LoginHandlerTests
{
    private static readonly string GenericFailure = "Geçersiz kullanıcı adı veya şifre.";
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
        Assert.Equal(UserRole.Support.ToString(), login.Role);
        Assert.Equal(LoginTime.UtcDateTime, user.LastLoginAt);
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockoutEndUtc);
        Assert.Equal(1, context.SaveChangesCallCount);
        Assert.Equal(1, tokenService.CreateTokenCallCount);
        Assert.Equal(1, passwordHasher.VerifyCallCount);
    }

    [Fact]
    public async Task UC001_WrongPassword_ReturnsGenericFailure_AndIncrementsCounter()
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
        Assert.Equal(1, user.FailedLoginAttempts);
        Assert.Null(user.LockoutEndUtc);
        Assert.Equal(1, context.SaveChangesCallCount);
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
        // Inactive still does a verify for timing parity (see handler)
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
        // Only wrong password increments (unknown and inactive do not save)
        Assert.Equal(1, context.SaveChangesCallCount);
        Assert.Equal(0, tokenService.CreateTokenCallCount);
    }

    [Fact]
    public async Task FailedLogin_IncrementsCounter_AndLocksAfterFifthAttempt()
    {
        var user = CreateUser();
        var context = new FakeApplicationDbContext(user);
        var handler = CreateHandler(context, new FakePasswordHasher("correct-password"), new FakeTokenService("token"));
        var lockoutOptions = CreateLoginSecurityOptions(maxFailedAttempts: 5, lockoutMinutes: 15);
        // Use handler with custom options
        handler = CreateHandler(context, new FakePasswordHasher("correct-password"), new FakeTokenService("token"), LoginTime, lockoutOptions);

        for (var i = 0; i < 4; i++)
        {
            var result = await handler.HandleAsync(new LoginCommand(user.Username, "wrong-password"), CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.Equal(i + 1, user.FailedLoginAttempts);
            Assert.Null(user.LockoutEndUtc);
        }

        var fifth = await handler.HandleAsync(new LoginCommand(user.Username, "wrong-password"), CancellationToken.None);
        Assert.True(fifth.IsFailure);
        Assert.Equal(5, user.FailedLoginAttempts);
        Assert.NotNull(user.LockoutEndUtc);
        Assert.Equal(LoginTime.UtcDateTime.AddMinutes(15), user.LockoutEndUtc!.Value);
    }

    [Fact]
    public async Task LockedAccount_PerformsExactlyOneIgnoredPasswordVerification()
    {
        var user = CreateUser();
        for (var i = 0; i < 5; i++)
        {
            user.RegisterFailedLogin(LoginTime.UtcDateTime, 5, TimeSpan.FromMinutes(15));
        }

        var context = new FakeApplicationDbContext(user);
        var hasher = new FakePasswordHasher("correct-password");
        var tokenService = new FakeTokenService("token");
        var handler = CreateHandler(context, hasher, tokenService, LoginTime);

        Assert.True(user.IsLoginLocked(LoginTime.UtcDateTime));
        var lockedResult = await handler.HandleAsync(new LoginCommand(user.Username, "correct-password"), CancellationToken.None);

        Assert.True(lockedResult.IsFailure);
        Assert.Equal(GenericFailure, lockedResult.Error);
        Assert.Equal(1, hasher.VerifyCallCount);
        Assert.Equal(0, context.SaveChangesAttemptCount);
        Assert.Equal(0, tokenService.CreateTokenCallCount);
    }

    [Fact]
    public async Task SuccessfulLogin_ResetsCounter_AndClearsLockout()
    {
        var user = CreateUser();
        var context = new FakeApplicationDbContext(user);
        var handler = CreateHandler(context, new FakePasswordHasher("correct-password"), new FakeTokenService("token"));

        // 2 failed attempts
        await handler.HandleAsync(new LoginCommand(user.Username, "wrong-password"), CancellationToken.None);
        await handler.HandleAsync(new LoginCommand(user.Username, "wrong-password"), CancellationToken.None);
        Assert.Equal(2, user.FailedLoginAttempts);

        // Successful login
        var result = await handler.HandleAsync(new LoginCommand(user.Username, "correct-password"), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockoutEndUtc);
        Assert.NotNull(user.LastLoginAt);
    }

    [Fact]
    public async Task LockoutExpires_AllowsLoginAgain()
    {
        var start = new DateTimeOffset(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);
        var user = CreateUser();
        var context = new FakeApplicationDbContext(user);
        var timeProvider = new MutableTimeProvider(start);
        var handler = new LoginHandler(context, context, new FakePasswordHasher("correct-password"), new FakeTokenService("token"), timeProvider, Options.Create(new LoginSecurityOptions { MaxFailedAttempts = 5, LockoutMinutes = 15 }), new FakeMessageProvider());

        // 5 failures to lock
        for (var i = 0; i < 5; i++)
        {
            await handler.HandleAsync(new LoginCommand(user.Username, "wrong-password"), CancellationToken.None);
        }

        Assert.True(user.IsLoginLocked(start.UtcDateTime));
        var lockedResult = await handler.HandleAsync(new LoginCommand(user.Username, "correct-password"), CancellationToken.None);
        Assert.True(lockedResult.IsFailure);

        // Advance past lockout (15 minutes)
        timeProvider.Advance(TimeSpan.FromMinutes(16));

        Assert.False(user.IsLoginLocked(timeProvider.GetUtcNow().UtcDateTime));
        var afterExpiry = await handler.HandleAsync(new LoginCommand(user.Username, "correct-password"), CancellationToken.None);
        Assert.True(afterExpiry.IsSuccess);
        Assert.Equal(0, user.FailedLoginAttempts);
    }

    [Fact]
    public async Task UsernameNormalization_TrimsWhitespace()
    {
        var user = CreateUser("test.user");
        var context = new FakeApplicationDbContext(user);
        var handler = CreateHandler(context, new FakePasswordHasher("correct-password"), new FakeTokenService("token"));

        var result = await handler.HandleAsync(new LoginCommand("  test.user  ", "correct-password"), CancellationToken.None);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ConcurrencyConflict_OnFailedLogin_DoesNotReturn500()
    {
        var user = CreateUser();
        var freshUser = CreateUser();
        var context = new FakeApplicationDbContext(user) { FreshUserAfterClear = freshUser };
        context.FailNextSaveWithConcurrency = true;
        var handler = CreateHandler(context, new FakePasswordHasher("correct-password"), new FakeTokenService("token"));

        var result = await handler.HandleAsync(new LoginCommand(user.Username, "wrong-password"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(GenericFailure, result.Error);
        // Should have retried and eventually saved (second attempt succeeds)
        Assert.Equal(2, context.SaveChangesAttemptCount);
        Assert.Equal(1, freshUser.FailedLoginAttempts);
    }

    [Fact]
    public async Task ConcurrencyConflict_ReloadsFreshUser_ReverifiesAndReappliesFailedLogin()
    {
        var staleUser = CreateUser();
        var freshUser = CreateUser();
        freshUser.RegisterFailedLogin(LoginTime.UtcDateTime, 5, TimeSpan.FromMinutes(15));
        var context = new FakeApplicationDbContext(staleUser)
        {
            FreshUserAfterClear = freshUser,
            ConcurrencyFailuresRemaining = 1
        };
        var passwordHasher = new FakePasswordHasher("correct-password");
        var tokenService = new FakeTokenService("token");
        var handler = CreateHandler(context, passwordHasher, tokenService);

        var result = await handler.HandleAsync(
            new LoginCommand(staleUser.Username, "wrong-password"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(GenericFailure, result.Error);
        Assert.Equal(2, context.GetByUsernameCallCount);
        Assert.Equal(1, context.ClearTrackedChangesCallCount);
        Assert.Equal(2, context.SaveChangesAttemptCount);
        Assert.Equal(2, passwordHasher.VerifyCallCount);
        Assert.Equal(2, freshUser.FailedLoginAttempts);
        Assert.Equal(0, tokenService.CreateTokenCallCount);
    }

    [Fact]
    public async Task ConcurrencyConflict_ReloadsFreshLockedUser_AndStopsWithoutSavingAgain()
    {
        var staleUser = CreateUser();
        var freshLockedUser = CreateUser();
        for (var i = 0; i < 5; i++)
        {
            freshLockedUser.RegisterFailedLogin(LoginTime.UtcDateTime, 5, TimeSpan.FromMinutes(15));
        }

        var context = new FakeApplicationDbContext(staleUser)
        {
            FreshUserAfterClear = freshLockedUser,
            ConcurrencyFailuresRemaining = 1
        };
        var passwordHasher = new FakePasswordHasher("correct-password");
        var tokenService = new FakeTokenService("token");
        var handler = CreateHandler(context, passwordHasher, tokenService);

        var result = await handler.HandleAsync(
            new LoginCommand(staleUser.Username, "correct-password"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(GenericFailure, result.Error);
        Assert.Equal(2, context.GetByUsernameCallCount);
        Assert.Equal(1, context.ClearTrackedChangesCallCount);
        Assert.Equal(1, context.SaveChangesAttemptCount);
        Assert.Equal(2, passwordHasher.VerifyCallCount);
        Assert.Equal(0, tokenService.CreateTokenCallCount);
    }

    [Fact]
    public async Task FourConcurrencyConflicts_SaveOnFifthAttempt_ReturnsToken()
    {
        var user = CreateUser();
        var context = new FakeApplicationDbContext(user) { ConcurrencyFailuresRemaining = 4 };
        var tokenService = new FakeTokenService("token");
        var handler = CreateHandler(context, new FakePasswordHasher("correct-password"), tokenService);

        var result = await handler.HandleAsync(
            new LoginCommand(user.Username, "correct-password"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, context.SaveChangesAttemptCount);
        Assert.Equal(5, context.GetByUsernameCallCount);
        Assert.Equal(4, context.ClearTrackedChangesCallCount);
        Assert.Equal(1, tokenService.CreateTokenCallCount);
    }

    [Fact]
    public async Task FiveConcurrencyConflicts_ThrowsAuthenticationStateUnavailableException()
    {
        var user = CreateUser();
        var context = new FakeApplicationDbContext(user) { ConcurrencyFailuresRemaining = 5 };
        var handler = CreateHandler(
            context,
            new FakePasswordHasher("correct-password"),
            new FakeTokenService("token"));

        var exception = await Record.ExceptionAsync(() => handler.HandleAsync(
            new LoginCommand(user.Username, "correct-password"),
            CancellationToken.None));

        Assert.IsType<AuthenticationStateUnavailableException>(exception);
        Assert.Equal(5, context.SaveChangesAttemptCount);
    }

    [Fact]
    public async Task FinalConcurrencyConflict_DoesNotCreateToken()
    {
        var user = CreateUser();
        var context = new FakeApplicationDbContext(user) { ConcurrencyFailuresRemaining = 5 };
        var tokenService = new FakeTokenService("token");
        var handler = CreateHandler(context, new FakePasswordHasher("correct-password"), tokenService);

        await Record.ExceptionAsync(() => handler.HandleAsync(
            new LoginCommand(user.Username, "correct-password"),
            CancellationToken.None));

        Assert.Equal(0, tokenService.CreateTokenCallCount);
    }

    [Fact]
    public void LoginSecurityOptions_InvalidConfig_FailsValidation()
    {
        var validator = new LoginSecurityOptionsValidator();
        var invalid1 = new LoginSecurityOptions { MaxFailedAttempts = 0, LockoutMinutes = 15 };
        var result1 = validator.Validate(null, invalid1);
        Assert.True(result1.Failed);

        var invalid2 = new LoginSecurityOptions { MaxFailedAttempts = 5, LockoutMinutes = 0 };
        var result2 = validator.Validate(null, invalid2);
        Assert.True(result2.Failed);

        var valid = new LoginSecurityOptions { MaxFailedAttempts = 5, LockoutMinutes = 15 };
        var result3 = validator.Validate(null, valid);
        Assert.True(result3.Succeeded);
    }

    private static LoginSecurityOptions CreateLoginSecurityOptions(int maxFailedAttempts = 5, int lockoutMinutes = 15)
    {
        return new LoginSecurityOptions { MaxFailedAttempts = maxFailedAttempts, LockoutMinutes = lockoutMinutes };
    }

    private static LoginHandler CreateHandler(
        FakeApplicationDbContext context,
        FakePasswordHasher passwordHasher,
        FakeTokenService tokenService,
        DateTimeOffset? time = null,
        LoginSecurityOptions? options = null) =>
        new(context, context, passwordHasher, tokenService, new FixedTimeProvider(time ?? LoginTime), Options.Create(options ?? new LoginSecurityOptions { MaxFailedAttempts = 5, LockoutMinutes = 15 }), new FakeMessageProvider());

    private sealed class FakeMessageProvider : IMessageProvider
    {
        public string Get(string key) => GenericFailure;
        public string Get(string key, params object[] args) => GenericFailure;
    }

    private static User CreateUser(string username = "active.user") =>
        new("Active User", username, $"{username}@example.test", "stored-password-hash", UserRole.Support);

    private sealed class FakeApplicationDbContext(params User[] users) : IApplicationDbContext, IUserRepository, IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }
        public int SaveChangesAttemptCount { get; private set; }
        public int GetByUsernameCallCount { get; private set; }
        public int ClearTrackedChangesCallCount { get; private set; }
        public bool FailNextSaveWithConcurrency { get; set; }
        public int ConcurrencyFailuresRemaining { get; set; }
        public User? FreshUserAfterClear { get; init; }

        private bool TrackingWasCleared { get; set; }

        public IQueryable<User> Users { get; } = users.AsQueryable();

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(Users.FirstOrDefault(u => u.Email == email));

        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken)
        {
            GetByUsernameCallCount++;
            var candidates = TrackingWasCleared && FreshUserAfterClear is not null
                ? new[] { FreshUserAfterClear }.AsQueryable()
                : Users;
            return Task.FromResult(candidates.FirstOrDefault(u => u.Username == username));
        }

        public IQueryable<User> GetListQueryable() => Users;

        public Task AddAsync(User user, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Update(User user) { }

        public IQueryable<Ticket> Tickets { get; } = Array.Empty<Ticket>().AsQueryable();

        public IQueryable<TicketMessage> TicketMessages { get; } =
            Array.Empty<TicketMessage>().AsQueryable();

        public IQueryable<TicketAttachment> TicketAttachments { get; } =
            Array.Empty<TicketAttachment>().AsQueryable();

        public IQueryable<ProcessedEmailMessage> ProcessedEmailMessages { get; } =
            Array.Empty<ProcessedEmailMessage>().AsQueryable();

        public IQueryable<ApplicationParameter> ApplicationParameters { get; } =
            Array.Empty<ApplicationParameter>().AsQueryable();

        public IQueryable<ParameterChangeLog> ParameterChangeLogs { get; } =
            Array.Empty<ParameterChangeLog>().AsQueryable();

        public IQueryable<SystemLog> SystemLogs { get; } =
            Array.Empty<SystemLog>().AsQueryable();

        public IQueryable<UserAuditEvent> UserAuditEvents { get; } = Array.Empty<UserAuditEvent>().AsQueryable();

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
        }

        public void Remove<TEntity>(TEntity entity) where TEntity : class
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesAttemptCount++;
            if (FailNextSaveWithConcurrency)
            {
                FailNextSaveWithConcurrency = false;
                throw new OptimisticConcurrencyException("concurrency", null);
            }

            if (ConcurrencyFailuresRemaining > 0)
            {
                ConcurrencyFailuresRemaining--;
                throw new OptimisticConcurrencyException("concurrency", null);
            }

            SaveChangesCallCount++;
            return Task.FromResult(1);
        }

        public void ClearTrackedChanges()
        {
            ClearTrackedChangesCallCount++;
            TrackingWasCleared = true;
        }

        public Task ExecuteSqlRawAsync(string sql, CancellationToken cancellationToken = default) => Task.CompletedTask;
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

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public MutableTimeProvider(DateTimeOffset now) => _now = now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
