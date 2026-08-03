namespace VSHelpDesk.Application.Abstractions.Persistence.Repositories;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    void ClearTrackedChanges();
}
