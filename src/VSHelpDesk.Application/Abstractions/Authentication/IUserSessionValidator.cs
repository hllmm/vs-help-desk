namespace VSHelpDesk.Application.Abstractions.Authentication;

public interface IUserSessionValidator
{
    Task<bool> IsCurrentAsync(
        Guid userId,
        int securityVersion,
        string role,
        CancellationToken cancellationToken = default);
}
