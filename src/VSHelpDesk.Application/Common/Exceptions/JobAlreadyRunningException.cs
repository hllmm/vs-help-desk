namespace VSHelpDesk.Application.Common.Exceptions;

public sealed class JobAlreadyRunningException(string jobName)
    : ConflictApplicationException(
        $"The '{jobName}' job is already running.");
