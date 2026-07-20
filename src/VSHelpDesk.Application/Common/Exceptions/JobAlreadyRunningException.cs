namespace VSHelpDesk.Application.Common.Exceptions;

public sealed class JobAlreadyRunningException()
    : ConflictApplicationException(
        "The incoming-email job is already running.");
