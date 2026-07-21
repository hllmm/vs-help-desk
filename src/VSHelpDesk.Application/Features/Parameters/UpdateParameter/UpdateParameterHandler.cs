using VSHelpDesk.Application.Abstractions.Authentication;
using VSHelpDesk.Application.Abstractions.Parameters;
using VSHelpDesk.Application.Abstractions.Persistence;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Application.Features.Parameters.GetParameters;
using VSHelpDesk.Domain.Entities;
using VSHelpDesk.Domain.Exceptions;

namespace VSHelpDesk.Application.Features.Parameters.UpdateParameter;

public sealed class UpdateParameterHandler(
    IApplicationDbContext applicationDbContext,
    IApplicationParameterReader reader,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider)
{
    public async Task<ParameterDto> HandleAsync(
        UpdateParameterCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!currentUserService.IsAuthenticated
            || currentUserService.UserId is not Guid userId
            || userId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException();
        }

        if (!ApplicationParameterCatalog.TryValidate(command.Key, command.Value, out var errorCode))
        {
            if (errorCode == ParameterCodes.KeyUnknown)
            {
                throw new NotFoundException($"Parameter '{command.Key}' was not found.");
            }

            throw new DomainException(errorCode ?? ParameterCodes.ValueInvalid);
        }

        await reader.EnsureCatalogAsync(cancellationToken);

        var entity = applicationDbContext.ApplicationParameters
            .FirstOrDefault(parameter => parameter.Key == command.Key)
            ?? throw new NotFoundException($"Parameter '{command.Key}' was not found.");

        var oldValue = entity.Value;
        var newValue = command.Value.Trim();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        entity.UpdateValue(newValue, now);
        applicationDbContext.Add(new ParameterChangeLog(
            entity.Key,
            oldValue,
            newValue,
            userId,
            now));
        await applicationDbContext.SaveChangesAsync(cancellationToken);

        return new ParameterDto(
            entity.Key,
            entity.Value,
            entity.Description,
            entity.UpdatedAt);
    }
}
