using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.Infrastructure.Persistence;
using VSHelpDesk.Infrastructure.Persistence.Repositories;

namespace VSHelpDesk.Infrastructure.IntegrationTests;

public sealed class UserRepositoryContractTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public UserRepositoryContractTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new ApplicationDbContext(_options);
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task AddAsync_And_GetByUsernameAsync_PersistsAndRetrievesUser()
    {
        await using var writeContext = new ApplicationDbContext(_options);
        var repo = new EfUserRepository(writeContext);

        var user = new User(
            "Test User",
            "testuser",
            "testuser@example.test",
            "hashedpass",
            UserRole.Support);

        await repo.AddAsync(user);
        await writeContext.SaveChangesAsync();

        await using var readContext = new ApplicationDbContext(_options);
        var readRepo = new EfUserRepository(readContext);

        var loaded = await readRepo.GetByUsernameAsync("testuser");
        Assert.NotNull(loaded);
        Assert.Equal("testuser@example.test", loaded.Email);
        Assert.Equal(UserRole.Support, loaded.Role);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
