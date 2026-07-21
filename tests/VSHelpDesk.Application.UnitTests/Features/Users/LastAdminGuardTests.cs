using VSHelpDesk.Application.Features.Users;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Domain.Exceptions;

namespace VSHelpDesk.Application.UnitTests.Features.Users;

public sealed class LastAdminGuardTests
{
    [Fact]
    public void EnsureCanDemoteOrDeactivate_SoleAdminDemote_Throws()
    {
        var sole = CreateUser(UserRole.Admin, isActive: true);
        var users = new[] { sole }.AsQueryable();

        var ex = Assert.Throws<DomainException>(() =>
            LastAdminGuard.EnsureCanDemoteOrDeactivate(
                users,
                sole.Id,
                UserRole.Support,
                newIsActive: true));

        Assert.Equal(LastAdminGuard.ErrorCode, ex.Message);
    }

    [Fact]
    public void EnsureCanDemoteOrDeactivate_SoleAdminDeactivate_Throws()
    {
        var sole = CreateUser(UserRole.Admin, isActive: true);
        var users = new[] { sole }.AsQueryable();

        var ex = Assert.Throws<DomainException>(() =>
            LastAdminGuard.EnsureCanDemoteOrDeactivate(
                users,
                sole.Id,
                UserRole.Admin,
                newIsActive: false));

        Assert.Equal(LastAdminGuard.ErrorCode, ex.Message);
    }

    [Fact]
    public void EnsureCanDemoteOrDeactivate_TwoAdminsDemoteOne_Ok()
    {
        var adminA = CreateUser(UserRole.Admin, isActive: true);
        var adminB = CreateUser(UserRole.Admin, isActive: true);
        var users = new[] { adminA, adminB }.AsQueryable();

        LastAdminGuard.EnsureCanDemoteOrDeactivate(
            users,
            adminA.Id,
            UserRole.Support,
            newIsActive: true);
    }

    [Fact]
    public void EnsureCanDemoteOrDeactivate_TwoAdminsDeactivateOne_Ok()
    {
        var adminA = CreateUser(UserRole.Admin, isActive: true);
        var adminB = CreateUser(UserRole.Admin, isActive: true);
        var users = new[] { adminA, adminB }.AsQueryable();

        LastAdminGuard.EnsureCanDemoteOrDeactivate(
            users,
            adminA.Id,
            UserRole.Admin,
            newIsActive: false);
    }

    [Fact]
    public void EnsureCanDemoteOrDeactivate_SupportUser_NoThrow()
    {
        var admin = CreateUser(UserRole.Admin, isActive: true);
        var support = CreateUser(UserRole.Support, isActive: true);
        var users = new[] { admin, support }.AsQueryable();

        LastAdminGuard.EnsureCanDemoteOrDeactivate(
            users,
            support.Id,
            UserRole.Support,
            newIsActive: false);
    }

    [Fact]
    public void EnsureCanDemoteOrDeactivate_InactiveAdmin_NoThrow()
    {
        var activeAdmin = CreateUser(UserRole.Admin, isActive: true);
        var inactiveAdmin = CreateUser(UserRole.Admin, isActive: false);
        var users = new[] { activeAdmin, inactiveAdmin }.AsQueryable();

        LastAdminGuard.EnsureCanDemoteOrDeactivate(
            users,
            inactiveAdmin.Id,
            UserRole.Support,
            newIsActive: false);
    }

    private static User CreateUser(UserRole role, bool isActive)
    {
        var user = new User(
            fullName: "Test User",
            username: Guid.NewGuid().ToString("N")[..8],
            email: $"{Guid.NewGuid():N}@test",
            passwordHash: "hash",
            role: role);

        if (!isActive)
        {
            user.Deactivate();
        }

        return user;
    }
}
