using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Authentication;

public sealed class JwtTokenService : ITokenService
{
    private readonly AuthOptions authOptions;
    private readonly TimeProvider timeProvider;

    public JwtTokenService(IOptions<AuthOptions> authOptions, TimeProvider timeProvider)
    {
        this.authOptions = authOptions.Value;
        this.timeProvider = timeProvider;
        AuthOptionsValidator.ThrowIfInvalid(this.authOptions);
    }

    public string CreateToken(User user)
    {
        var now = timeProvider.GetUtcNow();
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SigningKey));
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim("full_name", user.FullName),
            new Claim("role", user.Role.ToString())
        };
        var token = new JwtSecurityToken(
            authOptions.Issuer,
            authOptions.Audience,
            claims,
            now.UtcDateTime,
            now.AddMinutes(authOptions.ExpirationMinutes).UtcDateTime,
            new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
