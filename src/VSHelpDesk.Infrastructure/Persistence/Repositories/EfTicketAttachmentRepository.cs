using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Domain.Entities;

using VSHelpDesk.Application.Abstractions.Persistence;

namespace VSHelpDesk.Infrastructure.Persistence.Repositories;

public sealed class EfTicketAttachmentRepository(IApplicationDbContext dbContext) : ITicketAttachmentRepository
{
    public async Task<TicketAttachment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.TicketAttachments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<TicketAttachment?> GetByStoredFileNameAsync(string storedFileName, CancellationToken cancellationToken = default)
    {
        return await dbContext.TicketAttachments.FirstOrDefaultAsync(a => a.StoredFileName == storedFileName, cancellationToken);
    }

    public Task AddAsync(TicketAttachment attachment, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        dbContext.Add(attachment);
        return Task.CompletedTask;
    }

    public void Remove(TicketAttachment attachment)
    {
        dbContext.Remove(attachment);
    }

    public IQueryable<TicketAttachment> GetOrphansQueryable()
    {
        return dbContext.TicketAttachments.Where(a => !dbContext.TicketMessages.Select(m => m.Id).Contains(a.TicketMessageId));
    }
}
