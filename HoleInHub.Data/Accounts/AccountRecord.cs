using MongoDB.Bson.Serialization.Attributes;

namespace HoleInHub.Data.Accounts;

public sealed class AccountRecord
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public string Version { get; set; } = Guid.NewGuid().ToString("N");
    public bool EmailConfirmed { get; set; }
    public int AccessFailedCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public string[] Roles { get; set; } = ["Customer"];
}
