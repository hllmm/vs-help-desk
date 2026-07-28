namespace VSHelpDesk.Application.Abstractions.Persistence;

public interface IUserAdministrationTransaction
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
