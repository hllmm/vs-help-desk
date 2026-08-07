using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using VSHelpDesk.Application.Features.Tickets.AssignTicket;
using VSHelpDesk.Application.Features.Tickets.CreatePortalTicket;
using VSHelpDesk.Application.Features.Tickets.GetAssignableUsers;
using VSHelpDesk.Application.Features.Tickets.GetTicketDetails;
using VSHelpDesk.Application.Features.Tickets.GetTicketList;
using VSHelpDesk.Application.Features.Tickets.GetTicketMessages;
using VSHelpDesk.Application.Features.Tickets.ReplyToTicket;
using VSHelpDesk.Application.Features.Tickets.ResolveTicket;
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
    GetAssignableUsersHandler getAssignableUsersHandler,
    AssignTicketHandler assignTicketHandler,
    GetTicketListHandler getTicketListHandler,
    GetTicketDetailsHandler getTicketDetailsHandler,
    GetTicketMessagesHandler getTicketMessagesHandler,
    SupportReplyToTicketHandler supportReplyToTicketHandler,
    ResolveTicketHandler resolveTicketHandler,
    CreatePortalTicketHandler createPortalTicketHandler) : ControllerBase
{
    /// <summary>GET api/tickets/assignees — active users eligible for BR-011 assignment.</summary>
    [HttpGet("assignees")]
    public async Task<IActionResult> GetAssignableUsers(
        CancellationToken cancellationToken)
    {
        var users = await getAssignableUsersHandler.HandleAsync(cancellationToken);
        return Ok(users);
    }

    /// <summary>GET api/tickets — UC-003</summary>
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] TicketStatus? status = null,
        [FromQuery] string? search = null,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var page = await getTicketListHandler.HandleAsync(
            new GetTicketListQuery(
                Status: status,
                Search: search,
                PageSize: pageSize,
                Cursor: cursor),
            cancellationToken);
        return Ok(page);
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

    /// <summary>GET api/tickets/{id}/messages — older bounded UC-004 history.</summary>
    [HttpGet("{id:guid}/messages")]
    public async Task<IActionResult> GetMessages(
        Guid id,
        [FromQuery] int pageSize = 100,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        // IQueryCollection drops an empty duplicate when another value is non-empty.
        // Enumerating the raw query preserves every supplied value and its order.
        List<string>? cursorValues = null;
        foreach (var parameter in new QueryStringEnumerable(Request.QueryString.Value ?? string.Empty))
        {
            if (parameter.DecodeName().Span.Equals(
                "cursor".AsSpan(),
                StringComparison.OrdinalIgnoreCase))
            {
                (cursorValues ??= []).Add(parameter.DecodeValue().ToString());
            }
        }

        var suppliedCursor = cursorValues is null
            ? null
            : cursorValues.Count == 1
                ? cursorValues[0]
                : string.Join(',', cursorValues);
        var page = await getTicketMessagesHandler.HandleAsync(
            new GetTicketMessagesQuery(id, pageSize, suppliedCursor),
            cancellationToken);
        return Ok(page);
    }

    /// <summary>PUT api/tickets/{id}/assignee — assign, reassign or clear BR-011 owner.</summary>
    [HttpPut("{id:guid}/assignee")]
    public async Task<IActionResult> Assign(
        Guid id,
        [FromBody] AssignTicketRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { code = "request-required" });
        }

        var result = await assignTicketHandler.HandleAsync(
            new AssignTicketCommand(id, request.UserId),
            cancellationToken);
        return Ok(result);
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
    public async Task<IActionResult> Resolve(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await resolveTicketHandler.HandleAsync(
            new ResolveTicketCommand(id),
            cancellationToken);
        return Ok(result);
    }

    /// <summary>POST api/tickets — create ticket via UI (real E2E)</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateTicketRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { code = "request-required" });
        }

        if (!Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKeyHeader)
            || idempotencyKeyHeader.Count != 1)
        {
            return BadRequest(new { code = CreatePortalTicketErrorCodes.IdempotencyKeyRequired });
        }

        if (!Guid.TryParse(idempotencyKeyHeader[0], out var idempotencyKey))
        {
            return BadRequest(new { code = CreatePortalTicketErrorCodes.IdempotencyKeyInvalid });
        }

        var command = new CreatePortalTicketCommand(
            IdempotencyKey: idempotencyKey.ToString("D"),
            Subject: request.Subject,
            CustomerName: request.CustomerName,
            CustomerEmail: request.CustomerEmail,
            Content: request.Content);

        var result = await createPortalTicketHandler.HandleAsync(command, cancellationToken);
        if (result.IsFailure)
        {
            var payload = new { code = result.Error };
            return result.Error == CreatePortalTicketErrorCodes.PayloadConflict
                ? Conflict(payload)
                : BadRequest(payload);
        }

        if (result.Value!.WasAlreadyProcessed)
        {
            return Ok(result.Value);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Value.TicketId }, result.Value);
    }
}
