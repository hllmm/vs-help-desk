using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Abstractions.Persistence.Repositories;

public interface IPortalTicketRequestRepository
{
    Task<PortalTicketRequest?> GetByUserAndKeyAsync(
        Guid userId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        PortalTicketRequest request,
        CancellationToken cancellationToken = default);
}
