using VSHelpDesk.Application.Abstractions.Persistence.Repositories;

namespace VSHelpDesk.Application.Features.Parameters.GetParameterAudit;

public sealed class GetParameterAuditHandler(
    IApplicationParameterRepository parameterRepository,
    IUserRepository userRepository)
{
    public const int DefaultTake = 50;
    public const int MaxTake = 100;

    public Task<IReadOnlyList<ParameterChangeLogDto>> HandleAsync(
        GetParameterAuditQuery query,
        CancellationToken cancellationToken = default)
    {
        var take = query.Take <= 0 ? DefaultTake : Math.Min(query.Take, MaxTake);
        var keyFilter = string.IsNullOrWhiteSpace(query.Key) ? null : query.Key.Trim();

        // Materialize then join in memory — matches other Application list handlers
        // that avoid EF async projections on the abstraction.
        var logs = parameterRepository.GetChangeLogsQueryable()
            .Where(log => keyFilter == null || log.ParameterKey == keyFilter)
            .OrderByDescending(log => log.ChangedAt)
            .Take(take)
            .ToList();

        var userIds = logs.Select(log => log.ChangedByUserId).Distinct().ToList();
        var usernames = userRepository.GetListQueryable()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.Username })
            .ToList()
            .ToDictionary(row => row.Id, row => row.Username);

        IReadOnlyList<ParameterChangeLogDto> items = logs
            .Select(log => new ParameterChangeLogDto(
                log.Id,
                log.ParameterKey,
                log.OldValue,
                log.NewValue,
                log.ChangedByUserId,
                usernames.TryGetValue(log.ChangedByUserId, out var username) ? username : null,
                log.ChangedAt))
            .ToList();

        return Task.FromResult(items);
    }
}
