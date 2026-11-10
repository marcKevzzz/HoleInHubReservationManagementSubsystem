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

public sealed record StaffRoleChange(string ActorId, string ActorStamp, string TargetId, string ExpectedVersion,
    string? RequestedRole, string? Reason, string CorrelationId);

public enum StaffRoleChangeResult { Applied, Unchanged, Invalid, Forbidden, NotFound, Stale, Unavailable }

public sealed class AccountService(AccountPersistence persistence, TimeProvider clock)
{
    public async Task<AccountDirectory> GetDirectoryAsync(bool employeesOnly, int page, CancellationToken ct)
    {
        const int pageSize = 50;
        page = Math.Clamp(page, 1, 1000);
        var rows = await persistence.ReadDirectoryAsync(employeesOnly ? StaffRoles.Workspace : null, (page - 1) * pageSize, pageSize + 1, ct);
        return new(rows.Take(pageSize).Select(x => new AccountDirectoryEntry(x.Id, x.Email,
            $"{x.FirstName} {x.LastName}".Trim(), x.Roles, x.EmailConfirmed)).ToArray(), page, rows.Count > pageSize && page < 1000);
    }
    public async Task<StaffRoleChangeResult> ChangeStaffRoleAsync(StaffRoleChange command, CancellationToken ct)
    {
        var reason = command.Reason?.Trim();
        if (reason is not { Length: >= 1 and <= 500 }
            || (command.RequestedRole is not null && !StaffRoles.Workspace.Contains(command.RequestedRole))
            || string.IsNullOrWhiteSpace(command.ExpectedVersion))
        {
            return StaffRoleChangeResult.Invalid;
        }
        if (string.IsNullOrEmpty(command.ActorId) || string.IsNullOrEmpty(command.ActorStamp) || command.ActorId == command.TargetId)
        {
            return StaffRoleChangeResult.Forbidden;
        }

        var target = await persistence.FindByIdAsync(command.TargetId, ct);
        if (target is null) { return StaffRoleChangeResult.NotFound; }
        if (target.Version != command.ExpectedVersion) { return StaffRoleChangeResult.Stale; }

        var previous = target.Roles.Where(StaffRoles.Workspace.Contains).ToArray();
        string[] next = command.RequestedRole is { } role ? [role] : [];
        if (previous.SequenceEqual(next)) { return StaffRoleChangeResult.Unchanged; }
        if (next.Length > 0 && (!target.EmailConfirmed || string.IsNullOrWhiteSpace(target.PasswordHash)))
        {
            return StaffRoleChangeResult.Invalid;
        }

        var roles = target.Roles.Where(role => !StaffRoles.Workspace.Contains(role)).Concat(next).ToArray();
        var audit = new StaffRoleAuditRecord(Guid.NewGuid().ToString("N"), "StaffRoleChanged", clock.GetUtcNow().UtcDateTime,
            command.ActorId, StaffRoles.Admin, target.Id, previous, next, reason, command.CorrelationId);
        var result = await persistence.ApplyStaffRoleChangeAsync(command.ActorStamp, command.ExpectedVersion,
            roles.Length == 0 ? ["Customer"] : roles, audit, ct);
        return result switch
        {
            StaffRoleWriteResult.Applied => StaffRoleChangeResult.Applied,
            StaffRoleWriteResult.Forbidden => StaffRoleChangeResult.Forbidden,
            StaffRoleWriteResult.Stale => StaffRoleChangeResult.Stale,
            _ => StaffRoleChangeResult.Unavailable
        };
    }

    public async Task<AccountDetails?> FindByIdAsync(string id, CancellationToken ct) => Map(await persistence.FindByIdAsync(id, ct));
    public async Task<AccountDetails?> FindByEmailAsync(string email, CancellationToken ct) => Map(await persistence.FindByEmailAsync(email, ct));
    public async Task<AccountDetails?> FindByGoogleSubjectAsync(string subject, CancellationToken ct) => Map(await persistence.FindByGoogleSubjectAsync(subject, ct));
    public Task<bool> CreateAsync(AccountDetails account, CancellationToken ct) => persistence.CreateAsync(ToRecord(account), ct);
    public Task<bool> UpdateAsync(AccountDetails account, string expectedVersion, CancellationToken ct) => persistence.UpdateAsync(ToRecord(account), expectedVersion, ct);
    private static AccountDetails? Map(AccountRecord? a) => a is null ? null : new(a.Id, a.Email, a.NormalizedEmail, a.PasswordHash, a.SecurityStamp, a.Version, a.EmailConfirmed, a.AccessFailedCount, a.LockoutEndUtc, a.Roles) { FirstName = a.FirstName, LastName = a.LastName, Phone = a.Phone, GoogleSubject = a.GoogleSubject };
    private static AccountRecord ToRecord(AccountDetails a) => new() { Id = a.Id, Email = a.Email, NormalizedEmail = a.NormalizedEmail, PasswordHash = a.PasswordHash, SecurityStamp = a.SecurityStamp, Version = a.Version, EmailConfirmed = a.EmailConfirmed, AccessFailedCount = a.AccessFailedCount, LockoutEndUtc = a.LockoutEndUtc, Roles = a.Roles, FirstName = a.FirstName, LastName = a.LastName, Phone = a.Phone, GoogleSubject = a.GoogleSubject };
}

public sealed record AccountDirectoryEntry(string Id, string Email, string Name, string[] Roles, bool EmailConfirmed);
public sealed record AccountDirectory(IReadOnlyList<AccountDirectoryEntry> Entries, int Page, bool HasMore);

