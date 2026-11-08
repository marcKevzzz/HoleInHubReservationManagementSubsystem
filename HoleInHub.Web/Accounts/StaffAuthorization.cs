namespace HoleInHub.Web.Accounts;

public static class StaffAuthorization
{
    public const string Workspace = "StaffWorkspace";

    public static bool ManagesVenue(this System.Security.Claims.ClaimsPrincipal user) =>
        user.IsInRole(HoleInHub.Business.Accounts.StaffRoles.Manager) || user.IsInRole(HoleInHub.Business.Accounts.StaffRoles.Admin);
}
