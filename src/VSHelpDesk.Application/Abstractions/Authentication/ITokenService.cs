using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Abstractions.Authentication;

/// <summary>
/// Issues auth tokens/sessions for support users. Implemented in Infrastructure — Hafta 1.
/// </summary>
public interface ITokenService
{
    string CreateToken(User user);
}
