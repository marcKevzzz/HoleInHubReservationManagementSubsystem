using HoleInHub.Business.Accounts;
using HoleInHub.Data.Reservations;

namespace HoleInHub.Business.Reservations;

public sealed class FamilyLearnerInput
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public int? Age { get; set; }
    public int? GuardianIndex { get; set; }
    public bool IsBookingContact { get; set; }
}

public sealed class FamilyGuardianInput
{
    public string? Name { get; set; }
    public string? Relationship { get; set; }
    public bool IsBookingContact { get; set; }
}

public sealed record FamilyLearnerDto(string Id, string Name, string AgeCategory, string? GuardianId, string State, string Attendance);
public sealed record FamilyGuardianDto(string Id, string Name, string Relationship);
public sealed record FamilyClassOption(string ClassSessionId, string ServiceEntryId, DateTime StartUtc, DateTime EndUtc,
    int RemainingSeats, decimal Price)
{
    public DateTime? RegistrationEndsUtc { get; init; }
    public DateTime[] Dates { get; init; } = [];
    public bool JuniorOnly { get; init; }
    public string PriceBasis => "Per learner";
}

public sealed partial class ReservationService
{
    public Task<bool> SetClassReminderAsync(string id, string? publicBase, CancellationToken ct) =>
        persistence.SetClassReminderAsync(id, publicBase, ct);

    public bool SharedClassesEnabled => policy.Value.SharedClassesEnabled;

    public async Task<IReadOnlyList<FamilyClassOption>> FamilyClassesAsync(string? serviceEntryId, int seats, CancellationToken ct, bool showClosedPrograms = false)
    {
        if (!SharedClassesEnabled || seats is < 1 or > 8) { return []; }
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var earliest = now.UtcDateTime.AddHours(1);
        var classes = await persistence.ClassesAsync(earliest, DateTime.MaxValue, ct);
        var singles = classes.Where(x => x.ServiceEntryId == (serviceEntryId ?? "KpgaGroup") && x.State == "Open"
            && x.StartUtc >= earliest)
            .OrderBy(x => x.StartUtc).ThenBy(x => x.ClassSessionId, StringComparer.Ordinal)
            .Select(x => new FamilyClassOption(x.ClassSessionId, x.ServiceEntryId, x.StartUtc, x.EndUtc,
                x.Capacity - x.HeldSeats, x.PricePerLearner)).ToArray();
        var juniorOnly = serviceCatalog != null && (await serviceCatalog.GetBookingServiceAsync(serviceEntryId ?? "KpgaGroup", ct))?.JuniorOnly == true;
        var options = new List<FamilyClassOption>();
        foreach (var package in await persistence.PackagesAsync(serviceEntryId ?? "KpgaGroup", ct))
        {
            var included = package.ClassSessionIds.Select(id => singles.FirstOrDefault(x => x.ClassSessionId == id)).ToArray();
            if (included.Any(x => x == null)) { continue; }
            var first = included[0]!;
            var cutoff = package.RegistrationEndsUtc ?? first.StartUtc.AddHours(8).Date.AddHours(-8);
            if (!showClosedPrograms && (now.UtcDateTime >= cutoff || included.Min(x => x!.RemainingSeats) < seats)) { continue; }
            options.Add(first with
            {
                JuniorOnly = juniorOnly,
                RegistrationEndsUtc = cutoff,
                ClassSessionId = package.PackageId,
                Price = package.PricePerLearner,
                RemainingSeats = included.Min(x => x!.RemainingSeats),
                Dates = included.Select(x => x!.StartUtc).ToArray()
            });
        }
        return options;
    }

    private async Task<ClassSessionRecord> AvailableFamilyClassAsync(SubmitReservationRequest request, CancellationToken ct)
    {
        if (!SharedClassesEnabled) { throw new ReservationValidationException("Shared class enrollment is closed."); }
        if (request.ClassSessionId!.StartsWith("PKG-", StringComparison.Ordinal))
        {
            var option = (await FamilyClassesAsync(request.ServiceEntryId, request.Attendees, ct))
                .FirstOrDefault(x => x.ClassSessionId == request.ClassSessionId);
            if (option == null)
            {
                var package = (await persistence.PackagesAsync(request.ServiceEntryId ?? "KpgaGroup", ct))
                    .FirstOrDefault(x => x.PackageId == request.ClassSessionId);
                if (package != null)
                {
                    foreach (var id in package.ClassSessionIds)
                    {
                        var date = await persistence.ClassAsync(id, ct);
                        if (date != null && (date.State != "Open" || date.Capacity - date.HeldSeats < request.Attendees))
                        { throw new ReservationValidationException($"The lesson on {DashboardMetrics.Local(date.StartUtc):ddd, d MMM} cannot accommodate your family. Choose another package.", nameof(request.ClassSessionId)); }
                    }
                }
                throw new ReservationValidationException("Package enrollment has closed. Choose another schedule.", nameof(request.ClassSessionId));
            }
            return new ClassSessionRecord { StartUtc = option.StartUtc, EndUtc = option.EndUtc };
        }
        throw new ReservationValidationException("Choose a published lesson package to register.", nameof(request.ClassSessionId));
    }

    public static (ClassLearnerRecord[] Learners, ClassGuardianRecord[] Guardians) BuildFamilyRoster(SubmitReservationRequest request, bool juniorOnly = false)
    {
        if (request.Learners is not { Length: >= 1 and <= 8 } learners || request.Guardians?.Length > 8)
        { throw new ReservationValidationException("Enter one to eight learners for this class."); }
        var contact = $"{request.FirstName?.Trim()} {request.LastName?.Trim()}".Trim();
        var guardians = new Dictionary<int, ClassGuardianRecord>();
        if (juniorOnly)
        {
            if (!ContactRules.IsName(contact))
            { throw new ReservationValidationException("Enter the parent or guardian’s contact name."); }
            guardians[0] = new() { Id = Guid.NewGuid().ToString("N"), Name = contact, Relationship = "Parent or guardian" };
        }
        var roster = new List<ClassLearnerRecord>();
        foreach (var learner in learners)
        {
            if (juniorOnly && (learner?.IsBookingContact == true || learner?.Age is null or < 0 or > 17))
            { throw new ReservationValidationException("Enter each child separately. Junior learners must be 17 or younger."); }
            var first = (learner?.IsBookingContact == true ? request.FirstName : learner?.FirstName)?.Trim();
            var last = (learner?.IsBookingContact == true ? request.LastName : learner?.LastName)?.Trim();
            if (!ContactRules.IsName(first) || !ContactRules.IsName(last))
            { throw new ReservationValidationException("Enter each learner’s first and last name."); }
            if (learner?.Age is not int age || age is < 0 or > 120)
            { throw new ReservationValidationException("Enter each learner’s age in years, from 0 to 120."); }
            string? guardianId = null;
            if (juniorOnly) { guardianId = guardians[0].Id; }
            else if (age < 18)
            {
                if (learner.GuardianIndex is not int index || index < 0 || request.Guardians is null || index >= request.Guardians.Length)
                { throw new ReservationValidationException("Choose a parent or guardian for each learner under 18."); }
                if (!guardians.TryGetValue(index, out var guardian))
                {
                    var input = request.Guardians[index];
                    var guardianName = input?.IsBookingContact == true ? contact : input?.Name?.Trim();
                    if (!ContactRules.IsName(guardianName) || !ContactRules.IsName(input?.Relationship))
                    { throw new ReservationValidationException("Enter the parent or guardian’s name and relationship to the learner."); }
                    guardian = new() { Id = Guid.NewGuid().ToString("N"), Name = guardianName!, Relationship = input!.Relationship!.Trim() };
                    guardians.Add(index, guardian);
                }
                guardianId = guardian.Id;
            }
            roster.Add(new() { Id = Guid.NewGuid().ToString("N"), Name = $"{first} {last}", FirstName = first, LastName = last, Age = age, AgeCategory = age < 18 ? "Minor" : "Adult", GuardianId = guardianId });
        }
        return (roster.ToArray(), guardians.Values.ToArray());
    }

    public Task<ReservationDto> SubmitStaffFamilyAsync(SubmitReservationRequest request, string actor, string source, string? customerProfileId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor) || source is not ("Walk-in" or "Phone" or "Messaging")
            || string.IsNullOrWhiteSpace(request.ClassSessionId) || customerProfileId is { Length: > 100 })
        { throw new ReservationValidationException("Choose the published class and booking source."); }
        return SubmitCoreAsync(request, "staff:" + actor, request.Email?.Trim().ToLowerInvariant() ?? "", ct, null, null, false, null,
            source, customerProfileId, actor);
    }

    public Task<bool> CancelFamilyLearnerAsync(string id, string learnerId, string actor, CancellationToken ct) =>
        persistence.CancelLearnerAsync(id, learnerId, actor, (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime, ct);
}
