using Microsoft.AspNetCore.Http;

namespace Venly.Backend.Common.Authentication;

/// <summary>
/// Whether the calling customer has verified enough to do the thing they are asking for.
///
/// <para>
/// One implementation in the shared library rather than one per service, because it is the same rule
/// everywhere and the failure mode of duplicating it is silent: a service that read the header slightly
/// differently — treating a missing one as verified, say — would become the way round the gate, and nothing
/// would point at it.
/// </para>
/// <para>
/// It reads the gateway's forwarded header rather than the token, matching how every other principal fact is
/// read downstream. The gateway re-resolves the principal on every request and asserts the result; the two
/// must not disagree about what a caller is allowed.
/// </para>
/// </summary>
public static class CustomerVerificationGate
{
    /// <summary>Tier 1 — an identity document seen. The floor for holding or moving money at all.</summary>
    public const int Tier1 = 1;

    /// <summary>
    /// The caller's verification level, or 0 when there is no header.
    ///
    /// <para>
    /// **A missing header is level 0, never "unknown, allow".** That is the whole safety property: a request
    /// that reached a service without the gateway asserting a tier has not been established as verified, and
    /// the only safe reading of "we do not know" is the one that refuses. A staff principal also reads 0 here,
    /// correctly — staff do not transact as customers.
    /// </para>
    /// </summary>
    public static int TierOf(HttpContext? context)
    {
        var raw = context?.Request.Headers[PrincipalHeaders.VerificationTier].ToString();

        return int.TryParse(raw, out var tier) && tier > 0 ? tier : 0;
    }

    /// <summary>Whether the caller is at or above <paramref name="required"/>.</summary>
    public static bool IsAtLeast(HttpContext? context, int required) => TierOf(context) >= required;
}
