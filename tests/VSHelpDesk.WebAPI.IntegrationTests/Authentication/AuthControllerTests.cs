using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Common.Localization;
using VSHelpDesk.Application.Features.Authentication.Login;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Authentication;
using VSHelpDesk.WebAPI.Authentication;
using VSHelpDesk.WebAPI.Contracts.Authentication;
using VSHelpDesk.WebAPI.Controllers;
using VSHelpDesk.WebAPI.Middleware;

namespace VSHelpDesk.WebAPI.IntegrationTests.Authentication;

public sealed class AuthControllerTests
{
    private const string GenericFailure = "Geçersiz kullanıcı adı veya şifre.";
    private static readonly DateTimeOffset LoginTime = new(2026, 7, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UC001_Login_ValidCredentials_Returns200LoginResponseWithoutSecrets()
    {
        var user = CreateUser();
        var controller = CreateController(user, validPassword: "correct-password");

        var result = await controller.Login(
            new LoginRequest(user.Username, "correct-password"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        var body = Assert.IsType<LoginResponse>(ok.Value);
        Assert.Equal(user.Id, body.UserId);
        Assert.Equal(user.FullName, body.FullName);
        Assert.Equal(user.Username, body.Username);
        Assert.Equal(UserRole.Support.ToString(), body.Role);

        var json = JsonSerializer.Serialize(body);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stored-password-hash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access-token", json, StringComparison.OrdinalIgnoreCase);

        var setCookie = controller.Response.Headers.SetCookie.ToString();
        Assert.Contains(AuthCookieNames.Auth, setCookie, StringComparison.Ordinal);
        Assert.Contains(AuthCookieNames.Csrf, setCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UC001_Login_WrongPassword_Returns401GenericMessage()
    {
        var user = CreateUser();
        var controller = CreateController(user, validPassword: "correct-password");

        var result = await controller.Login(
            new LoginRequest(user.Username, "wrong-password"),
            CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        Assert.Contains(GenericFailure, unauthorized.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BR015_Login_InactiveUser_Returns401SameGenericMessage()
    {
        var user = CreateUser();
        user.Deactivate();
        var controller = CreateController(user, validPassword: "correct-password");

        var result = await controller.Login(
            new LoginRequest(user.Username, "correct-password"),
            CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        Assert.Contains(GenericFailure, unauthorized.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_WhenAuthenticationStatePersistenceIsUnavailable_Returns503WithoutJwtOrAuthCookie()
    {
        var user = CreateUser();
        var unitOfWork = new FakeUnitOfWork { ConcurrencyFailuresRemaining = 5 };
        var tokenService = new FakeTokenService("access-token");
        var controller = CreateController(
            user,
            validPassword: "correct-password",
            unitOfWork,
            tokenService);
        controller.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(
            async _ =>
            {
                await controller.Login(
                    new LoginRequest(user.Username, "correct-password"),
                    CancellationToken.None);
            },
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.InvokeAsync(controller.HttpContext, FallbackMessageProvider.Instance);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, controller.Response.StatusCode);
        Assert.Equal(0, tokenService.CreateTokenCallCount);
        Assert.DoesNotContain("access-token", controller.Response.Headers.SetCookie.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, controller.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public void BR014_Me_AuthenticatedPrincipal_ReturnsUserSummaryFromClaims()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var controller = CreateController(CreateUser(), validPassword: "correct-password");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim("sub", userId.ToString()),
                        new Claim("unique_name", "support"),
                        new Claim("full_name", "Local Support User"),
                        new Claim("role", UserRole.Support.ToString())
                    ],
                    authenticationType: "Bearer"))
            }
        };

        var result = controller.Me();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        var body = Assert.IsType<CurrentUserResponse>(ok.Value);
        Assert.Equal(userId, body.UserId);
        Assert.Equal("support", body.Username);
        Assert.Equal("Local Support User", body.FullName);
        Assert.Equal(UserRole.Support.ToString(), body.Role);
    }

    [Fact]
    public void Logout_ClearsAuthCookies_Returns204()
    {
        var controller = CreateController(CreateUser(), validPassword: "correct-password");

        var result = controller.Logout();

        Assert.IsType<NoContentResult>(result);
        var setCookie = controller.Response.Headers.SetCookie.ToString();
        Assert.Contains(AuthCookieNames.Auth, setCookie, StringComparison.Ordinal);
        Assert.Contains(AuthCookieNames.Csrf, setCookie, StringComparison.Ordinal);
    }

    private static AuthController CreateController(
        User user,
        string validPassword,
        FakeUnitOfWork? unitOfWork = null,
        FakeTokenService? tokenService = null)
    {
        var userRepository = new FakeUserRepository(user);
        unitOfWork ??= new FakeUnitOfWork();
        tokenService ??= new FakeTokenService("access-token");
        var loginSecurityOptions = Microsoft.Extensions.Options.Options.Create(new LoginSecurityOptions
        {
            MaxFailedAttempts = 5,
            LockoutMinutes = 15
        });
        var handler = new LoginHandler(
            userRepository,
            unitOfWork,
            new FakePasswordHasher(validPassword),
            tokenService,
            new FixedTimeProvider(LoginTime),
            loginSecurityOptions);
        var env = new FakeHostEnvironment { EnvironmentName = Environments.Development };
        var authOptions = Microsoft.Extensions.Options.Options.Create(new AuthOptions
        {
            Issuer = "VSHelpDesk",
            Audience = "VSHelpDesk",
            SigningKey = "unit-test-signing-key-with-32-bytes!!",
            ExpirationMinutes = 60
        });

        var controller = new AuthController(handler, env, authOptions)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        return controller;
    }

    private static User CreateUser(string username = "active.user") =>
        new("Active User", username, $"{username}@example.test", "stored-password-hash", UserRole.Support);

    private sealed class FakeApplicationDbContext(params User[] users) : IApplicationDbContext
    {
        public IQueryable<User> Users { get; } = users.AsQueryable();

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

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(1);

        public void ClearTrackedChanges()
        {
        }

    }

    private sealed class FakeUserRepository(User user) : VSHelpDesk.Application.Abstractions.Persistence.Repositories.IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(user.Id == id ? user : null);

        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase) ? user : null);

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase) ? user : null);

        public Task AddAsync(User user, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Update(User user) { }

        public IQueryable<User> GetListQueryable() => new[] { user }.AsQueryable();
    }

    private sealed class FakeUnitOfWork : VSHelpDesk.Application.Abstractions.Persistence.Repositories.IUnitOfWork
    {
        public int ConcurrencyFailuresRemaining { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (ConcurrencyFailuresRemaining > 0)
            {
                ConcurrencyFailuresRemaining--;
                throw new OptimisticConcurrencyException("concurrency");
            }

            return Task.FromResult(1);
        }

        public void ClearTrackedChanges() { }
    }

    private sealed class FakePasswordHasher(string validPassword) : IPasswordHasher
    {
        public string Hash(string password) => password;

        public bool Verify(string password, string? passwordHash) =>
            password == validPassword && passwordHash == "stored-password-hash";
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

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "VSHelpDesk.WebAPI.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
