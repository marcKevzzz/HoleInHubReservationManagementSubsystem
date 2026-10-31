using MongoDB.Bson.Serialization.Attributes;
namespace HoleInHub.Data.Reservations;

public sealed class ResourceRecord { [BsonId] public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string Service { get; set; } = "Regular"; }
public sealed class ReservationRecord
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N"); public string AccountId { get; set; } = ""; public string Email { get; set; } = ""; public string Reference { get; set; } = ""; public string Service { get; set; } = ""; public string Status { get; set; } = "Pending"; public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ReviewDeadlineUtc { get; set; }
    public string[] ResourceIds { get; set; } = []; public string FirstName { get; set; } = ""; public string LastName { get; set; } = ""; public string Phone { get; set; } = ""; public int Attendees { get; set; }
    public string IdempotencyKey { get; set; } = ""; public string PayloadHash { get; set; } = ""; public List<HistoryRecord> History { get; set; } = [];
}
public sealed class HistoryRecord { public string Status { get; set; } = ""; public DateTime AtUtc { get; set; } public string Actor { get; set; } = ""; }
public sealed class ScheduleBlock { [BsonId] public string Id { get; set; } = ""; public string[] ResourceIds { get; set; } = []; public DateTime StartUtc { get; set; } public DateTime EndUtc { get; set; } }
public sealed class OutboxRecord { [BsonId] public string Id { get; set; } = ""; public string Recipient { get; set; } = ""; public string Subject { get; set; } = ""; public string Body { get; set; } = ""; public string? ReservationId { get; set; } public string? RequiredStatus { get; set; } public DateTime DueUtc { get; set; } public DateTime? LeaseUntilUtc { get; set; } public string? LeaseToken { get; set; } public DateTime? SentUtc { get; set; } public int Attempts { get; set; } public bool Failed { get; set; } }
