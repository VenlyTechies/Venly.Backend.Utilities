using Microsoft.AspNetCore.Authorization;
using Venly.Backend.Common.Authentication;
using Venly.Backend.Common.Errors;
namespace Venly.Backend.Common;
/// <summary>
/// A client route for a signed-in Eventspaces person. The policy checks the token's principal_type, so a SendGram
/// customer or staff token, which AuthService signs with the same key, is refused here.
/// </summary>
[Authorize(Policy = SendGramAuth.EventspacesUserOnlyPolicy)]
public abstract class EventspacesUserController : ClientController
{
    /// <summary>The subject comes from the validated token and never from the path or body, so there is nothing to tamper with.</summary>
    protected string UserId =>
        User.FindFirst(SendGramAuth.SubjectClaim)?.Value
        ?? throw UnauthorizedException.MissingClaim(SendGramAuth.SubjectClaim).DumpLocation();
}
