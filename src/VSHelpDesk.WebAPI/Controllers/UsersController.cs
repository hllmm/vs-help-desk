using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VSHelpDesk.Application.Features.Users.CreateUser;
using VSHelpDesk.Application.Features.Users.GetUsers;
using VSHelpDesk.Application.Features.Users.SetUserPassword;
using VSHelpDesk.Application.Features.Users.UpdateUser;
using VSHelpDesk.WebAPI.Contracts.Users;

namespace VSHelpDesk.WebAPI.Controllers;

/// <summary>
/// Admin user management (list, create, update profile/role/active, set password).
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/users")]
public sealed class UsersController(
    GetUsersHandler getUsersHandler,
    CreateUserHandler createUserHandler,
    UpdateUserHandler updateUserHandler,
    SetUserPasswordHandler setUserPasswordHandler) : ControllerBase
{
    /// <summary>GET api/users — list portal users (no password hashes).</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var items = await getUsersHandler.HandleAsync(cancellationToken);
        return Ok(items);
    }

    /// <summary>POST api/users — create Support or Admin account.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateUserRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { code = "request-required" });
        }

        var created = await createUserHandler.HandleAsync(
            new CreateUserCommand(
                request.FullName,
                request.Username,
                request.Email,
                request.Password,
                request.Role),
            cancellationToken);

        return Created($"/api/users/{created.Id}", created);
    }

    /// <summary>PUT api/users/{id} — update profile, role, active (last-admin guarded).</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateUserRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { code = "request-required" });
        }

        var updated = await updateUserHandler.HandleAsync(
            new UpdateUserCommand(
                id,
                request.FullName,
                request.Email,
                request.Role,
                request.IsActive),
            cancellationToken);

        return Ok(updated);
    }

    /// <summary>POST api/users/{id}/password — admin set/reset password.</summary>
    [HttpPost("{id:guid}/password")]
    public async Task<IActionResult> SetPassword(
        Guid id,
        [FromBody] SetUserPasswordRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { code = "request-required" });
        }

        await setUserPasswordHandler.HandleAsync(
            new SetUserPasswordCommand(id, request.Password),
            cancellationToken);

        return NoContent();
    }
}
