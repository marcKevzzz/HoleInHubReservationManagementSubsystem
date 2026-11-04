using HoleInHub.Data.Accounts;

namespace HoleInHub.Business.Accounts;

public sealed record AccountDetails(string Id, string Email, string NormalizedEmail, string PasswordHash,
    string SecurityStamp, string Version, bool EmailConfirmed, int AccessFailedCount, DateTime? LockoutEndUtc, string[] Roles)
{
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Phone { get; init; } = "";
    public string? GoogleSubject { get; init; }
}

public sealed class AccountService(AccountPersistence persistence)
{
    public async Task<AccountDetails?> FindByIdAsync(string id, CancellationToken ct) => Map(await persistence.FindByIdAsync(id, ct));
    public async Task<AccountDetails?> FindByEmailAsync(string email, CancellationToken ct) => Map(await persistence.FindByEmailAsync(email, ct));
    public async Task<AccountDetails?> FindByGoogleSubjectAsync(string subject, CancellationToken ct) => Map(await persistence.FindByGoogleSubjectAsync(subject, ct));
    public Task<bool> CreateAsync(AccountDetails account, CancellationToken ct) => persistence.CreateAsync(ToRecord(account), ct);
    public Task<bool> UpdateAsync(AccountDetails account, string expectedVersion, CancellationToken ct) => persistence.UpdateAsync(ToRecord(account), expectedVersion, ct);
    private static AccountDetails? Map(AccountRecord? a) => a is null ? null : new(a.Id, a.Email, a.NormalizedEmail, a.PasswordHash, a.SecurityStamp, a.Version, a.EmailConfirmed, a.AccessFailedCount, a.LockoutEndUtc, a.Roles) { FirstName = a.FirstName, LastName = a.LastName, Phone = a.Phone, GoogleSubject = a.GoogleSubject };
    private static AccountRecord ToRecord(AccountDetails a) => new() { Id = a.Id, Email = a.Email, NormalizedEmail = a.NormalizedEmail, PasswordHash = a.PasswordHash, SecurityStamp = a.SecurityStamp, Version = a.Version, EmailConfirmed = a.EmailConfirmed, AccessFailedCount = a.AccessFailedCount, LockoutEndUtc = a.LockoutEndUtc, Roles = a.Roles, FirstName = a.FirstName, LastName = a.LastName, Phone = a.Phone, GoogleSubject = a.GoogleSubject };
}

