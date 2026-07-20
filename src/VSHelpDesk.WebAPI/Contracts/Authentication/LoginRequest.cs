namespace VSHelpDesk.WebAPI.Contracts.Authentication;

/// <summary>UC-001 — request body. Wired in Hafta 1.</summary>
public sealed record LoginRequest(string Username, string Password);
