using Microsoft.Extensions.Options;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Common.Localization;
using VSHelpDesk.Application.Common.Models;

namespace VSHelpDesk.Application.Features.Authentication.Login;

public sealed class LoginHandler(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    TimeProvider timeProvider,
    IOptions<LoginSecurityOptions> loginSecurityOptions,
    IMessageProvider? messages = null)
{
    private readonly LoginSecurityOptions _options = loginSecurityOptions.Value;
    private readonly IMessageProvider _messages = messages ?? FallbackMessageProvider.Instance;

    public async Task<Result<LoginResult>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var normalizedUsername = command.Username?.Trim() ?? string.Empty;
        const int maxPersistenceAttempts = 3;

        for (var attempt = 0; attempt < maxPersistenceAttempts; attempt++)
        {
            var user = await userRepository.GetByUsernameAsync(normalizedUsername, cancellationToken);

            // Timing-attack mitigation: run dummy verification when user does not exist.
            if (user is null)
            {
                passwordHasher.Verify(command.Password, null);
                return InvalidCredentials();
            }

            // BR-015: inactive users receive same generic error, no lockout mutation.
            if (!user.IsActive)
            {
                passwordHasher.Verify(command.Password, user.PasswordHash);
                return InvalidCredentials();
            }

            var utcNow = timeProvider.GetUtcNow().UtcDateTime;

            // Preserve password-hash timing work without using its result for locked accounts.
            if (user.IsLoginLocked(utcNow))
            {
                passwordHasher.Verify(command.Password, user.PasswordHash);
                return InvalidCredentials();
            }

            var passwordIsValid = passwordHasher.Verify(command.Password, user.PasswordHash);
            if (passwordIsValid)
            {
                user.RegisterSuccessfulLogin();
                user.RecordLogin(utcNow);
            }
            else
            {
                user.RegisterFailedLogin(
                    utcNow,
                    _options.MaxFailedAttempts,
                    TimeSpan.FromMinutes(_options.LockoutMinutes));
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);

                if (!passwordIsValid)
                {
                    return InvalidCredentials();
                }

                var accessToken = tokenService.CreateToken(user);
                return Result.Success(new LoginResult(
                    accessToken,
                    user.Id,
                    user.FullName,
                    user.Username,
                    user.Role.ToString()));
            }
            catch (Exception ex) when (IsConcurrency(ex))
            {
                unitOfWork.ClearTrackedChanges();
                if (attempt == maxPersistenceAttempts - 1)
                {
                    throw new AuthenticationStateUnavailableException(ex);
                }
            }
        }

        throw new AuthenticationStateUnavailableException();
    }

    private Result<LoginResult> InvalidCredentials() =>
        Result.Failure<LoginResult>(_messages.Get(MessageKeys.Auth.InvalidCredentials));

    private static bool IsConcurrency(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is OptimisticConcurrencyException)
            {
                return true;
            }
        }

        return false;
    }
}
