using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VSHelpDesk.Application.Features.Attachments.GetAttachment;
using VSHelpDesk.Application.Features.Attachments.UploadAttachment;

namespace VSHelpDesk.WebAPI.Controllers;

/// <summary>
/// Attachment upload/download (BR-012, BR-017).
/// </summary>
[ApiController]
[Authorize]
public sealed class AttachmentsController(
    UploadAttachmentHandler uploadAttachmentHandler,
    GetAttachmentHandler getAttachmentHandler) : ControllerBase
{
    /// <summary>POST api/ticket-messages/{messageId}/attachments</summary>
    [HttpPost("api/ticket-messages/{messageId:guid}/attachments")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        Guid messageId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length <= 0)
        {
            return BadRequest(new { message = "file is required." });
        }

        await using var stream = file.OpenReadStream();
        var result = await uploadAttachmentHandler.HandleAsync(
            new UploadAttachmentCommand(
                messageId,
                file.FileName,
                file.ContentType ?? string.Empty,
                file.Length,
                stream),
            cancellationToken);

        if (result.IsFailure)
        {
            return BadRequest(new { message = result.Error });
        }

        return Ok(result.Value);
    }

    /// <summary>GET api/attachments/{id}</summary>
    [HttpGet("api/attachments/{id:guid}")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var attachment = await getAttachmentHandler.HandleAsync(
            new GetAttachmentQuery(id),
            cancellationToken);

        return File(attachment.Content, attachment.ContentType, attachment.FileName);
    }
}
