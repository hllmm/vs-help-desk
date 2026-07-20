using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VSHelpDesk.WebAPI.Controllers;

/// <summary>
/// Attachment download (BR-012). Implementation — Hafta 3.
/// </summary>
[ApiController]
[Authorize]
[Route("api/attachments")]
public sealed class AttachmentsController : ControllerBase
{
    /// <summary>GET api/attachments/{id}</summary>
    [HttpGet("{id:guid}")]
    public IActionResult Download(Guid id)
        => StatusCode(StatusCodes.Status501NotImplemented, new { message = $"Hafta 3: GetAttachment id={id}." });
}
