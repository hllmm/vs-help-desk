namespace VSHelpDesk.Application.Abstractions.Email;

/// <summary>
/// Typed verdict from trusted MTA Authentication-Results header.
/// Parsed once in Infrastructure; Application consumes IsTrusted.
/// </summary>
public sealed record EmailAuthenticationVerdict(
    bool IsTrusted,
    bool DmarcPassed,
    string? AuthServId,
    string? RawHeader);
