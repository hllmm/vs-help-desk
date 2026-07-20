using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VSHelpDesk.WebAPI.Controllers;

/// <summary>
/// Application parameters (UC-010, BR-016). Optional / bonus in internship plan.
/// </summary>
[ApiController]
[Authorize]
[Route("api/parameters")]
public sealed class ParametersController : ControllerBase
{
    [HttpGet]
    public IActionResult GetAll()
        => StatusCode(StatusCodes.Status501NotImplemented, new { message = "Bonus: GetParameters (UC-010)." });

    [HttpPut("{key}")]
    public IActionResult Update(string key)
        => StatusCode(StatusCodes.Status501NotImplemented, new { message = $"Bonus: UpdateParameter key={key}." });
}
