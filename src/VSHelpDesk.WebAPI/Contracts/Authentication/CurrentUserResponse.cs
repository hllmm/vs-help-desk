namespace VSHelpDesk.WebAPI.Contracts.Authentication;

/// <summary>Authenticated support user summary from JWT claims (GET api/auth/me).</summary>
public sealed record CurrentUserResponse(Guid UserId, string Username, string FullName, string Role);
