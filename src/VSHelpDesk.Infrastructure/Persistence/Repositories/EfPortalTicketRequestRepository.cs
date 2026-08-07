using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence.Repositories;

public sealed class EfPortalTicketRequestRepository(ApplicationDbContext dbContext)
    : IPortalTicketRequestRepository
{
    public async Task<PortalTicketRequest?> GetByUserAndKeyAsync(
        Guid userId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.PortalTicketRequests
            .FirstOrDefaultAsync(
                request => request.UserId == userId &&
                    request.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    public async Task AddAsync(
        PortalTicketRequest request,
        CancellationToken cancellationToken = default)
    {
        await dbContext.PortalTicketRequests.AddAsync(request, cancellationToken);
    }
}
