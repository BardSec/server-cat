using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace ServerCat.Api.Middleware;

/// <summary>
/// Global exception handler middleware.
/// Returns RFC 7807 Problem Details for all unhandled exceptions.
/// Logs the full stack trace server-side; returns only safe details to the client.
/// </summary>
public class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = context.TraceIdentifier;
        logger.LogError(exception, "Unhandled exception for request {TraceId} {Method} {Path}",
            traceId, context.Request.Method, context.Request.Path);

        var (status, title) = exception switch
        {
            InvalidOperationException => (StatusCodes.Status400BadRequest, "Invalid Operation"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };

        var problem = new ProblemDetails
        {
            Type = $"https://servercat.local/errors/{status}",
            Title = title,
            Status = status,
            Detail = status < 500 ? exception.Message : "An unexpected error occurred. Check the server logs.",
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = traceId;

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }
}
