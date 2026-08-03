using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;

namespace VSHelpDesk.Infrastructure.Persistence.Repositories;

public sealed class EfUnitOfWork(IApplicationDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    public void ClearTrackedChanges()
    {
        dbContext.ClearTrackedChanges();
    }
}
