namespace Venly.Backend.Common.Authentication;

/// <summary>
/// The headers the gateway asserts about the caller, and the one it merely forwards.
///
/// A downstream service trusts these because the hop is HMAC-signed and because the gateway's scrubbing
/// middleware removes any inbound copy — a caller can neither forge one nor have theirs survive.
///
/// <para>These names used to live in GatewayService. They moved here when Venly.Audit.Helper became a second
/// reader of them: the gateway writes them, every audited service reads them, and two copies of the list is
/// exactly the drift <c>service-conventions.md</c> exists to prevent. The names carry no domain content, so
/// this is where they belong.</para>
/// </summary>
public static class PrincipalHeaders
{
    /// <summary>The subject of the caller's token — a staff account id or a customer id.</summary>
    public const string Id = "X-SendGram-Principal";

    /// <summary>Either <c>staff</c> or <c>customer</c>, matching the token's <c>principal_type</c> claim.</summary>
    public const string Type = "X-SendGram-Principal-Type";

    /// <summary>
    /// The permission key the gateway CHECKED and allowed for this request. Not the caller's whole permission
    /// set: forwarding that would put a header of unbounded size on every hop, and the interesting fact for an
    /// audit record is which permission authorised THIS action.
    /// </summary>
    public const string Permission = "X-SendGram-Permission";

    /// <summary>
    /// SHA-256 of the caller's address. Hashed at the gateway, because a downstream service sees only the
    /// gateway's own address and because a raw address in an append-only table is personal data that can never
    /// be redacted.
    /// </summary>
    public const string SourceIpHash = "X-SendGram-Source-Ip-Hash";

    /// <summary>
    /// The customer's verification level — "0", "1", "2" or "3" — taken from the token's
    /// <c>verification_tier</c> claim. Absent for a staff principal, which does not have one.
    ///
    /// <para>
    /// It is forwarded so that a service which moves money can refuse an unverified account without calling
    /// CustomerService first. The alternative is a lookup on every quote, transfer and funding attempt, on the
    /// hot path, to answer a question the token already carries.
    /// </para>
    /// <para>
    /// It is in <see cref="All"/> — scrubbed on the way in — for the obvious reason: a header that grants the
    /// right to move money is exactly the one a caller would like to assert for themselves.
    /// </para>
    /// </summary>
    public const string VerificationTier = "X-SendGram-Verification-Tier";

    /// <summary>
    /// The request correlation id, produced by the gateway's CorrelationId middleware and forwarded by Ocelot
    /// like any other request header. Deliberately absent from <see cref="All"/>: unlike the four above, a
    /// caller supplying their own is legitimate — it is how a client ties its own logs to ours — so it must
    /// NOT be scrubbed.
    /// </summary>
    public const string CorrelationId = "X-Correlation-ID";

    /// <summary>
    /// The headers no caller may assert. The scrubbing middleware strips every one of these on the way in.
    /// </summary>
    public static readonly string[] All = [Id, Type, Permission, SourceIpHash, VerificationTier];
}
