using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Abstractions.Persistence.Repositories;

public interface ITicketAttachmentRepository
{
    Task<TicketAttachment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TicketAttachment?> GetByStoredFileNameAsync(string storedFileName, CancellationToken cancellationToken = default);

    Task AddAsync(TicketAttachment attachment, CancellationToken cancellationToken = default);

    void Remove(TicketAttachment attachment);

    IQueryable<TicketAttachment> GetOrphansQueryable();
}
