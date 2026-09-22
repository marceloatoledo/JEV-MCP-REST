using System.Security.Claims;
using JevMcp.Data;

namespace JevMcp.App;

internal static class CallAuditOrigin
{
    public static void Apply(CallAuditScope scope, ClaimsPrincipal? user)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (user is null)
        {
            return;
        }

        long? accessTokenId = null;
        var idClaim = user.FindFirst(AccessClaims.TokenId)?.Value;
        if (!string.IsNullOrEmpty(idClaim) && long.TryParse(idClaim, out var parsed))
        {
            accessTokenId = parsed;
        }

        scope.SetOrigin(
            user.FindFirst(AccessClaims.TokenName)?.Value,
            user.FindFirst(AccessClaims.TokenPrefix)?.Value,
            accessTokenId);
    }
}
