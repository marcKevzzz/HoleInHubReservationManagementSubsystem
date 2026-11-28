using HoleInHub.Data.Integrations;
using System.Security.Cryptography;
using System.Text;

namespace HoleInHub.Business.Services;

public sealed record KeySummary(string Id, string Name, string[] Permissions, DateTime ExpiresUtc, bool Revoked, string Subsystem);
public sealed record IntegrationPermission(string Group, string Code, string Label, string Description, bool Live = false);
public sealed record RequestSummary(string Id, string? KeyId, string Route, int Status, DateTime AtUtc, long DurationMs);
public sealed class IntegrationAccess(IntegrationStore store, TimeProvider clock)
{
    public const string CreateCustomers = "customers.create";
    public const string ReadCustomerProfile = "customer-profile.read";
    public const string ReadServices = "services.read";
    public const string CreateServices = "services.create";
    public const string UpdateRates = "rates.update";
    public const string UpdateServices = "services.update";
    public const string PublishServices = "services.publish";
    public const string ReadReservations = "reservations.read";
    public const string ReadReservedBays = "reserved-bays.read";
    public const string ReadBays = "bays.read";
    public const string ReadCoachAvailability = "coach-availability.read";
    public static IReadOnlyList<IntegrationPermission> Catalog { get; } = [
        new("Service catalog", ReadServices, "Service details", "Read active offers and booking types.", true),
        new("Service catalog", ReadRates, "Service rates", "Read displayed rates for active services.", true),
        new("Service catalog", CreateServices, "Create services", "Add unpublished offerings to the catalog.", true),
        new("Service catalog", UpdateServices, "Edit service details", "Change titles, summaries and descriptions.", true),
        new("Service catalog", UpdateRates, "Edit rates", "Update the base PHP rate for an existing service.", true),
        new("Service catalog", PublishServices, "Manage publication", "Publish or withdraw an offering.", true),
        new("Reservations", ReadReservations, "Reservations", "Read booking schedules and status.", true),
        new("Reservations", ReadReservedBays, "Reserved bays", "Read bay assignments for a booking.", true),
        new("Venue resources", ReadBays, "Bay resources", "Read the venue bay directory.", true),
        new("Venue resources", ReadCoachAvailability, "Coach availability", "Find available coaches for an individual coaching session.", true),
        new("Settlement", "deposits.write", "Verify deposits", "Submit whole-shop deposit verification."),
        new("Settlement", "settlements.write", "Settle checkout", "Submit final settlement for a reservation."),
        new("Customers", CreateCustomers, "Add customers", "Add customer contact profiles.", true),
        new("Customers", ReadCustomerProfile, "Customer details", "Read the contact details a signed-in customer agrees to share.", true)
    ];
    public static bool ValidPermissions(string[] permissions) => permissions.Length > 0 && permissions.All(p => Catalog.Any(scope => scope.Code == p));
    public const string ReadRates = "rates.read";
    public async Task<string?> ProfileRecipientAsync(string keyId, CancellationToken ct)
    {
        if (!Guid.TryParseExact(keyId, "N", out _)) { return null; }
        var key = await store.FindAsync(keyId, ct);
        return key is not null && !key.Revoked && key.ExpiresUtc > clock.GetUtcNow().UtcDateTime
            && key.Permissions.Contains(ReadCustomerProfile) ? key.Name : null;
    }
    private static byte[] Digest(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    public async Task<string> IssueAsync(string name, int days, string[] permissions, CancellationToken ct)
        => await TryIssueAsync(name, days, permissions, Guid.NewGuid().ToString("N"), ct)
            ?? throw new InvalidOperationException("Could not allocate a key ID.");

    public Task<string?> IssueForSubmissionAsync(string actor, Guid submission, string name, int days, string[] permissions, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor) || submission == Guid.Empty) { throw new ArgumentException("Missing issuance identity."); }
        var id = Convert.ToHexStringLower(Digest(actor + ":" + submission.ToString("N")).AsSpan(0, 16));
        return TryIssueAsync(name, days, permissions, id, ct);
    }

    private async Task<string?> TryIssueAsync(string name, int days, string[] permissions, string id, CancellationToken ct)
    {
        if (!ValidPermissions(permissions)) { throw new ArgumentException("Unknown or missing permission.", nameof(permissions)); }
        var secret = id + "." + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var inserted = await store.TryAddAsync(new(id, name.Trim(), Convert.ToHexString(Digest(secret)), permissions.Distinct().ToArray(), clock.GetUtcNow().UtcDateTime.AddDays(days), false) { Subsystem = string.Empty }, ct);
        return inserted ? secret : null;
    }
    public async Task<(int Status, string? Caller)> CheckAsync(string token, string permission, CancellationToken ct)
    {
        if (token.Length != 97 || token[32] != '.') { return (401, null); }
        var key = await store.FindAsync(token[..32], ct);
        if (key == null || !CryptographicOperations.FixedTimeEquals(Digest(token), Convert.FromHexString(key.Digest))) { return (401, null); }
        if (key.Revoked || key.ExpiresUtc <= clock.GetUtcNow().UtcDateTime) { return (401, key.Id); }
        return (key.Permissions.Contains(permission) ? 200 : 403, key.Id);
    }
    public async Task<KeySummary[]> KeysAsync(CancellationToken ct) => (await store.ListAsync(ct)).Select(k => new KeySummary(k.Id,k.Name,k.Permissions,k.ExpiresUtc,k.Revoked,k.Subsystem)).ToArray();
    public Task DeleteAsync(string id, CancellationToken ct) => store.DeleteAsync(id, ct);
    public async Task<RequestSummary[]> LogsAsync(string? key,CancellationToken ct) => (await store.LogsAsync(key,ct)).Select(r => new RequestSummary(r.Id,r.KeyId,r.Route,r.Status,r.AtUtc,r.DurationMs)).ToArray();
    public Task LogAsync(RequestSummary r,CancellationToken ct) => store.LogAsync(new(r.Id,r.KeyId,r.Route,r.Status,r.AtUtc,r.DurationMs),ct);
}




