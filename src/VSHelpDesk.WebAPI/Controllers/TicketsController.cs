using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VSHelpDesk.WebAPI.Controllers;

/// <summary>
/// Ticket portal endpoints. List/Detail/Reply — Hafta 3; Resolve — Hafta 4.
/// </summary>
[ApiController]
[Authorize]
[Route("api/tickets")]
public sealed class TicketsController : ControllerBase
{
    /// <summary>GET api/tickets — UC-003</summary>
    [HttpGet]
    public IActionResult GetList()
        => StatusCode(StatusCodes.Status501NotImplemented, new { message = "Hafta 3: GetTicketList (UC-003)." });

    /// <summary>GET api/tickets/{id} — UC-004</summary>
    [HttpGet("{id:guid}")]
    public IActionResult GetById(Guid id)
        => StatusCode(StatusCodes.Status501NotImplemented, new { message = $"Hafta 3: GetTicketDetails (UC-004) id={id}." });

    /// <summary>POST api/tickets/{id}/replies — UC-005</summary>
    [HttpPost("{id:guid}/replies")]
    public IActionResult Reply(Guid id)
        => StatusCode(StatusCodes.Status501NotImplemented, new { message = $"Hafta 3: ReplyToTicket (UC-005) id={id}." });

    /// <summary>POST api/tickets/{id}/resolve — UC-007</summary>
    [HttpPost("{id:guid}/resolve")]
    public IActionResult Resolve(Guid id)
        => StatusCode(StatusCodes.Status501NotImplemented, new { message = $"Hafta 4: ResolveTicket (UC-007) id={id}." });

    /// <summary>POST api/tickets/{id}/assign — BR-011</summary>
    [HttpPost("{id:guid}/assign")]
    public IActionResult Assign(Guid id)
        => StatusCode(StatusCodes.Status501NotImplemented, new { message = $"Hafta 3: AssignTicket (BR-011) id={id}." });
}
