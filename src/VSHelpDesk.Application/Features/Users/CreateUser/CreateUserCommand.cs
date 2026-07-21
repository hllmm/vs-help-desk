namespace VSHelpDesk.Application.Features.Users.CreateUser;

public sealed record CreateUserCommand(
    string FullName,
    string Username,
    string Email,
    string Password,
    string Role);
