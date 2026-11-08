namespace HoleInHub.Business.Accounts;

public static class StaffRoles
{
    public const string Staff = "Staff";
    public const string Manager = "Manager";
    public const string Admin = "Admin";
    public const string VenueManagement = Manager + "," + Admin;

    public static IReadOnlyList<string> Workspace { get; } = Array.AsReadOnly(new[] { Staff, Manager, Admin });
}
