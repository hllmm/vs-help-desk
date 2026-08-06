using System.Net.Mail;
using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Application.Features.Users.GetUsers;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Domain.Exceptions;

namespace VSHelpDesk.Application.Features.Users.CreateUser;

public sealed class CreateUserHandler(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ICurrentUserService? currentUserService = null,
    TimeProvider? timeProvider = null,
    IApplicationDbContext? dbContext = null)
{
    public const int MinPasswordLength = 12;
    public const int MaxPasswordLength = 128;
    public const int MaxFullNameLength = 200;
    public const int MaxUsernameLength = 100;
    public const int MaxEmailLength = 255;

    public async Task<UserListItemDto> HandleAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var fullName = ValidateFullName(command.FullName);
        var username = ValidateUsername(command.Username);
        var email = ValidateEmail(command.Email);
        var password = ValidatePassword(command.Password);
        var role = ParseRole(command.Role);

        var existingUser = await userRepository.GetByUsernameAsync(username, cancellationToken);
        if (existingUser is not null)
        {
            throw new DomainException(UserCodes.UsernameTaken);
        }

        var user = new User(
            fullName,
            username,
            email,
            passwordHasher.Hash(password),
            role);

        await userRepository.AddAsync(user, cancellationToken);

        // Durable audit — append-only, never stores password/hash.
        if (dbContext is not null
            && currentUserService?.UserId is Guid actorId
            && actorId != Guid.Empty)
        {
            var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            var audit = new UserAuditEvent(
                actorId,
                user.Id,
                "Created",
                null,
                user.Role.ToString(),
                null,
                null,
                now,
                null);
            dbContext.Add(audit);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(user);
    }

    internal static string ValidateFullName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new DomainException(UserCodes.FullNameRequired);
        }

        var trimmed = fullName.Trim();
        if (trimmed.Length > MaxFullNameLength)
        {
            throw new DomainException(UserCodes.FullNameTooLong);
        }

        return trimmed;
    }

    internal static string ValidateUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new DomainException(UserCodes.UsernameRequired);
        }

        var trimmed = username.Trim();
        if (trimmed.Length > MaxUsernameLength)
        {
            throw new DomainException(UserCodes.UsernameTooLong);
        }

        return trimmed;
    }

    internal static string ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainException(UserCodes.EmailRequired);
        }

        var trimmed = email.Trim();
        if (trimmed.Length > MaxEmailLength)
        {
            throw new DomainException(UserCodes.EmailTooLong);
        }

        if (!MailAddress.TryCreate(trimmed, out var parsed)
            || !string.Equals(parsed.Address, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(UserCodes.EmailInvalid);
        }

        return trimmed;
    }

    internal static string ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new DomainException(UserCodes.PasswordRequired);
        }

        if (password.Length < MinPasswordLength)
        {
            throw new DomainException(UserCodes.PasswordTooShort);
        }

        if (password.Length > MaxPasswordLength)
        {
            throw new DomainException(UserCodes.PasswordTooLong);
        }

        return password;
    }

    internal static UserRole ParseRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)
            || !Enum.TryParse<UserRole>(role.Trim(), ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new DomainException(UserCodes.RoleInvalid);
        }

        return parsed;
    }

    private static UserListItemDto ToDto(User user) =>
        new(
            user.Id,
            user.FullName,
            user.Username,
            user.Email,
            user.Role.ToString(),
            user.IsActive,
            user.CreatedAt,
            user.LastLoginAt);
}
