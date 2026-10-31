using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace HoleInHub.Web.Accounts;

public sealed class AccountClaimsFactory(UserManager<AccountUser> users, IOptions<IdentityOptions> options) : UserClaimsPrincipalFactory<AccountUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AccountUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim("email_verified", user.EmailConfirmed ? "true" : "false"));
        foreach (var role in user.Roles) { identity.AddClaim(new Claim(ClaimTypes.Role, role)); }
        return identity;
    }
}
