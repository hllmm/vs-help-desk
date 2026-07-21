using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Domain.Exceptions;

namespace VSHelpDesk.Application.Features.Users;

/// <summary>
/// Ensures the system always retains at least one active Admin (Role == Admin &amp;&amp; IsActive).
/// </summary>
public static class LastAdminGuard
{
    public const string ErrorCode = "last-admin-required";

    /// <summary>
    /// Throws <see cref="DomainException"/> with <see cref="ErrorCode"/> when applying
    /// <paramref name="newRole"/> / <paramref name="newIsActive"/> to <paramref name="targetUserId"/>
    /// would leave zero active Admins.
    /// </summary>
    public static void EnsureCanDemoteOrDeactivate(
        IQueryable<User> users,
        Guid targetUserId,
        UserRole newRole,
        bool newIsActive)
    {
        ArgumentNullException.ThrowIfNull(users);

        var target = users.FirstOrDefault(u => u.Id == targetUserId)
            ?? throw new InvalidOperationException($"User '{targetUserId}' was not found.");

        var isCurrentlyActiveAdmin = target.Role == UserRole.Admin && target.IsActive;
        var willBeActiveAdmin = newRole == UserRole.Admin && newIsActive;

        // No active-admin seat is being removed.
        if (!isCurrentlyActiveAdmin || willBeActiveAdmin)
        {
            return;
        }

        var activeAdminCount = users.Count(u => u.Role == UserRole.Admin && u.IsActive);
        if (activeAdminCount - 1 < 1)
        {
            throw new DomainException(ErrorCode);
        }
    }
}
