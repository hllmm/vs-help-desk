namespace VSHelpDesk.Application.Features.Parameters.GetParameterAudit;

public sealed record ParameterChangeLogDto(
    Guid Id,
    string ParameterKey,
    string OldValue,
    string NewValue,
    Guid ChangedByUserId,
    string? ChangedByUsername,
    DateTime ChangedAt);
