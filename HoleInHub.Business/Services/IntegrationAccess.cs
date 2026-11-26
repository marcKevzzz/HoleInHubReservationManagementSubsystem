using HoleInHub.Data.Integrations;
using System.Security.Cryptography;
using System.Text;

namespace HoleInHub.Business.Services;

public sealed record KeySummary(string Id, string Name, string[] Permissions, DateTime ExpiresUtc, bool Revoked, string Subsystem);
public sealed record IntegrationPermission(string Group, string Code, string Label, string Description, bool Live = false);
public sealed record RequestSummary(string Id, string? KeyId, string Route, int Status, DateTime AtUtc, long DurationMs);
public sealed class IntegrationAccess(IntegrationStore store, TimeProvider clock)
{
    public const string ReadServices = "services.read";
    public static IReadOnlyList<IntegrationPermission> Catalog { get; } = [
        new("Service catalog", ReadServices, "Service details", "Read active offers and booking types.", true),
        new("Service catalog", ReadRates, "Service rates", "Read displayed rates for active services.", true),
        new("Service catalog", "services.create", "Create services", "Add new offerings to the catalog."),
        new("Service catalog", "services.update", "Edit service details", "Change titles, descriptions and booking details."),
        new("Service catalog", "rates.update", "Edit rates", "Update prices for existing services."),
        new("Service catalog", "services.publish", "Manage publication", "Publish or withdraw an offering."),
        new("Reservations", "reservations.read", "Reservations", "Read booking schedules and status."),
        new("Reservations", "reserved-bays.read", "Reserved bays", "Read bay assignments for a booking."),
        new("Venue resources", "bays.read", "Bay resources", "Read the venue bay directory."),
        new("Venue resources", "coach-availability.read", "Coach availability", "Read available coaching slots."),
        new("Settlement", "deposits.write", "Verify deposits", "Submit whole-shop deposit verification."),
        new("Settlement", "settlements.write", "Settle checkout", "Submit final settlement for a reservation."),
        new("Customers", "customer-profile.read", "Customer details", "Read approved profile fields with customer authorization.")
    ];
    public static bool ValidPermissions(string[] permissions) => permissions.Length > 0 && permissions.All(p => Catalog.Any(scope => scope.Code == p));
    public const string ReadRates = "rates.read";
    private static byte[] Digest(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    public async Task<string> IssueAsync(string name, int days, string[] permissions, CancellationToken ct)
    {
        if (!ValidPermissions(permissions)) { throw new ArgumentException("Unknown or missing permission.", nameof(permissions)); }
        var id = Guid.NewGuid().ToString("N");
        var secret = id + "." + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await store.AddAsync(new(id, name.Trim(), Convert.ToHexString(Digest(secret)), permissions.Distinct().ToArray(), clock.GetUtcNow().UtcDateTime.AddDays(days), false) { Subsystem = string.Empty }, ct);
        return secret;
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
    public Task RevokeAsync(string id,CancellationToken ct) => store.RevokeAsync(id,ct);
    public async Task<RequestSummary[]> LogsAsync(string? key,CancellationToken ct) => (await store.LogsAsync(key,ct)).Select(r => new RequestSummary(r.Id,r.KeyId,r.Route,r.Status,r.AtUtc,r.DurationMs)).ToArray();
    public Task LogAsync(RequestSummary r,CancellationToken ct) => store.LogAsync(new(r.Id,r.KeyId,r.Route,r.Status,r.AtUtc,r.DurationMs),ct);
}




