using System.Net;
using System.Text.Json;
using VSHelpDesk.Application.Common.Exceptions;
using VSHelpDesk.Domain.Exceptions;

namespace VSHelpDesk.WebAPI.Middleware;

/// <summary>
/// Maps domain/application exceptions to HTTP responses. Wire in Program.cs — Hafta 1.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var unwrapped = exception switch
        {
            System.Reflection.TargetInvocationException tie when tie.InnerException is not null => tie.InnerException,
            AggregateException ae when ae.InnerExceptions.Count == 1 => ae.InnerExceptions[0],
            _ => exception
        };

        // Client titles stay stable/non-sensitive; full detail is logged server-side.
        var (statusCode, title) = unwrapped switch
        {
            RequestValidationException => (HttpStatusCode.BadRequest, "The request was invalid."),
            NotFoundException => (HttpStatusCode.NotFound, "The requested resource was not found."),
            UnauthorizedApplicationException => (HttpStatusCode.Unauthorized, "Unauthorized."),
            ConflictApplicationException =>
                (HttpStatusCode.Conflict, "The request conflicts with current state."),
            DomainException => (HttpStatusCode.BadRequest, "A domain rule was violated."),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        if (exception is RequestValidationException requestValidationException)
        {
            _logger.LogInformation(
                "Request validation failed path={RequestPath} code={Code}",
                context.Request.Path,
                requestValidationException.Code);
        }
        else if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception");
        }
        else
        {
            _logger.LogWarning(
                exception,
                "Handled application exception status={Status} detail={Detail}",
                (int)statusCode,
                exception.Message);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        // DomainException.Message carries stable machine codes (e.g. last-admin-required).
        object payload = exception switch
        {
            RequestValidationException validationException => new
            {
                status = (int)statusCode,
                title,
                code = validationException.Code
            },
            DomainException => new
            {
                status = (int)statusCode,
                title,
                code = exception.Message
            },
            _ => new
            {
                status = (int)statusCode,
                title
            }
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
