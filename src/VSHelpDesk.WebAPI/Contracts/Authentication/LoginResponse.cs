namespace VSHelpDesk.WebAPI.Contracts.Authentication;

/// <summary>UC-001 — response body. Wired in Hafta 1.</summary>
public sealed record LoginResponse(
    string AccessToken,
    Guid UserId,
    string FullName,
    string Username);
