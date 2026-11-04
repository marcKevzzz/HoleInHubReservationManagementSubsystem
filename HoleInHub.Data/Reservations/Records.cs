using MongoDB.Bson.Serialization.Attributes;

namespace HoleInHub.Data.Reservations;

public sealed class ResourceRecord
{
    public bool Active { get; set; } = true;
    [BsonId] public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Service { get; set; } = "Regular";
}

public sealed class ReservationRecord
{
    public int PolicyVersion { get; set; }
    [BsonElement("GuestSessionHash")] public string? EmailVerificationId { get; set; }
    [BsonIgnoreIfNull] public string? CustomerAccountId { get; set; }
    public string? AccessTokenHash { get; set; }
    public Dictionary<string, string> ResourceNames { get; set; } = [];
    public ParticipantRecord[] Participants { get; set; } = [];
    public string PaymentStatus { get; set; } = "NotRequired";
    public string? PaymentReference { get; set; }
    public decimal? RentalTotal { get; set; }
    public decimal? DepositAmount { get; set; }
    public bool CoachConfirmed { get; set; }
    public bool CancellationRequested { get; set; }

    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    // AccountId is the legacy BSON ownership scope; guest values are not login-account IDs.
    public string AccountId { get; set; } = "";
    public string Email { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Service { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ReviewDeadlineUtc { get; set; }
    public string[] ResourceIds { get; set; } = [];
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Phone { get; set; } = "";
    public int Attendees { get; set; }
    public string Notes { get; set; } = "";
    public bool PrivacyAccepted { get; set; }
    public string IdempotencyKey { get; set; } = "";
    [BsonElement("PayloadHash")] public string SubmissionHash { get; set; } = "";
    public List<HistoryRecord> History { get; set; } = [];
}

public sealed class HistoryRecord
{
    [BsonIgnoreIfNull] public string? EventType { get; set; }
    [BsonIgnoreIfNull] public string? ReservationStatus { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime AtUtc { get; set; }
    public string Actor { get; set; } = "";
}

public sealed class ScheduleBlock
{
    [BsonId] public string Id { get; set; } = "";
    public string[] ResourceIds { get; set; } = [];
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
}

public sealed class OutboxRecord
{
    [BsonId] public string Id { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string? ReservationId { get; set; }
    public string? RequiredStatus { get; set; }
    public DateTime DueUtc { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public string? LeaseToken { get; set; }
    public DateTime? SentUtc { get; set; }
    public int Attempts { get; set; }
    public bool Failed { get; set; }
}

public sealed class ParticipantRecord
{
    public string ResourceId { get; set; } = "";
    public int Players { get; set; }
    public int NonPlayers { get; set; }
}
public sealed class GuestVerificationRecord
{
    [BsonElement("ProtectedDraft")] public string? ProtectedContext { get; set; }
    [BsonIgnoreIfNull] public string? Purpose { get; set; }
    [BsonId] public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public DateTime SentUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public int Attempts { get; set; }
    public DateTime? VerifiedUtc { get; set; }
    [BsonElement("ConsumedKey")] public string? ConsumedBySubmissionKey { get; set; }
}

public sealed record EmailDeliveryQuota(string Id, int Limit, TimeSpan Window, TimeSpan Cooldown);
public sealed class RecoveryDeliveryRecord
{
    [BsonId] public string Id { get; set; } = "";
    public long Version { get; set; }
    public List<DateTime> IssuedUtc { get; set; } = [];
    public DateTime ExpiresUtc { get; set; }
}
