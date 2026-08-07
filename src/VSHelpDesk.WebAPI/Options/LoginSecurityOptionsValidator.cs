using Microsoft.Extensions.Options;

namespace VSHelpDesk.WebAPI.Options;

public sealed class LoginSecurityOptionsValidator : IValidateOptions<LoginSecurityOptions>
{
    public ValidateOptionsResult Validate(string? name, LoginSecurityOptions options)
    {
        if (options.MaxFailedAttempts <= 0)
        {
            return ValidateOptionsResult.Fail(
                "The LoginSecurity:MaxFailedAttempts configuration value must be positive.");
        }

        if (options.MaxFailedAttempts > 100)
        {
            return ValidateOptionsResult.Fail(
                "The LoginSecurity:MaxFailedAttempts configuration value must not exceed 100.");
        }

        if (options.LockoutMinutes <= 0)
        {
            return ValidateOptionsResult.Fail(
                "The LoginSecurity:LockoutMinutes configuration value must be positive.");
        }

        if (options.LockoutMinutes > 1440)
        {
            return ValidateOptionsResult.Fail(
                "The LoginSecurity:LockoutMinutes configuration value must not exceed 1440.");
        }

        return ValidateOptionsResult.Success;
    }
}
