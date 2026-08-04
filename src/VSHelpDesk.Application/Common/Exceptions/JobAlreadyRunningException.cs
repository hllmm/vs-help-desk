
using VSHelpDesk.Application.Common.Localization;

namespace VSHelpDesk.Application.Common.Exceptions;

public sealed class JobAlreadyRunningException(string jobName, string? customMessage = null)
    : ConflictApplicationException(customMessage ?? LocalizedApplicationMessages.JobAlreadyRunning(jobName));
