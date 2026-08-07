namespace VSHelpDesk.Application.Features.Authentication.Login;

public sealed class LoginSecurityOptions
{
    public const string SectionName = "LoginSecurity";

    public int MaxFailedAttempts { get; init; } = 5;

    public int LockoutMinutes { get; init; } = 15;
}
