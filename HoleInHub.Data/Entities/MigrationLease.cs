using MongoDB.Bson.Serialization.Attributes;

namespace HoleInHub.Data.Entities;

public sealed class MigrationLease
{
    public const string DefaultLeaseId = "migration_exclusive_lease";

    [BsonId]
    public string LeaseId { get; set; } = DefaultLeaseId;

    [BsonElement("owner")]
    public string Owner { get; set; } = string.Empty;

    [BsonElement("acquiredAtUtc")]
    public DateTime AcquiredAtUtc { get; set; }

    [BsonElement("expiresAtUtc")]
    public DateTime ExpiresAtUtc { get; set; }

    [BsonElement("lastHeartbeatAtUtc")]
    public DateTime LastHeartbeatAtUtc { get; set; }
}
