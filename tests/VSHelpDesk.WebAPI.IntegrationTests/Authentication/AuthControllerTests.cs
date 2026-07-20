using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Features.Authentication.Login;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.WebAPI.Contracts.Authentication;
using VSHelpDesk.WebAPI.Controllers;

namespace VSHelpDesk.WebAPI.IntegrationTests.Authentication;

public sealed class AuthControllerTests
{
    private const string GenericFailure = "Invalid username or password.";
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
        Assert.Equal("access-token", body.AccessToken);
        Assert.Equal(user.Id, body.UserId);
        Assert.Equal(user.FullName, body.FullName);
        Assert.Equal(user.Username, body.Username);

        var json = JsonSerializer.Serialize(body);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stored-password-hash", json, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains(GenericFailure, JsonSerializer.Serialize(unauthorized.Value), StringComparison.Ordinal);
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
        Assert.Contains(GenericFailure, JsonSerializer.Serialize(unauthorized.Value), StringComparison.Ordinal);
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
                        new Claim("full_name", "Local Support User")
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
    }

    private static AuthController CreateController(User user, string validPassword)
    {
        var context = new FakeApplicationDbContext(user);
        var handler = new LoginHandler(
            context,
            new FakePasswordHasher(validPassword),
            new FakeTokenService("access-token"),
            new FixedTimeProvider(LoginTime));
        return new AuthController(handler);
    }

    private static User CreateUser(string username = "active.user") =>
        new("Active User", username, $"{username}@example.test", "stored-password-hash");

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

        public void Add<TEntity>(TEntity entity) where TEntity : class
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(1);

        public void ClearTrackedChanges()
        {
        }

    }

    private sealed class FakePasswordHasher(string validPassword) : IPasswordHasher
    {
        public string Hash(string password) => password;

        public bool Verify(string password, string? passwordHash) =>
            password == validPassword && passwordHash == "stored-password-hash";
    }

    private sealed class FakeTokenService(string token) : ITokenService
    {
        public string CreateToken(User user) => token;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
