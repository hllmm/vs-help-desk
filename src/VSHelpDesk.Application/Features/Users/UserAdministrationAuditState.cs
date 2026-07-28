using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Features.Users;

internal static class UserAdministrationAuditState
{
    public static string Format(User user) =>
        $"role={user.Role};" +
        $"active={user.IsActive.ToString().ToLowerInvariant()};" +
        $"email={user.Email};" +
        $"fullName={user.FullName}";
}
