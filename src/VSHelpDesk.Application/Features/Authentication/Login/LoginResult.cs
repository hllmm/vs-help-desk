namespace VSHelpDesk.Application.Features.Authentication.Login;

public sealed record LoginResult(string AccessToken, Guid UserId, string FullName, string Username);
