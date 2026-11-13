using System.Security.Cryptography;
using System.Text;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Data.Customers;

public sealed record CustomerContactVariant(string FirstName, string LastName, string Email, string Phone, string BookingId, DateTime RecordedUtc);
public sealed class CustomerProfileRecord
{
    public string Id { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Kind { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public string RepresentativeBookingId { get; set; } = "";
    public CustomerContactVariant[] Variants { get; set; } = [];
    public string[] Sources { get; set; } = [];
    public string[] SearchNames { get; set; } = [];
    public string? LastChangeOperationId { get; set; }
    public string? MergedIntoId { get; set; }
    public bool PreferredContactPinned { get; set; }
    public long ContactRevision { get; set; } = 1;
    public long MembershipRevision { get; set; } = 1;
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public static (string Id, string Kind) Identity(ReservationRecord record, bool legacyGrouping = true)
    {
        static bool Account(string? id) => !string.IsNullOrWhiteSpace(id) && !id.StartsWith("guest:", StringComparison.Ordinal) && !id.StartsWith("staff:", StringComparison.Ordinal);
        var account = Account(record.CustomerAccountId) ? record.CustomerAccountId : Account(record.AccountId) ? record.AccountId : null;
        var guest = record.AccountId.StartsWith("guest:", StringComparison.Ordinal) && !string.IsNullOrEmpty(record.EmailVerificationId) && !string.IsNullOrEmpty(record.Email);
        var kind = account is not null ? "Account" : guest ? "Guest" : "BookingContact";
        var key = account ?? (guest && legacyGrouping ? record.Email.ToLowerInvariant() : record.Id);
        return ("cp_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(kind + ":" + key))), kind);
    }
}
public sealed class CustomerMembershipRecord
{
    public string Id { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public long Revision { get; set; } = 1;
    public string? LastOperationId { get; set; }
}
public sealed class CustomerProfileOperationRecord
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "Merge";
    public string PayloadHash { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string ActorLabel { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime OccurredUtc { get; set; }
    public string[] ReservationIds { get; set; } = [];
    public CustomerProfileRecord SourceBefore { get; set; } = new();
    public CustomerProfileRecord TargetBefore { get; set; } = new();
    public long SourceContactAfter { get; set; }
    public long SourceMembershipAfter { get; set; }
    public long TargetContactAfter { get; set; }
    public long TargetMembershipAfter { get; set; }
    public string SourceAfterHash { get; set; } = "";
    public string TargetAfterHash { get; set; } = "";
    public CustomerMembershipRecord[] MembershipsBefore { get; set; } = [];
    public string? UndoneById { get; set; }
    public string? ReversesId { get; set; }
}
public sealed class CustomerMergeWrite
{
    public string Id { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string TargetId { get; set; } = "";
    public long SourceContactRevision { get; set; }
    public long SourceMembershipRevision { get; set; }
    public long TargetContactRevision { get; set; }
    public long TargetMembershipRevision { get; set; }
    public string[]? ReservationIds { get; set; }
    public string NameFrom { get; set; } = "target";
    public string EmailFrom { get; set; } = "target";
    public string PhoneFrom { get; set; } = "target";
    public string Reason { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string ActorLabel { get; set; } = "";
    public DateTime OccurredUtc { get; set; }
}
public sealed record CustomerProfileSnapshot(CustomerProfileRecord Profile, string[] ReservationIds);
public sealed class CustomerProfileConflictException(string message) : Exception(message);
