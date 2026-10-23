using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Venly.Backend.Common;

/// <summary>The body of a health response. A shape rather than a bare string so it can be documented.</summary>
public sealed record HealthResponse(string Status);

/// <summary>
/// <c>GET /health</c> for every service, and the reason it is a CONTROLLER rather than
/// <c>app.MapHealthChecks("/health")</c>: MapHealthChecks registers an endpoint with no ApiExplorer metadata,
/// so Swashbuckle never puts <c>/health</c> in the service's OpenAPI document. Both halves of the gateway's
/// aggregated Swagger UI are built from that document -- the "ALL endpoints" view IS the document, and MMLib's
/// "- v1" view is the same document rewritten through the route table -- so neither can show a path the
/// document does not contain. Health checks answered 200 all along and were invisible in both.
///
/// <para>Shared rather than copied into six services because there is one health contract, not six. It reaches
/// each service through <c>AddApplicationPart</c> beside that service's <c>AddControllers()</c>; a service that
/// forgets the call has no /health at all, which is what HealthEndpointTests fails the build over.</para>
///
/// <para>Anonymous, and published anonymously at the edge. A probe, a load balancer and an uptime monitor have
/// no token to present. The cost is that anyone who reaches the edge can tell which services are up, so this
/// returns the aggregate status and NOTHING else -- no check names, no exception text, no versions, no
/// durations. The detailed view stays on the container's own port.</para>
///
/// <para>Derives from <see cref="BaseController"/> rather than <see cref="ClientController"/> or
/// <see cref="ServiceController"/>: the caller is neither a person nor a service holding a secret.</para>
/// </summary>
[Route("health")]
[AllowAnonymous]
public sealed class HealthController(HealthCheckService health) : BaseController
{
    [HttpGet]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var report = await health.CheckHealthAsync(cancellationToken);

        // The same mapping MapHealthChecks used: Degraded is still serving, so it stays a 200 and only
        // Unhealthy takes the service out of a load balancer's rotation.
        return StatusCode(
            report.Status == HealthStatus.Unhealthy
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status200OK,
            new HealthResponse(report.Status.ToString()));
    }
}
