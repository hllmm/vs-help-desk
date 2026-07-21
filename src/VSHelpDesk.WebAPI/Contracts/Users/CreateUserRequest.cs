namespace VSHelpDesk.WebAPI.Contracts.Users;

public sealed record CreateUserRequest(
    string FullName,
    string Username,
    string Email,
    string Password,
    string Role);
