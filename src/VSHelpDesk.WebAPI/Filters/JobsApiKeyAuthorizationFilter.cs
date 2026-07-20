using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using VSHelpDesk.WebAPI.Options;

namespace VSHelpDesk.WebAPI.Filters;

/// <summary>
/// Requires header <c>X-Jobs-Api-Key</c> matching <see cref="JobsOptions.ApiKey"/>.
/// </summary>
public sealed class JobsApiKeyAuthorizationFilter(IOptions<JobsOptions> jobsOptions) : IAsyncActionFilter
{
    public const string HeaderName = "X-Jobs-Api-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var expected = jobsOptions.Value.ApiKey;
        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var provided) ||
            !FixedTimeEquals(provided.ToString(), expected))
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                message = "Invalid or missing jobs API key."
            });
            return;
        }

        await next();
    }

    private static bool FixedTimeEquals(string provided, string expected)
    {
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        if (providedBytes.Length != expectedBytes.Length)
        {
            // Compare against expected to keep work roughly constant for wrong lengths.
            return CryptographicOperations.FixedTimeEquals(expectedBytes, expectedBytes) && false;
        }

        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
