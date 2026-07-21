namespace VSHelpDesk.Application.Features.Parameters.GetParameters;

public sealed record ParameterDto(
    string Key,
    string Value,
    string Description,
    DateTime UpdatedAt);
