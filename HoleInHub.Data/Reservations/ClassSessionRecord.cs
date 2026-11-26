using MongoDB.Bson.Serialization.Attributes;

namespace HoleInHub.Data.Reservations;

public sealed class ClassSessionRecord
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [BsonElement("class_session_id")] public string ClassSessionId => BusinessIdentifier.Format("CLS", Id);
    public string ServiceEntryId { get; set; } = "KpgaGroup";
    [BsonIgnoreIfNull] public string? CoachId { get; set; }
    [BsonIgnoreIfNull] public string? CoachName { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string[] ResourceIds { get; set; } = [];
    [BsonIgnoreIfDefault] public long ScheduleRevision { get; set; }
    public int Capacity { get; set; }
    public int HeldSeats { get; set; }
    public string State { get; set; } = "Open";
    public decimal PricePerLearner { get; set; }
    public string CreatedBy { get; set; } = "";
}

public sealed class ClassLearnerRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    [BsonIgnoreIfNull] public string? FirstName { get; set; }
    [BsonIgnoreIfNull] public string? LastName { get; set; }
    [BsonIgnoreIfNull] public int? Age { get; set; }
    public string AgeCategory { get; set; } = "Adult";
    [BsonIgnoreIfNull] public string? GuardianId { get; set; }
    public string State { get; set; } = "Enrolled";
    public string Attendance { get; set; } = "Unmarked";
}

public sealed class ClassGuardianRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Relationship { get; set; } = "";
}
