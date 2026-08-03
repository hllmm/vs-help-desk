using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Application.Abstractions.Persistence.Repositories;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence.Repositories;

public sealed class EfApplicationParameterRepository(ApplicationDbContext dbContext) : IApplicationParameterRepository
{
    public async Task<ApplicationParameter?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return await dbContext.ApplicationParameters.FirstOrDefaultAsync(p => p.Key == code, cancellationToken);
    }

    public async Task<ApplicationParameter?> GetByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        return await dbContext.ApplicationParameters.FirstOrDefaultAsync(p => p.Key == key, cancellationToken);
    }

    public async Task<IReadOnlyList<ApplicationParameter>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.ApplicationParameters.ToListAsync(cancellationToken);
    }

    public async Task AddChangeLogAsync(ParameterChangeLog changeLog, CancellationToken cancellationToken = default)
    {
        await dbContext.ParameterChangeLogs.AddAsync(changeLog, cancellationToken);
    }

    public void Update(ApplicationParameter parameter)
    {
        dbContext.ApplicationParameters.Update(parameter);
    }

    public IQueryable<ParameterChangeLog> GetChangeLogsQueryable()
    {
        return dbContext.ParameterChangeLogs;
    }
}
