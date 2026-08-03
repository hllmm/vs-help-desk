using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence.Repositories;

public sealed class EfProcessedEmailRepository(ApplicationDbContext dbContext) : IProcessedEmailRepository
{
    public async Task<ProcessedEmailMessage?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        return await dbContext.ProcessedEmailMessages.FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public async Task<ProcessedEmailMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.ProcessedEmailMessages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task AddAsync(ProcessedEmailMessage message, CancellationToken cancellationToken = default)
    {
        await dbContext.ProcessedEmailMessages.AddAsync(message, cancellationToken);
    }

    public IQueryable<ProcessedEmailMessage> GetListQueryable()
    {
        return dbContext.ProcessedEmailMessages;
    }
}
