using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Features.Authentication.Login;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Authentication;
using VSHelpDesk.WebAPI.Authentication;
using VSHelpDesk.WebAPI.Contracts.Authentication;

namespace VSHelpDesk.WebAPI.Controllers;

/// <summary>
/// UC-001 Login / current user / logout.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    LoginHandler loginHandler,
    IHostEnvironment hostEnvironment,
    IOptions<AuthOptions> authOptions) : ControllerBase
{
    /// <summary>POST api/auth/login — UC-001</summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await loginHandler.HandleAsync(
            new LoginCommand(request.Username, request.Password),
            cancellationToken);

        if (result.IsFailure)
        {
            return Unauthorized(new { message = result.Error });
        }

        var login = result.Value!;
        var csrfToken = AuthCookieService.CreateCsrfToken();
        AuthCookieService.AppendAuthCookies(
            Response,
            login.AccessToken,
            csrfToken,
            authOptions.Value,
            hostEnvironment.IsDevelopment());

        return Ok(new LoginResponse(
            login.UserId,
            login.FullName,
            login.Username,
            login.Role));
    }

    /// <summary>POST api/auth/logout — clears auth + CSRF cookies</summary>
    [AllowAnonymous]
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        AuthCookieService.ClearAuthCookies(Response, hostEnvironment.IsDevelopment());
        return NoContent();
    }

    /// <summary>GET api/auth/me — protected; BR-014</summary>
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var userIdValue = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        var username = User.FindFirstValue(JwtRegisteredClaimNames.UniqueName)
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? string.Empty;
        var fullName = User.FindFirstValue("full_name") ?? string.Empty;
        var role = User.FindFirstValue("role") ?? UserRole.Support.ToString();

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Unauthorized(new { message = "Unauthorized." });
        }

        return Ok(new CurrentUserResponse(userId, username, fullName, role));
    }
}

