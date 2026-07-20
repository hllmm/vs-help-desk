using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Domain.UnitTests.Entities;

public sealed class UserRoleTests
{
    [Fact]
    public void Constructor_StoresRole()
    {
        var user = new User("A", "admin", "a@test", "hash", UserRole.Admin);
        Assert.Equal(UserRole.Admin, user.Role);
    }

    [Fact]
    public void AssignRole_UpdatesRole()
    {
        var user = new User("S", "support", "s@test", "hash", UserRole.Support);
        user.AssignRole(UserRole.Admin);
        Assert.Equal(UserRole.Admin, user.Role);
    }
}
