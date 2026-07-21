namespace VSHelpDesk.Application.Features.Tickets.GetAssignableUsers;

public sealed record AssignableUserDto(
    Guid Id,
    string FullName,
    string Username);
