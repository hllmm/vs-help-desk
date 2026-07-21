namespace VSHelpDesk.Application.Features.Users.GetUsers;

public sealed record UserListItemDto(
    Guid Id,
    string FullName,
    string Username,
    string Email,
    string Role,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastLoginAt);
