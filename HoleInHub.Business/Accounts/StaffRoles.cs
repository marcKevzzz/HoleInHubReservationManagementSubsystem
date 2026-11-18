namespace HoleInHub.Business.Accounts;

public static class StaffRoles
{
    public const string Staff = "Staff";
    public const string Manager = "Manager";
    public const string Admin = "Admin";
    public const string VenueManagement = Manager + "," + Admin;

    public static bool CanCreate(string actorRole, string requestedRole) => actorRole == Admin
        ? Workspace.Contains(requestedRole)
        : actorRole == Manager && requestedRole is Staff or Manager;

    public static IReadOnlyList<string> Workspace { get; } = Array.AsReadOnly(new[] { Staff, Manager, Admin });
}
