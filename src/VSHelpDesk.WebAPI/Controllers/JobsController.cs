using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VSHelpDesk.Application.Features.MailProcessing.ProcessIncomingEmails;
using VSHelpDesk.WebAPI.Filters;

namespace VSHelpDesk.WebAPI.Controllers;

/// <summary>
/// External scheduler targets (SRD §6.5). Implementation — Hafta 2 (mail) / Hafta 4 (auto-resolve).
/// Protected with <c>X-Jobs-Api-Key</c> (Jobs:ApiKey).
/// </summary>
[ApiController]
[Route("api/jobs")]
[AllowAnonymous]
[ServiceFilter(typeof(JobsApiKeyAuthorizationFilter))]
public sealed class JobsController(ProcessIncomingEmailsHandler processIncomingEmailsHandler) : ControllerBase
{
    /// <summary>POST api/jobs/process-incoming-emails — UC-002 boundary (Day 8 fetch/probe; Day 9 ticket create).</summary>
    [HttpPost("process-incoming-emails")]
    public async Task<IActionResult> ProcessIncomingEmails(CancellationToken cancellationToken)
    {
        var result = await processIncomingEmailsHandler.HandleAsync(
            new ProcessIncomingEmailsCommand(),
            cancellationToken);

        if (result.IsFailure)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { message = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>POST api/jobs/resolve-inactive-tickets — UC-008 / BR-008</summary>
    [HttpPost("resolve-inactive-tickets")]
    public IActionResult ResolveInactiveTickets()
        => StatusCode(StatusCodes.Status501NotImplemented, new { message = "Hafta 4: ResolveInactiveTickets (UC-008)." });
}
