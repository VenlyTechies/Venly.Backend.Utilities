using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Venly.Backend.Common.Authentication;

/// <summary>
/// The one place the client-facing authentication contract is defined, shared by the gateway and by every
/// service that serves a client surface.
///
/// The division this enforces: <b>HMAC authenticates a SERVICE, a bearer token authenticates a PERSON.</b> A
/// signed request proves the caller holds a service's <c>HmacSettings:Secret</c> — the gateway, or a peer service
/// using one of the Venly.*.Helper clients. It says nothing about which human is on the other end, and it is
/// therefore the wrong instrument for a client-facing route. A client presents a bearer token minted by
/// AuthService, and the service validates it here.
///
/// Every parameter below matches GatewayService's JwtAuthenticationSetup exactly, and that is not incidental: a
/// token accepted at the edge and rejected one hop later — or the reverse — is the failure mode this shared type
/// exists to make impossible.
/// </summary>
public static class SendGramAuth
{
    /// <summary>
    /// Also the value route files pass to Ocelot as <c>AuthenticationOptions.AuthenticationProviderKey</c>, so it
    /// is coupled to configuration in the gateway's Routes folder as well as to code.
    /// </summary>
    public const string BearerScheme = "SendGramBearer";

    /// <summary>
    /// The subject — who the token was minted for. Read it as this literal, never as
    /// <c>ClaimTypes.NameIdentifier</c>: <see cref="AddSendGramJwtAuthentication"/> sets
    /// <c>MapInboundClaims = false</c> precisely so the name the token carries survives, and code that reaches
    /// for the mapped WS-Federation URI instead gets null and silently treats an authenticated caller as
    /// anonymous.
    /// </summary>
    public const string SubjectClaim = "sub";

    /// <summary>
    /// Duplicated from AuthService's <c>SendGramClaimTypes.PrincipalType</c> rather than referenced: services
    /// must not take a project reference on AuthService. PrincipalTypeClaimTests pins the two to the same string.
    /// </summary>
    public const string PrincipalTypeClaim = "principal_type";

    public const string StaffPrincipal = "staff";

    public const string CustomerPrincipal = "customer";

    /// <summary>Requires a valid token whose <c>principal_type</c> is <c>staff</c>.</summary>
    public const string StaffOnlyPolicy = "SendGram.StaffOnly";

    /// <summary>Requires a valid token whose <c>principal_type</c> is <c>customer</c>.</summary>
    public const string CustomerOnlyPolicy = "SendGram.CustomerOnly";

    public const string ConfigurationSectionName = "Authorization";

    /// <summary>
    /// Registers the bearer scheme and the two principal-type policies.
    ///
    /// <paramref name="configuration"/> must supply <c>Authorization:Issuer</c> and
    /// <c>Authorization:MetadataAddress</c>. They are separate settings and are routinely DIFFERENT: the issuer
    /// is the value AuthService stamps into <c>iss</c> and is the address a client outside the deployment uses,
    /// while the metadata address is wherever this process can actually reach AuthService — a container name
    /// under Compose. Both are required rather than defaulted, because a wrong guess here rejects every token
    /// with a message about signing keys.
    /// </summary>
    public static IServiceCollection AddSendGramJwtAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigurationSectionName);

        var issuer = section["Issuer"];
        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw new InvalidOperationException(
                $"{ConfigurationSectionName}:Issuer is not configured. It must equal the 'iss' AuthService "
                + "stamps into a token — AuthService's own Authorization:Issuer — or every client request to "
                + "this service is rejected.");
        }

        // DEV-ONLY ESCAPE HATCH. Normal operation fetches the signing keys from AuthService's discovery
        // document at MetadataAddress. Where that address is unreachable (local work against an AuthService
        // whose JWKS lives behind a cluster-internal host), a JWKS JSON can be supplied inline and metadata is
        // skipped. This is OFF unless BOTH DevStaticValidation is true AND DevSigningKeysJwks is non-empty, so a
        // deploy that sets neither behaves exactly as before. Everything else about validation — issuer,
        // lifetime, no audience, zero skew — is identical on both paths, so a token accepted in dev is accepted
        // the same way in production.
        var devStaticValidation = section.GetValue("DevStaticValidation", false);
        var devJwks = section["DevSigningKeysJwks"];
        var useDevKeys = devStaticValidation && !string.IsNullOrWhiteSpace(devJwks);

        var metadataAddress = section["MetadataAddress"];
        if (string.IsNullOrWhiteSpace(metadataAddress) && !useDevKeys)
        {
            throw new InvalidOperationException(
                $"{ConfigurationSectionName}:MetadataAddress is not configured. This is where this service "
                + "fetches AuthService's discovery document to get the signing keys, and it is an address "
                + "routable from THIS process — not necessarily the issuer. (Alternatively, set "
                + $"{ConfigurationSectionName}:DevStaticValidation=true and supply "
                + $"{ConfigurationSectionName}:DevSigningKeysJwks for local work without reachable metadata.)");
        }

        IList<SecurityKey> devSigningKeys = [];
        if (useDevKeys)
        {
            // Parse the JWKS once at startup. A malformed dev JWKS should fail loudly here, not silently reject
            // every token at runtime.
            devSigningKeys = new JsonWebKeySet(devJwks!).GetSigningKeys();
            if (devSigningKeys.Count == 0)
                throw new InvalidOperationException(
                    $"{ConfigurationSectionName}:DevSigningKeysJwks parsed to zero signing keys.");
        }

        services.AddAuthentication()
            .AddJwtBearer(BearerScheme, jwt =>
            {
                if (useDevKeys)
                {
                    // No metadata fetch: validate against the supplied keys directly.
                    jwt.RequireHttpsMetadata = false;
                }
                else
                {
                    // Non-null here: the guard above throws when metadata is absent and dev keys are not used.
                    jwt.MetadataAddress = metadataAddress!;
                    jwt.RequireHttpsMetadata = section.GetValue("RequireHttpsMetadata", true);
                }

                // Keep the claim names the token actually carries. The default inbound map renames 'sub' to the
                // WS-Federation nameidentifier URI, so FindFirstValue("sub") comes back null and an authorised
                // request is denied for want of a subject — failing closed, but for entirely the wrong reason.
                jwt.MapInboundClaims = false;

                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,

                    // AuthService registers no audience, so there is nothing to validate against and demanding
                    // one would reject every token it mints.
                    ValidateAudience = false,

                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    // No leeway. The default five minutes would keep a 15-minute access token working for
                    // twenty.
                    ClockSkew = TimeSpan.Zero,
                };

                // In dev-keys mode the keys come from config, not the metadata endpoint.
                if (useDevKeys)
                    jwt.TokenValidationParameters.IssuerSigningKeys = devSigningKeys;
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(StaffOnlyPolicy, policy => policy
                .AddAuthenticationSchemes(BearerScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(PrincipalTypeClaim, StaffPrincipal))
            .AddPolicy(CustomerOnlyPolicy, policy => policy
                .AddAuthenticationSchemes(BearerScheme)
                .RequireAuthenticatedUser()
                .RequireClaim(PrincipalTypeClaim, CustomerPrincipal));

        return services;
    }
}
