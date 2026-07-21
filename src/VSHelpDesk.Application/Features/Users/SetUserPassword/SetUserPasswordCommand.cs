namespace VSHelpDesk.Application.Features.Users.SetUserPassword;

public sealed record SetUserPasswordCommand(Guid Id, string Password);
