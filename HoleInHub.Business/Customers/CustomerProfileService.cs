using System.Text.RegularExpressions;
using HoleInHub.Business.Reservations;
using HoleInHub.Data.Customers;

namespace HoleInHub.Business.Customers;

public sealed class CustomerProfileService(CustomerProfilePersistence persistence, TimeProvider clock)
{
    public const int MaximumMergeBookings = CustomerProfilePersistence.MaximumMergeBookings;
    public Task<bool> InstalledAsync(CancellationToken ct) => persistence.InstalledAsync(ct);
    private static CustomerProfile Map(CustomerProfileSnapshot snapshot)
    {
        var p = snapshot.Profile;
        return new(p.Id, p.RepresentativeBookingId, p.FirstName, p.LastName, p.Email, p.Phone, p.Kind,
            p.Variants.Select(v => new CustomerContact(v.FirstName, v.LastName, v.Email, v.Phone, v.BookingId, v.RecordedUtc)).ToArray(),
            p.Sources, p.ContactRevision, p.MembershipRevision, snapshot.ReservationIds.Length);
    }
    public async Task<CustomerProfile?> GetAsync(string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || !await persistence.InstalledAsync(ct)) { return null; }
        var snapshot = await persistence.ReadAsync(key, ct);
        return snapshot is null ? null : Map(snapshot);
    }
    public async Task<CustomerMergePreview?> PreviewAsync(string sourceKey, string targetKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceKey) || string.IsNullOrWhiteSpace(targetKey) || sourceKey.Length > 100 || targetKey.Length > 100) { return null; }
        var source = await persistence.ReadAsync(sourceKey, ct);
        var target = await persistence.ReadAsync(targetKey, ct);
        if (source is null || target is null) { return null; }
        if (source.Profile.Id == target.Profile.Id) { throw new ReservationValidationException("These records already belong to the same customer profile."); }
        var bookings = await persistence.BookingsAsync(source.ReservationIds, ct);
        var sourceDates = await persistence.DateRangeAsync(source.ReservationIds, ct);
        var targetDates = await persistence.DateRangeAsync(target.ReservationIds, ct);
        return new(Map(source), Map(target), bookings.Select(b => new CustomerMergeBooking(b.Id, b.Reference, b.FirstName, b.LastName, b.Phone, b.StartUtc, b.EndUtc, b.Status)).ToArray(),
            sourceDates.First, sourceDates.Last, targetDates.First, targetDates.Last);
    }
    public static bool HaveMatchingDetails(CustomerProfile a, CustomerProfile b)
    {
        static IEnumerable<CustomerContact> Contacts(CustomerProfile profile) =>
            profile.Variants.Append(new(profile.FirstName, profile.LastName, profile.Email, profile.Phone, "", default));

        static IEnumerable<string> Keys(CustomerContact contact)
        {
            var email = contact.Email.Trim();
            if (email.Length > 0) { yield return "email:" + email.ToUpperInvariant(); }

            var phone = new string(contact.Phone.Where(char.IsAsciiDigit).ToArray());
            if (phone.Length == 11 && phone.StartsWith("09", StringComparison.Ordinal)) { phone = "63" + phone[1..]; }
            if (phone.Length > 0) { yield return "phone:" + phone; }

            // Match two distinct words within a single recorded name, regardless of order.
            var words = Regex.Matches((contact.FirstName + " " + contact.LastName).ToUpperInvariant(), @"[\p{L}\p{M}]+")
                .Select(match => match.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            for (var first = 0; first < words.Length; first++)
            {
                for (var second = first + 1; second < words.Length; second++)
                {
                    yield return $"name:{words[first]} {words[second]}";
                }
            }
        }

        return Contacts(a).SelectMany(Keys).Intersect(Contacts(b).SelectMany(Keys), StringComparer.Ordinal).Any();
    }
    public async Task<CustomerMergeReceipt> MergeAsync(CustomerMergeCommand command, string actorId, string actorLabel, CancellationToken ct)
    {
        if (!await persistence.InstalledAsync(ct)) { throw new ReservationValidationException("Apply the customer-profile database update before combining records."); }
        ValidateReason(command.Reason, command.OperationId, actorId);
        if (!ValidId(command.SourceId) || !ValidId(command.TargetId) || command.SourceId == command.TargetId
            || command.SourceContactRevision < 1 || command.SourceMembershipRevision < 1 || command.TargetContactRevision < 1 || command.TargetMembershipRevision < 1
            || !Choice(command.NameFrom) || !Choice(command.PhoneFrom) || !Choice(command.EmailFrom)
            || command.ReservationIds is { Length: 0 or > MaximumMergeBookings }
            || command.ReservationIds is not null && (command.ReservationIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 100) || command.ReservationIds.Distinct().Count() != command.ReservationIds.Length))
        { throw new ReservationValidationException("Review both profiles, choose their contact details and select valid bookings before merging."); }
        var source = await GetAsync(command.SourceId, ct);
        var target = await GetAsync(command.TargetId, ct);
        // A committed retry resolves both keys to one profile; let persistence return its receipt.
        if (source is not null && target is not null && source.Id != target.Id && !HaveMatchingDetails(source, target))
        { throw new ReservationValidationException("These records need two matching name words, a matching email, or a matching phone number."); }
        try
        {
            var operation = await persistence.MergeAsync(new()
            {
                Id = command.OperationId,
                SourceId = command.SourceId,
                TargetId = command.TargetId,
                SourceContactRevision = command.SourceContactRevision,
                SourceMembershipRevision = command.SourceMembershipRevision,
                TargetContactRevision = command.TargetContactRevision,
                TargetMembershipRevision = command.TargetMembershipRevision,
                NameFrom = command.NameFrom,
                PhoneFrom = command.PhoneFrom,
                EmailFrom = command.EmailFrom,
                ReservationIds = command.ReservationIds,
                Reason = command.Reason.Trim(),
                ActorId = actorId,
                ActorLabel = actorLabel,
                OccurredUtc = clock.GetUtcNow().UtcDateTime
            }, ct);
            return new(operation.Id, operation.TargetId, operation.ReservationIds.Length);
        }
        catch (CustomerProfileConflictException error) { throw new ReservationValidationException(error.Message); }
    }
    public async Task<CustomerMergeHistory?> HistoryAsync(string key, int page, CancellationToken ct)
    {
        var profile = await GetAsync(key, ct);
        if (profile is null) { return null; }
        page = Math.Clamp(page, 1, 1000);
        var operations = await persistence.HistoryAsync(profile.Id, page, ct);
        var entries = new List<CustomerMergeHistoryEntry>();
        foreach (var op in operations.Take(25))
        {
            entries.Add(new(op.Id, op.Kind, op.SourceId, op.TargetId, Name(op.SourceBefore), Name(op.TargetBefore), op.ActorLabel.Length > 0 ? op.ActorLabel : op.ActorId,
                op.Reason, op.OccurredUtc, op.ReservationIds.Length, await persistence.CanUndoAsync(op, ct), op.UndoneById));
        }
        return new(profile, entries.ToArray(), page, operations.Count > 25);
    }
    public async Task<CustomerMergeReceipt> UndoAsync(string operationId, string commandId, string reason, string actorId, string actorLabel, CancellationToken ct)
    {
        if (!await persistence.InstalledAsync(ct)) { throw new ReservationValidationException("Apply the customer-profile database update before changing records."); }
        ValidateReason(reason, commandId, actorId);
        if (!Guid.TryParseExact(operationId, "N", out _)) { throw new ReservationValidationException("Choose a valid merge operation."); }
        try
        {
            var operation = await persistence.UndoAsync(operationId, commandId, reason.Trim(), actorId, actorLabel, clock.GetUtcNow().UtcDateTime, ct);
            return new(operation.Id, operation.TargetId, operation.ReservationIds.Length);
        }
        catch (CustomerProfileConflictException error) { throw new ReservationValidationException(error.Message); }
    }
    private static string Name(CustomerProfileRecord p) => (p.FirstName + " " + p.LastName).Trim();
    private static bool Choice(string value) => value is "source" or "target";
    private static bool ValidId(string value) => value is { Length: 67 } && value.StartsWith("cp_", StringComparison.Ordinal) && value[3..].All(char.IsAsciiHexDigit);
    private static void ValidateReason(string reason, string operationId, string actorId)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500 || !Guid.TryParseExact(operationId, "N", out _) || string.IsNullOrWhiteSpace(actorId) || actorId.Length > 128)
        { throw new ReservationValidationException("Enter a reason of 1–500 characters and reload the form if its operation key is missing."); }
    }
}
