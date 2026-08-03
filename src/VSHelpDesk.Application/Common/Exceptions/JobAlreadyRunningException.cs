namespace VSHelpDesk.Application.Common.Exceptions;

public sealed class JobAlreadyRunningException(string jobName)
    : ConflictApplicationException(ApplicationMessages.MailProcessing.JobAlreadyRunning(jobName));
