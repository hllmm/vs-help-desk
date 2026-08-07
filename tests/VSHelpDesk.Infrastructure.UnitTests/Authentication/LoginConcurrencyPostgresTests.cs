using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common.Localization;
using VSHelpDesk.Application.Features.Authentication.Login;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Authentication;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.Infrastructure.Persistence.Repositories;
using VSHelpDesk.Infrastructure.UnitTests.Persistence;

namespace VSHelpDesk.Infrastructure.UnitTests.Authentication;

public sealed class LoginConcurrencyPostgresTests
{
    [PostgresFact]
    public async Task FiveConcurrentWrongPasswords_PersistAllAttempts_AndLockCorrectPassword()
    {
        const int concurrentAttempts = 5;
        const string correctPassword = "correct-password";
        var username = $"concurrent-login-{Guid.NewGuid():N}";
        var passwordHasher = new PasswordHasher();
        var user = new User(
            "Concurrent Login User",
            username,
            $"{username}@example.test",
            passwordHasher.Hash(correctPassword),
            UserRole.Support);
        var now = DateTimeOffset.UtcNow;

        await using (var seedContext = PostgresTestConnection.CreateContext())
        {
            seedContext.Users.Add(user);
            await seedContext.SaveChangesAsync();
        }

        try
        {
            var readBarrier = new LoginReadBarrier(concurrentAttempts);
            using var serviceProvider = CreateServiceProvider(readBarrier, now);
            var scopes = Enumerable.Range(0, concurrentAttempts)
                .Select(_ => serviceProvider.CreateAsyncScope())
                .ToArray();

            try
            {
                var contexts = scopes
                    .Select(scope => scope.ServiceProvider.GetRequiredService<ApplicationDbContext>())
                    .ToArray();
                Assert.Equal(concurrentAttempts, contexts.Distinct().Count());

                var wrongPasswordLogins = scopes
                    .Select(scope => scope.ServiceProvider
                        .GetRequiredService<LoginHandler>()
                        .HandleAsync(new LoginCommand(username, "wrong-password"), CancellationToken.None))
                    .ToArray();

                try
                {
                    await readBarrier.AllAttemptsReadAsync.WaitAsync(TimeSpan.FromSeconds(15));
                    Assert.Equal(concurrentAttempts, readBarrier.ReadFailedAttemptCounts.Count);
                    Assert.All(readBarrier.ReadFailedAttemptCounts, count => Assert.Equal(0, count));
                }
                finally
                {
                    readBarrier.ReleaseSaves();
                }

                var loginResults = await Task.WhenAll(wrongPasswordLogins);
                Assert.All(loginResults, result => Assert.True(result.IsFailure));
            }
            finally
            {
                foreach (var scope in scopes)
                {
                    await scope.DisposeAsync();
                }
            }

            await using (var verificationContext = PostgresTestConnection.CreateContext())
            {
                var persistedUser = await verificationContext.Users.SingleAsync(candidate => candidate.Id == user.Id);
                Assert.Equal(concurrentAttempts, persistedUser.FailedLoginAttempts);
                Assert.NotNull(persistedUser.LockoutEndUtc);
                Assert.True(persistedUser.LockoutEndUtc > now.UtcDateTime);
            }

            using var lockedAccountScope = serviceProvider.CreateScope();
            var correctPasswordResult = await lockedAccountScope.ServiceProvider
                .GetRequiredService<LoginHandler>()
                .HandleAsync(new LoginCommand(username, correctPassword), CancellationToken.None);

            Assert.True(correctPasswordResult.IsFailure);
        }
        finally
        {
            await using var cleanupContext = PostgresTestConnection.CreateContext();
            await cleanupContext.Users
                .Where(candidate => candidate.Id == user.Id)
                .ExecuteDeleteAsync();
        }
    }

    private static ServiceProvider CreateServiceProvider(LoginReadBarrier readBarrier, DateTimeOffset now)
    {
        var services = new ServiceCollection();
        services.AddSingleton(readBarrier);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenService, TestTokenService>();
        services.AddSingleton<IMessageProvider, TestMessageProvider>();
        services.AddSingleton<IOptions<LoginSecurityOptions>>(
            Options.Create(new LoginSecurityOptions { MaxFailedAttempts = 5, LockoutMinutes = 15 }));
        services.AddScoped<ApplicationDbContext>(_ => PostgresTestConnection.CreateContext());
        services.AddScoped<IApplicationDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<EfUserRepository>();
        services.AddScoped<IUserRepository, CoordinatedUserRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<LoginHandler>();

        return services.BuildServiceProvider();
    }

    private sealed class CoordinatedUserRepository(
        EfUserRepository inner,
        LoginReadBarrier readBarrier) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.GetByIdAsync(id, cancellationToken);

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            inner.GetByEmailAsync(email, cancellationToken);

        public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            var user = await inner.GetByUsernameAsync(username, cancellationToken);
            await readBarrier.WaitAfterReadAsync(user, cancellationToken);
            return user;
        }

        public IQueryable<User> GetListQueryable() => inner.GetListQueryable();

        public Task AddAsync(User user, CancellationToken cancellationToken = default) =>
            inner.AddAsync(user, cancellationToken);

        public void Update(User user) => inner.Update(user);
    }

    private sealed class LoginReadBarrier(int participantCount)
    {
        private readonly TaskCompletionSource allAttemptsRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource allowSaves =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<int> readFailedAttemptCounts = [];
        private int arrivedCount;

        public Task AllAttemptsReadAsync => allAttemptsRead.Task;

        public IReadOnlyList<int> ReadFailedAttemptCounts
        {
            get
            {
                lock (readFailedAttemptCounts)
                {
                    return readFailedAttemptCounts.ToArray();
                }
            }
        }

        public async Task WaitAfterReadAsync(User? user, CancellationToken cancellationToken)
        {
            var attemptNumber = Interlocked.Increment(ref arrivedCount);
            if (attemptNumber > participantCount)
            {
                return;
            }

            lock (readFailedAttemptCounts)
            {
                readFailedAttemptCounts.Add(user?.FailedLoginAttempts ?? -1);
            }

            if (attemptNumber == participantCount)
            {
                allAttemptsRead.TrySetResult();
            }

            await allowSaves.Task.WaitAsync(cancellationToken);
        }

        public void ReleaseSaves() => allowSaves.TrySetResult();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TestTokenService : ITokenService
    {
        public string CreateToken(User user) => "unused-for-locked-account";
    }

    private sealed class TestMessageProvider : IMessageProvider
    {
        public string Get(string key) => "Geçersiz kullanıcı adı veya şifre.";

        public string Get(string key, params object[] args) => Get(key);
    }
}
