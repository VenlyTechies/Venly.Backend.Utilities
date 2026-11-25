using Microsoft.AspNetCore.Authorization;
using Venly.Backend.Common.Authentication;
using Venly.Backend.Common.Errors;
namespace Venly.Backend.Common;
/// <summary>
/// A back-office route for a signed-in staff member. The policy checks the token's principal_type, so an Eventspaces
/// person or a SendGram customer, whose tokens AuthService signs with the same key, is refused here. Which staff
/// permission the route needs is declared and enforced at the gateway; this class establishes that the caller is staff
/// and who they are.
/// </summary>
[Authorize(Policy = SendGramAuth.StaffOnlyPolicy)]
public abstract class StaffController : ClientController
{
    /// <summary>The staff id comes from the validated token and never from the path or body.</summary>
    protected string StaffId =>
        User.FindFirst(SendGramAuth.SubjectClaim)?.Value
        ?? throw UnauthorizedException.MissingClaim(SendGramAuth.SubjectClaim).DumpLocation();
}
