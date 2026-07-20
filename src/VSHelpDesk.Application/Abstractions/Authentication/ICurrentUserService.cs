namespace VSHelpDesk.Application.Abstractions.Authentication;

/// <summary>
/// Resolves the authenticated support user from the HTTP context.
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }

    bool IsAuthenticated { get; }
}
