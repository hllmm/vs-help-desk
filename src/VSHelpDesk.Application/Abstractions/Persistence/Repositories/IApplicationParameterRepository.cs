using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Application.Abstractions.Persistence.Repositories;

public interface IApplicationParameterRepository
{
    Task<ApplicationParameter?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<ApplicationParameter?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApplicationParameter>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddChangeLogAsync(ParameterChangeLog changeLog, CancellationToken cancellationToken = default);

    void Update(ApplicationParameter parameter);

    IQueryable<ParameterChangeLog> GetChangeLogsQueryable();
}
