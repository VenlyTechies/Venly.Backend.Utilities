using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Venly.Backend.Common.Authentication;

namespace Venly.Backend.Common;

/// <summary>
/// Base for a controller a CLIENT reaches — an app or a browser, through the gateway. Authentication is the
/// bearer token AuthService minted, validated against the same parameters the gateway uses.
///
/// The default is closed: inheriting this requires a valid token on every action. A credential ceremony, where
/// the caller cannot yet have a token, opts out per action with <c>[AllowAnonymous]</c> — sign-in, registration,
/// verification, password reset. Those are anonymous because there is no principal yet, NOT because they are
/// unimportant, and each one is declared in GatewayService's <c>permissions.map.json</c> so the edge and the
/// service agree on which they are.
///
/// Where a route is restricted to one kind of principal, prefer
/// <c>[Authorize(Policy = SendGramAuth.StaffOnlyPolicy)]</c> on the action over the class default: a valid
/// customer token is still a valid token, and a staff-only surface that merely requires "authenticated" is
/// reachable by any customer who has signed in.
/// </summary>
[Authorize(AuthenticationSchemes = SendGramAuth.BearerScheme)]
public abstract class ClientController : BaseController
{
    /// <summary>
    /// Refuses the request unless the caller has verified to at least <paramref name="tier"/>, returning the
    /// refusal to send back or null to carry on.
    ///
    /// <para>
    /// **403 and not 402 or 409**, and the reason it says so out loud: the customer is perfectly well
    /// authenticated and the account is perfectly well formed — they are simply not permitted this yet. The
    /// message names Tier 1 and what clears it, because a requirement discovered as a bare refusal is a
    /// support ticket, and the rule everywhere else in this product is that a limit must be visible before it
    /// binds rather than met by being blocked.
    /// </para>
    /// <para>
    /// Read from the gateway's forwarded header, where a MISSING value is level 0 rather than "unknown, allow"
    /// — see <see cref="CustomerVerificationGate"/>.
    /// </para>
    /// </summary>
    protected IActionResult? RequireVerification(int tier, string message)
    {
        if (CustomerVerificationGate.IsAtLeast(HttpContext, tier))
            return null;

        return StatusCode(
            StatusCodes.Status403Forbidden,
            new RequestResponse<object>
            {
                ResponseCode = StatusCodes.Status403Forbidden,
                ResponseMessage = message,
            });
    }
}
