using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence.Repositories;

public sealed class EfUserRepository(ApplicationDbContext dbContext) : IUserRepository
{
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await dbContext.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        return await dbContext.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
    }

    public IQueryable<User> GetListQueryable()
    {
        return dbContext.Users;
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        await dbContext.Users.AddAsync(user, cancellationToken);
    }

    public void Update(User user)
    {
        dbContext.Users.Update(user);
    }
}
