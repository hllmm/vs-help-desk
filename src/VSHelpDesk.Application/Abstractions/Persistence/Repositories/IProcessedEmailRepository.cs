using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Abstractions.Persistence.Repositories;

public interface IProcessedEmailRepository
{
    Task<ProcessedEmailMessage?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    Task<ProcessedEmailMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(ProcessedEmailMessage message, CancellationToken cancellationToken = default);

    IQueryable<ProcessedEmailMessage> GetListQueryable();
}
