namespace HoleInHub.Business.Customers;

public sealed record CustomerContact(string FirstName, string LastName, string Email, string Phone, string BookingId, DateTime RecordedUtc);
public sealed record CustomerProfile(string Id, string BookingId, string FirstName, string LastName, string Email, string Phone, string Kind,
    CustomerContact[] Variants, string[] Sources, long ContactRevision, long MembershipRevision, int BookingCount);
public sealed record CustomerMergeBooking(string Id, string Reference, string FirstName, string LastName, string Phone, DateTime StartUtc, DateTime EndUtc, string Status);
public sealed record CustomerMergePreview(CustomerProfile Source, CustomerProfile Target, CustomerMergeBooking[] SourceBookings,
    DateTime? SourceFirst, DateTime? SourceLast, DateTime? TargetFirst, DateTime? TargetLast);
public sealed record CustomerMergeCommand(string OperationId, string SourceId, string TargetId, long SourceContactRevision,
    long SourceMembershipRevision, long TargetContactRevision, long TargetMembershipRevision, string NameFrom, string PhoneFrom,
    string EmailFrom, string[]? ReservationIds, string Reason);
public sealed record CustomerMergeReceipt(string OperationId, string ProfileId, int BookingCount);
public sealed record CustomerMergeHistoryEntry(string Id, string Kind, string SourceId, string TargetId, string SourceName, string TargetName,
    string Actor, string Reason, DateTime AtUtc, int BookingCount, bool CanUndo, string? UndoneById);
public sealed record CustomerMergeHistory(CustomerProfile Profile, CustomerMergeHistoryEntry[] Entries, int Page, bool HasMore);
