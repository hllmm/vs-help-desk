namespace VSHelpDesk.Application.Features.Users.UpdateUser;

public sealed record UpdateUserCommand(
    Guid Id,
    string FullName,
    string Email,
    string Role,
    bool IsActive);
