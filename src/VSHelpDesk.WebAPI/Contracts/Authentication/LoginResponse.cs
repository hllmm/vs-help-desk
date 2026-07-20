namespace VSHelpDesk.WebAPI.Contracts.Authentication;

/// <summary>UC-001 — response body (profile only; JWT is HttpOnly cookie).</summary>
public sealed record LoginResponse(
    Guid UserId,
    string FullName,
    string Username,
    string Role);
