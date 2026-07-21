using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VSHelpDesk.Application.Features.Parameters.GetParameterAudit;
using VSHelpDesk.Application.Features.Parameters.GetParameters;
using VSHelpDesk.Application.Features.Parameters.UpdateParameter;
using VSHelpDesk.WebAPI.Contracts.Parameters;

namespace VSHelpDesk.WebAPI.Controllers;

/// <summary>
/// Application parameters (UC-010, BR-016).
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/parameters")]
public sealed class ParametersController(
    GetParametersHandler getParametersHandler,
    GetParameterAuditHandler getParameterAuditHandler,
    UpdateParameterHandler updateParameterHandler) : ControllerBase
{
    /// <summary>GET api/parameters — UC-010 list allowlisted parameters.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var items = await getParametersHandler.HandleAsync(cancellationToken);
        return Ok(items);
    }

    /// <summary>GET api/parameters/audit — recent parameter change history.</summary>
    [HttpGet("audit")]
    public async Task<IActionResult> GetAudit(
        [FromQuery] string? key,
        [FromQuery] int take = GetParameterAuditHandler.DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var items = await getParameterAuditHandler.HandleAsync(
            new GetParameterAuditQuery(key, take),
            cancellationToken);
        return Ok(items);
    }

    /// <summary>PUT api/parameters/{key} — UC-010 update parameter value.</summary>
    [HttpPut("{key}")]
    public async Task<IActionResult> Update(
        string key,
        [FromBody] UpdateParameterRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await updateParameterHandler.HandleAsync(
            new UpdateParameterCommand(key, request?.Value ?? string.Empty),
            cancellationToken);
        return Ok(result);
    }
}
