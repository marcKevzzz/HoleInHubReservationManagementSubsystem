using HoleInHub.Business.Accounts;

namespace HoleInHub.Web.Accounts;

public sealed class AccountUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public string Version { get; set; } = Guid.NewGuid().ToString("N");
    public bool EmailConfirmed { get; set; }
    public int AccessFailedCount { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public string[] Roles { get; set; } = ["Customer"];
    public AccountDetails ToDetails() => new(Id, Email, NormalizedEmail, PasswordHash, SecurityStamp, Version, EmailConfirmed, AccessFailedCount, LockoutEnd?.UtcDateTime, Roles);
    public static AccountUser? From(AccountDetails? a) => a is null ? null : new() { Id = a.Id, Email = a.Email, NormalizedEmail = a.NormalizedEmail, PasswordHash = a.PasswordHash, SecurityStamp = a.SecurityStamp, Version = a.Version, EmailConfirmed = a.EmailConfirmed, AccessFailedCount = a.AccessFailedCount, LockoutEnd = a.LockoutEndUtc is { } d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)) : null, Roles = a.Roles };
}
