using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace HaulingTracker.Server.Accounts;

/// <summary>
/// A login made by a tracker ("Open my page") has the player's rights only, also when the player's login is the admin's (Frank plays as zzoega and is the
/// admin): the admin pages need the password. The login is marked, and the mark (and the missing admin role) stays when the login is checked again.
/// </summary>
public static class TrackerLogins
{
    const string Mark = "login";
    const string ByTracker = "tracker";

    /// <summary>The user as his tracker logs him in: his roles without Admin, and the mark.</summary>
    public static async Task<ClaimsPrincipal> PrincipalAsync(SignInManager<IdentityUser> signIn, IdentityUser user)
    {
        var principal = await signIn.CreateUserPrincipalAsync(user);
        Restrict(principal);
        return principal;
    }

    /// <summary>For SecurityStampValidatorOptions.OnRefreshingPrincipal: a login that was made by a tracker stays one when it is checked again.</summary>
    public static Task KeepAsync(SecurityStampRefreshingPrincipalContext context)
    {
        if (context.CurrentPrincipal?.HasClaim(Mark, ByTracker) == true && context.NewPrincipal != null) Restrict(context.NewPrincipal);
        return Task.CompletedTask;
    }

    static void Restrict(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity id) return;
        foreach (var role in id.FindAll(id.RoleClaimType).Where(c => c.Value == Roles.Admin).ToList()) id.RemoveClaim(role);
        if (!id.HasClaim(id.RoleClaimType, Roles.Player)) id.AddClaim(new Claim(id.RoleClaimType, Roles.Player));
        if (!id.HasClaim(Mark, ByTracker)) id.AddClaim(new Claim(Mark, ByTracker));
    }
}
