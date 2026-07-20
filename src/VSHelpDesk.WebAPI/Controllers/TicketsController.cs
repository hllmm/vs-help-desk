using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VSHelpDesk.Application.Features.Tickets.GetTicketDetails;
using VSHelpDesk.Application.Features.Tickets.GetTicketList;
using VSHelpDesk.Application.Features.Tickets.ReplyToTicket;
using VSHelpDesk.Domain.Enums;
using VSHelpDesk.WebAPI.Contracts.Tickets;

namespace VSHelpDesk.WebAPI.Controllers;

/// <summary>
/// Ticket portal endpoints. List/Detail/Reply — Hafta 3; Resolve — Hafta 4.
/// </summary>
[ApiController]
[Authorize]
[Route("api/tickets")]
public sealed class TicketsController(
    GetTicketListHandler getTicketListHandler,
    GetTicketDetailsHandler getTicketDetailsHandler,
    SupportReplyToTicketHandler supportReplyToTicketHandler) : ControllerBase
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
    public async Task<IActionResult> Reply(
        Guid id,
        [FromBody] ReplyToTicketRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new { code = SupportReplyCodes.ContentRequired });
        }

        var result = await supportReplyToTicketHandler.HandleAsync(
            new SupportReplyToTicketCommand(id, request.Content),
            cancellationToken);

        if (result.IsFailure)
        {
            return BadRequest(new { code = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>POST api/tickets/{id}/resolve — UC-007</summary>
    [HttpPost("{id:guid}/resolve")]
    public IActionResult Resolve(Guid id)
        => StatusCode(StatusCodes.Status501NotImplemented, new { message = $"Hafta 4: ResolveTicket (UC-007) id={id}." });

    /// <summary>POST api/tickets/{id}/assign — BR-011</summary>
    [HttpPost("{id:guid}/assign")]
    public IActionResult Assign(Guid id)
        => StatusCode(StatusCodes.Status501NotImplemented, new { message = $"Hafta 3: AssignTicket (BR-011) id={id}." });
}
