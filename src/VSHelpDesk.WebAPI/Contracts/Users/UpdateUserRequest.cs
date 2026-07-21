namespace VSHelpDesk.WebAPI.Contracts.Users;

public sealed record UpdateUserRequest(
    string FullName,
    string Email,
    string Role,
    bool IsActive);
