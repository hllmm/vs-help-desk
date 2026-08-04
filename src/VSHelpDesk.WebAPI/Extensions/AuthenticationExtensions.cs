using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using VSHelpDesk.Infrastructure.Authentication;
using VSHelpDesk.WebAPI.Authentication;

namespace VSHelpDesk.WebAPI.Extensions;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddJwtBearerAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authOptions = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()
            ?? throw new InvalidOperationException(
                $"The {AuthOptions.SectionName} configuration section is required.");

        AuthOptionsValidator.ThrowIfInvalid(authOptions);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep JWT short-name claims (sub, unique_name) aligned with token issuance.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(authOptions.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = true,
                    ValidIssuer = authOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = authOptions.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    RoleClaimType = "role"
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (string.IsNullOrEmpty(context.Token)
                            && context.Request.Cookies.TryGetValue(
                                AuthCookieNames.Auth,
                                out var cookieToken)
                            && !string.IsNullOrWhiteSpace(cookieToken))
                        {
                            context.Token = cookieToken;
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var userIdClaim = context.Principal?.FindFirst("sub")?.Value;
                        if (!Guid.TryParse(userIdClaim, out var userId))
                        {
                            context.Fail("Invalid user claim.");
                            return;
                        }

                        var userRepo = context.HttpContext.RequestServices
                            .GetRequiredService<VSHelpDesk.Application.Abstractions.Persistence.Repositories.IUserRepository>();
                        var user = await userRepo.GetByIdAsync(userId, context.HttpContext.RequestAborted);

                        if (user is null || !user.IsActive)
                        {
                            context.Fail("User account is inactive or deleted.");
                            return;
                        }

                        var stampClaim = context.Principal?.FindFirst("security_stamp")?.Value;
                        if (!string.IsNullOrEmpty(stampClaim) &&
                            !string.Equals(stampClaim, user.SecurityStamp, StringComparison.Ordinal))
                        {
                            context.Fail("Token has been revoked due to security settings change.");
                        }
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            // Portal endpoints require an authenticated principal unless explicitly [AllowAnonymous].
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }
}
