using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Salio.Domain.Enums;
using Salio.Domain.Services;
using Salio.Infrastructure.Security;

namespace Salio.Api.Security;

/// <summary>
/// What every controller calls before it touches anything (locked rule 9).
/// It turns the cookie into a user id, asks MembershipResolver what that
/// user may touch, and hands back a refusal the caller can return as-is.
///
/// Controllers must use the OrganizationId this returns, never the one the
/// client sent.
/// </summary>
public class OrganizationGuard
{
    private readonly MembershipResolver _resolver;

    public OrganizationGuard(MembershipResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task<MembershipAccessResult> ForAsync(
        ClaimsPrincipal principal,
        Guid? requestedOrganizationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out Guid userId))
        {
            // Should not happen behind [Authorize], but a request with no
            // readable user gets nothing rather than the benefit of the doubt.
            return new MembershipAccessResult
            {
                Outcome = MembershipAccessOutcome.NoMemberships
            };
        }

        return await _resolver.ResolveAsync(userId, requestedOrganizationId, cancellationToken);
    }

    public static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        string? idText = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idText, out userId);
    }

    // The refusal to return when access was not granted.
    public static ObjectResult Deny(MembershipAccessResult access)
    {
        // "Choose a shop" is the caller's next action, not a permission
        // problem, so it is a 400. The other two are refusals: 403.
        if (access.Outcome == MembershipAccessOutcome.MustChooseOrganization)
        {
            return Problem(
                "Say which shop this is for. You belong to more than one.",
                StatusCodes.Status400BadRequest);
        }

        if (access.Outcome == MembershipAccessOutcome.NoMemberships)
        {
            return Problem(
                "This account is not attached to any shop.",
                StatusCodes.Status403Forbidden);
        }

        // NotAMember. The same words whether or not that shop exists, so
        // guessing ids reveals nothing.
        return Problem(
            "You do not have access to that shop.",
            StatusCodes.Status403Forbidden);
    }

    // For the parts of a shop only an Owner may see: money, profit, stock
    // value and tax (locked rule 13).
    public static ObjectResult OwnerOnly()
    {
        return Problem(
            "Only an owner may do that.",
            StatusCodes.Status403Forbidden);
    }

    private static ObjectResult Problem(string detail, int status)
    {
        return new ObjectResult(new ProblemDetails
        {
            Title = "Refused",
            Detail = detail,
            Status = status
        })
        {
            StatusCode = status
        };
    }
}
