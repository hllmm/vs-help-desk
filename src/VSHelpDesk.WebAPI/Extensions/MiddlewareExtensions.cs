using VSHelpDesk.WebAPI.Middleware;

namespace VSHelpDesk.WebAPI.Extensions;

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder app)
        => app.UseMiddleware<ExceptionHandlingMiddleware>();
}
