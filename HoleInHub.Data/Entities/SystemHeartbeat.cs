using MongoDB.Bson.Serialization.Attributes;

namespace HoleInHub.Data.Entities;

public sealed class SystemHeartbeat
{
    public const string DefaultId = "system_heartbeat";

    [BsonId]
    public string Id { get; set; } = DefaultId;

    [BsonElement("lastPingAtUtc")]
    public DateTime LastPingAtUtc { get; set; }

    [BsonElement("environment")]
    public string Environment { get; set; } = string.Empty;
}
