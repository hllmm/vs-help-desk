using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VSHelpDesk.Application.Features.Tickets.GetTicketDetails;
using VSHelpDesk.Application.Features.Tickets.GetTicketList;
using VSHelpDesk.Domain.Enums;

namespace VSHelpDesk.WebAPI.Controllers;

/// <summary>
/// Ticket portal endpoints. List/Detail — Hafta 3 Day 11; Reply — Day 12; Resolve — Hafta 4.
/// </summary>
[ApiController]
[Authorize]
[Route("api/tickets")]
public sealed class TicketsController(
    GetTicketListHandler getTicketListHandler,
    GetTicketDetailsHandler getTicketDetailsHandler) : ControllerBase
{
    /// <summary>GET api/tickets — UC-003</summary>
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] TicketStatus? status,
        CancellationToken cancellationToken)
    {
        var items = await getTicketListHandler.HandleAsync(
            new GetTicketListQuery(status),
            cancellationToken);
        return Ok(items);
    }

    /// <summary>GET api/tickets/{id} — UC-004</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var details = await getTicketDetailsHandler.HandleAsync(
            new GetTicketDetailsQuery(id),
            cancellationToken);
        return Ok(details);
    }

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
