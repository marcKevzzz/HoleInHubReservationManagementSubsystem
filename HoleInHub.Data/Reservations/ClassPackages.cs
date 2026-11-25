using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace HoleInHub.Data.Reservations;

public sealed class PackageSeat
{
    public string SessionId { get; set; } = "";
    public string LearnerId { get; set; } = "";
    public string State { get; set; } = "Enrolled";
    public string Attendance { get; set; } = "Unmarked";
}

public sealed class ClassPackageRecord
{
    [BsonId] public string Id { get; init; } = Guid.NewGuid().ToString("N");
    [BsonElement("package_id")] public string PackageId => BusinessIdentifier.Format("PKG", Id);
    public string ServiceEntryId { get; init; } = "";
    public string[] ClassSessionIds { get; init; } = [];
    public decimal PricePerLearner { get; init; }
    [BsonIgnoreIfNull] public DateTime? RegistrationEndsUtc { get; init; }
    public bool Open { get; set; } = true;
    public long Revision { get; set; }
}

public sealed partial class ReservationPersistence
{
    private IMongoCollection<ClassPackageRecord> Packages => context.Database.GetCollection<ClassPackageRecord>("class_packages");

    public async Task<ClassPackageRecord> PublishPackageAsync(string offering, string[] ids, HoleInHub.Data.Services.ServicePricing defaultPricing, DateTime now, CancellationToken ct, DateTime? registrationEndsUtc = null)
    {
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            var catalog = context.Database.GetCollection<HoleInHub.Data.Services.ServiceCatalogRecord>("service_catalog");
            var service = await catalog.Find(s, x => x.Id == offering && x.Active && !x.Deleted && x.BookingService == "KpgaGroup").FirstOrDefaultAsync(token);
            var pricing = service?.Pricing ?? defaultPricing;
            if (service == null || pricing.BaseRate <= 0 || ids.Length < 2 ||
                pricing.ClassCount != ids.Length || ids.Distinct().Count() != ids.Length)
            { throw new AllocationConflictException("Select the included number of distinct lessons for this package."); }
            var available = await Classes.Find(s, Builders<ClassSessionRecord>.Filter.In(x => x.ClassSessionId, ids))
                .SortBy(x => x.StartUtc).ToListAsync(token);
            if (available.Count != ids.Length || available.Any(x => x.ServiceEntryId != offering || x.State != "Open" || x.StartUtc <= now)
                || available.Zip(available.Skip(1)).Any(pair => pair.First.EndUtc > pair.Second.StartUtc))
            { throw new AllocationConflictException("Choose future, open lessons of the same program with separate times."); }
            var cutoff = registrationEndsUtc ?? available[0].StartUtc.AddHours(8).Date.AddHours(-8);
            if (cutoff <= now || cutoff > available[0].StartUtc)
            { throw new AllocationConflictException("Choose a registration deadline before the first class and on or after today."); }
            var package = new ClassPackageRecord { RegistrationEndsUtc = cutoff, ServiceEntryId = offering, ClassSessionIds = available.Select(x => x.ClassSessionId).ToArray(), PricePerLearner = pricing.BaseRate };
            await Packages.InsertOneAsync(s, package, cancellationToken: token);
            return package;
        }, cancellationToken: ct);
    }

    public async Task<bool> UpdatePackageSeatAsync(string bookingId, string sessionId, string learnerId, string action, DateTime now, CancellationToken ct)
    {
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (s, token) =>
        {
            var booking = await Reservations.Find(s, x => x.Id == bookingId).FirstOrDefaultAsync(token);
            var date = booking?.PackageDates?.FirstOrDefault(x => x.ClassSessionId == sessionId);
            var seat = booking?.PackageSeats?.FirstOrDefault(x => x.SessionId == sessionId && x.LearnerId == learnerId);
            if (date == null || seat == null || booking!.Status is not ("Pending" or "Confirmed" or "Checked-In" or "Completed")) { return false; }
            switch (action)
            {
                case "Request" when seat.State == "Enrolled" && now.AddHours(1) <= date.StartUtc:
                    seat.State = "Requested"; break;
                case "Approve" when seat.State == "Requested" && now < date.StartUtc:
                    await ReturnSeatsAsync(s, sessionId, 1, token);
                    seat.State = "Cancelled"; break;
                case "Decline" when seat.State == "Requested":
                    seat.State = "Enrolled"; break;
                case "Present" or "Absent" when seat.State != "Cancelled" && now >= date.StartUtc && booking.Status != "Pending":
                    seat.Attendance = action; break;
                default: return false;
            }
            await Reservations.UpdateOneAsync(s, x => x.Id == bookingId,
                Builders<ReservationRecord>.Update.Set(x => x.PackageSeats, booking.PackageSeats).Inc(x => x.Revision, 1), cancellationToken: token);
            return true;
        }, cancellationToken: ct);
    }

    public Task<List<ClassPackageRecord>> PackagesAsync(string offering, CancellationToken ct) =>
        Packages.Find(x => x.ServiceEntryId == offering && x.Open).ToListAsync(ct);

    public async Task<bool> ClosePackageAsync(string offering, string id, CancellationToken ct) =>
        (await Packages.UpdateOneAsync(x => x.PackageId == id && x.ServiceEntryId == offering, Builders<ClassPackageRecord>.Update.Set(x => x.Open, false), cancellationToken: ct)).MatchedCount == 1;
}
