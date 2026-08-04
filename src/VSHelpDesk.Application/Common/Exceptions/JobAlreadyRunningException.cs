namespace VSHelpDesk.Application.Common.Exceptions;

public sealed class JobAlreadyRunningException(string jobName, string? customMessage = null)
    : ConflictApplicationException(customMessage ?? $"'{jobName}' işi zaten çalışıyor.");
