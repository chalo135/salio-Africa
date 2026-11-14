using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Salio.Api.Security;

/// <summary>
/// Marks the endpoints a person may still reach while they owe a password
/// change: /api/auth/me and /api/auth/change-password. Nothing else carries it.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class AllowWhilePasswordMustChangeAttribute : Attribute
{
}

/// <summary>
/// The one place a temporary password is enforced. A person with
/// MustChangePassword true may reach only the endpoints marked
/// [AllowWhilePasswordMustChange]. Every other endpoint that needs a login
/// answers 403, whatever its controller says.
///
/// Endpoints that need no login (sign in, sign out) are left alone: they
/// act for nobody, and blocking them would leave the phone stuck with this
/// person's cookie, unable to sign anyone else in.
/// </summary>
public static class PasswordChangeGate
{
    // Set by the cookie check in Program.cs, which already loads the user
    // from the database on every request. So the flag is always current:
    // the moment the password is changed, the next request is let through.
    public const string MustChangeKey = "salio:mustChangePassword";

    public static IApplicationBuilder UsePasswordChangeGate(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            bool mustChange = context.Items.ContainsKey(MustChangeKey);
            var endpoint = context.GetEndpoint();

            bool needsLogin = endpoint?.Metadata.GetMetadata<IAuthorizeData>() is not null
                && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;

            bool allowed = endpoint?.Metadata.GetMetadata<AllowWhilePasswordMustChangeAttribute>() is not null;

            if (mustChange && needsLogin && !allowed)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;

                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Title = "Choose your password first",
                    Detail = "You are signed in with a temporary password. Choose your own password before doing anything else.",
                    Status = StatusCodes.Status403Forbidden
                });

                return;
            }

            await next();
        });
    }
}
