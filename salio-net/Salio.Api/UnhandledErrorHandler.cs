using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Salio.Api.Security;

namespace Salio.Api;

/// <summary>
/// What a client sees when something breaks that no controller expected: a
/// plain sentence and a short code, never a stack trace, a file path, SQL
/// or a connection string. The same code is written to the log next to the
/// full error, so "tell support this code" leads straight to the cause.
///
/// Registered in Program.cs with AddExceptionHandler and UseExceptionHandler
/// (built into ASP.NET Core). A request the client itself gave up on never
/// reaches this: the framework ends it quietly, without an error in the log.
/// </summary>
public class UnhandledErrorHandler : IExceptionHandler
{
    public const string Title = "Something went wrong";

    private readonly ILogger<UnhandledErrorHandler> _logger;

    public UnhandledErrorHandler(ILogger<UnhandledErrorHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        string code = NewCode();

        // Who, which shop, and what they were doing. The shop is the one the
        // request named in its address, if it named one. Only the path is
        // logged, never the query string or the body: a body can hold a
        // password or a PIN.
        string userId = OrganizationGuard.TryGetUserId(httpContext.User, out Guid id)
            ? id.ToString()
            : "nobody signed in";

        string shop = httpContext.Request.Query["organizationId"].ToString();

        if (!Guid.TryParse(shop, out _))
        {
            shop = "not named in the address";
        }

        _logger.LogError(
            exception,
            "Unhandled error {ErrorCode}: {Method} {Path} failed. User: {UserId}. Shop: {OrganizationId}.",
            code,
            httpContext.Request.Method,
            httpContext.Request.Path.Value,
            userId,
            shop);

        var problem = new ProblemDetails
        {
            Title = Title,
            Detail = DetailFor(code),
            Status = StatusCodes.Status500InternalServerError
        };

        // So the till can show or copy the code without reading the sentence.
        problem.Extensions["code"] = code;

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        // True: handled. Nothing else writes to this response.
        return true;
    }

    public static string DetailFor(string code)
    {
        return "Salio could not do that. Nothing was saved. Try again; "
            + "if it keeps happening, tell support this code: " + code + ".";
    }

    // Eight characters, easy to read out over the phone. Random, so it says
    // nothing about the server.
    public static string NewCode()
    {
        return Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    }
}
