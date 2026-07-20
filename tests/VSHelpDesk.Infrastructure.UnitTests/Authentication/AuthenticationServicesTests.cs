using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure;
using VSHelpDesk.Infrastructure.Authentication;

namespace VSHelpDesk.Infrastructure.UnitTests.Authentication;

public sealed class AuthenticationServicesTests
{
    private static readonly DateTimeOffset TokenIssuedAt = new(2026, 7, 20, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void PasswordHasher_HashesSamePasswordWithDistinctSaltsAndVerifiesBoth()
    {
        var passwordHasher = new PasswordHasher();

        var firstPasswordHash = passwordHasher.Hash("correct-password");
        var secondPasswordHash = passwordHasher.Hash("correct-password");

        Assert.NotEqual("correct-password", firstPasswordHash);
        Assert.NotEqual("correct-password", secondPasswordHash);
        Assert.NotEqual(firstPasswordHash, secondPasswordHash);
        Assert.True(passwordHasher.Verify("correct-password", firstPasswordHash));
        Assert.True(passwordHasher.Verify("correct-password", secondPasswordHash));
        Assert.False(passwordHasher.Verify("wrong-password", firstPasswordHash));
    }

    [Fact]
    public void JwtTokenService_CreateToken_ContainsConfiguredMetadataClaimsAndExpiry()
    {
        var options = new AuthOptions
        {
            Issuer = "VSHelpDesk",
            Audience = "VSHelpDesk.Client",
            SigningKey = "test-signing-key-with-at-least-32-bytes!",
            ExpirationMinutes = 480
        };
        var user = new User("Active User", "active.user", "active.user@example.test", "password-hash", UserRole.Support);
        var tokenService = new JwtTokenService(Options.Create(options), new FixedTimeProvider(TokenIssuedAt));

        var accessToken = tokenService.CreateToken(user);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var validationTimeProvider = new FixedTimeProvider(TokenIssuedAt.AddMinutes(1));
        var principal = handler.ValidateToken(
            accessToken,
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ValidateIssuer = true,
                ValidIssuer = options.Issuer,
                ValidateAudience = true,
                ValidAudience = options.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                LifetimeValidator = (notBefore, expires, _, _) =>
                    notBefore <= validationTimeProvider.GetUtcNow().UtcDateTime &&
                    expires > validationTimeProvider.GetUtcNow().UtcDateTime
            },
            out var validatedToken);
        var token = Assert.IsType<JwtSecurityToken>(validatedToken);

        Assert.Equal(options.Issuer, token.Issuer);
        Assert.Contains(options.Audience, token.Audiences);
        Assert.Equal(SecurityAlgorithms.HmacSha256, token.Header.Alg);
        Assert.Equal(user.Id.ToString(), principal.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(user.Username, principal.Claims.Single(claim => claim.Type == JwtRegisteredClaimNames.UniqueName).Value);
        Assert.Equal(user.FullName, principal.Claims.Single(claim => claim.Type == "full_name").Value);
        Assert.Equal("Support", principal.Claims.Single(claim => claim.Type == "role").Value);
        Assert.Equal(TokenIssuedAt.AddMinutes(options.ExpirationMinutes).UtcDateTime, token.ValidTo);
    }

    [Fact]
    public void JwtTokenService_CreateToken_EmitsAdminRoleClaim()
    {
        var options = new AuthOptions
        {
            Issuer = "VSHelpDesk",
            Audience = "VSHelpDesk.Client",
            SigningKey = "test-signing-key-with-at-least-32-bytes!",
            ExpirationMinutes = 480
        };
        var admin = new User("Admin User", "admin.user", "admin@example.test", "password-hash", UserRole.Admin);
        var tokenService = new JwtTokenService(Options.Create(options), new FixedTimeProvider(TokenIssuedAt));

        var accessToken = tokenService.CreateToken(admin);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);

        Assert.Contains(jwt.Claims, c => c.Type == "role" && c.Value == "Admin");
    }

    [Fact]
    public void JwtTokenService_ShortSigningKey_ThrowsClearConfigurationError()
    {
        var options = new AuthOptions
        {
            Issuer = "VSHelpDesk",
            Audience = "VSHelpDesk.Client",
            SigningKey = "too-short",
            ExpirationMinutes = 480
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => new JwtTokenService(Options.Create(options), new FixedTimeProvider(TokenIssuedAt)));

        Assert.Contains("SigningKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JwtTokenService_CommittedPlaceholderSigningKey_ThrowsClearConfigurationError()
    {
        var options = new AuthOptions
        {
            Issuer = "VSHelpDesk",
            Audience = "VSHelpDesk.Client",
            SigningKey = "CHANGE_ME_DEV_ONLY_MIN_32_CHARS_LONG!!",
            ExpirationMinutes = 480
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => new JwtTokenService(Options.Create(options), new FixedTimeProvider(TokenIssuedAt)));

        Assert.Contains("placeholder", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddInfrastructure_InvalidAuthConfiguration_FailsStartupValidation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Issuer"] = "VSHelpDesk",
                ["Auth:Audience"] = "VSHelpDesk.Client",
                ["Auth:SigningKey"] = "too-short",
                ["Auth:ExpirationMinutes"] = "480",
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=metadata_test;Username=test_user",
                ["Email:ReceiverMode"] = "Fake",
                ["Email:SmtpHost"] = "localhost",
                ["Email:SmtpPort"] = "1025",
                ["Email:SmtpSecurityMode"] = "None",
                ["Email:SupportMailboxAddress"] = "support@vshelpdesk.local"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new FixedHostEnvironment
        {
            EnvironmentName = Environments.Development
        });
        services.AddInfrastructure(configuration);
        using var serviceProvider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => serviceProvider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("Auth:SigningKey", exception.Message, StringComparison.Ordinal);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FixedHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

