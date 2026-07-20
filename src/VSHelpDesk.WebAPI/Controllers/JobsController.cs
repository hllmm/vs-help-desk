using Microsoft.AspNetCore.Mvc;

namespace VSHelpDesk.WebAPI.Controllers;

/// <summary>
/// External scheduler targets (SRD §6.5). Implementation — Hafta 2 (mail) / Hafta 4 (auto-resolve).
/// Protect with Jobs:ApiKey header in implementation.
/// </summary>
[ApiController]
[Route("api/jobs")]
public sealed class JobsController : ControllerBase
{
    /// <summary>POST api/jobs/process-incoming-emails — UC-002 / UC-006 / UC-009</summary>
    [HttpPost("process-incoming-emails")]
    public IActionResult ProcessIncomingEmails()
        => StatusCode(StatusCodes.Status501NotImplemented, new { message = "Hafta 2: ProcessIncomingEmails." });

    /// <summary>POST api/jobs/resolve-inactive-tickets — UC-008 / BR-008</summary>
    [HttpPost("resolve-inactive-tickets")]
    public IActionResult ResolveInactiveTickets()
        => StatusCode(StatusCodes.Status501NotImplemented, new { message = "Hafta 4: ResolveInactiveTickets (UC-008)." });
}
