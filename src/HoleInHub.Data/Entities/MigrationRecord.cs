using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace HoleInHub.Data.Entities;

public sealed class MigrationRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("migrationId")]
    public string MigrationId { get; set; } = string.Empty;

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("checksum")]
    public string Checksum { get; set; } = string.Empty;

    [BsonElement("appliedAtUtc")]
    public DateTime AppliedAtUtc { get; set; }

    [BsonElement("executionDurationMs")]
    public long ExecutionDurationMs { get; set; }
}
