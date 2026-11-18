
using MongoDB.Bson.Serialization.Attributes;

namespace HoleInHub.Data.Accounts;

public sealed record StaffRoleAuditRecord(
    [property: BsonId] string Id, string EventType, DateTime OccurredUtc, string ActorAccountId, string ActorRole,
    string TargetAccountId, string[] PreviousRoles, string[] NewRoles, string Reason, string CorrelationId)
{
    internal const string CollectionName = "staff_role_audit";
}
