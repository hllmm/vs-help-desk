using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VSHelpDesk.Application.Features.Authentication.Login;
using VSHelpDesk.WebAPI.Contracts.Authentication;

namespace VSHelpDesk.WebAPI.Controllers;

/// <summary>
/// UC-001 Login / current user.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(LoginHandler loginHandler) : ControllerBase
{
    /// <summary>POST api/auth/login — UC-001</summary>
    [AllowAnonymous]
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
        return Ok(new LoginResponse(
            login.AccessToken,
            login.UserId,
            login.FullName,
            login.Username));
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

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Unauthorized(new { message = "Unauthorized." });
        }

        return Ok(new CurrentUserResponse(userId, username, fullName));
    }
}
