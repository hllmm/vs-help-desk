using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using VSHelpDesk.Application.Abstractions.Authentication;
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
                        if (!Guid.TryParse(
                                context.Principal?.FindFirstValue(
                                    JwtRegisteredClaimNames.Sub),
                                out var userId)
                            || !int.TryParse(
                                context.Principal?.FindFirstValue(
                                    AuthClaimNames.SecurityVersion),
                                NumberStyles.None,
                                CultureInfo.InvariantCulture,
                                out var securityVersion))
                        {
                            context.Fail("Session is no longer valid.");
                            return;
                        }

                        var claimedRole =
                            context.Principal?.FindFirstValue("role")
                            ?? string.Empty;
                        var validator = context.HttpContext.RequestServices
                            .GetRequiredService<IUserSessionValidator>();
                        if (!await validator.IsCurrentAsync(
                                userId,
                                securityVersion,
                                claimedRole,
                                context.HttpContext.RequestAborted))
                        {
                            context.Fail("Session is no longer valid.");
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
