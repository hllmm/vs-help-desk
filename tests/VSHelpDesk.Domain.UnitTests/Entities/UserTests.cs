using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.Domain.UnitTests.Entities;

public sealed class UserTests
{
    [Fact]
    public void Activate_SetsIsActiveTrue()
    {
        var user = new User("Ada", "ada", "ada@test", "hash", UserRole.Support);
        user.Deactivate();
        Assert.False(user.IsActive);

        user.Activate();

        Assert.True(user.IsActive);
    }

    [Fact]
    public void UpdateProfile_TrimsAndSetsFullNameAndEmail()
    {
        var user = new User("Old", "ada", "old@test", "hash", UserRole.Support);

        user.UpdateProfile("  Ada Lovelace  ", "  ada@example.test  ");

        Assert.Equal("Ada Lovelace", user.FullName);
        Assert.Equal("ada@example.test", user.Email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateProfile_RejectsEmptyFullName(string? fullName)
    {
        var user = new User("Ada", "ada", "ada@test", "hash", UserRole.Support);

        var ex = Assert.Throws<ArgumentException>(() => user.UpdateProfile(fullName!, "ada@test"));

        Assert.Equal("fullName", ex.ParamName);
        Assert.Equal("Ada", user.FullName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateProfile_RejectsEmptyEmail(string? email)
    {
        var user = new User("Ada", "ada", "ada@test", "hash", UserRole.Support);

        var ex = Assert.Throws<ArgumentException>(() => user.UpdateProfile("Ada", email!));

        Assert.Equal("email", ex.ParamName);
        Assert.Equal("ada@test", user.Email);
    }
}
