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

public sealed record StaffPinReset(string ActorId, string ActorStamp, string TargetId, string ExpectedVersion,
    string CommandId, string Reason);

public enum TeamCreationResult { Created, Duplicate, Forbidden, Unavailable, Invalid }

public enum StaffRoleChangeResult { Applied, Unchanged, Invalid, Forbidden, NotFound, Stale, Unavailable }

public sealed class AccountService(AccountPersistence persistence, TimeProvider clock)
{
    public async Task<AccountDirectory> GetDirectoryAsync(bool employeesOnly, int page, CancellationToken ct, string? role = null)
    {
        const int pageSize = 20;
        page = Math.Clamp(page, 1, 1000);
        if (role is not null && !StaffRoles.Workspace.Contains(role)) { throw new ArgumentException("Choose a staff role.", nameof(role)); }
        IReadOnlyList<string>? roles = role is not null ? [role] : employeesOnly ? StaffRoles.Workspace : null;
        var rows = await persistence.ReadDirectoryAsync(roles, (page - 1) * pageSize, pageSize + 1, ct);
        return new(rows.Take(pageSize).Select(x => new AccountDirectoryEntry(x.Id, x.Email,
            $"{x.FirstName} {x.LastName}".Trim(), x.Roles, x.EmailConfirmed)).ToArray(), page, rows.Count > pageSize && page < 1000);
    }
    public async Task<RoleActivityPage> GetRoleActivityAsync(int page, CancellationToken ct)
    {
        const int pageSize = 20;
        page = Math.Clamp(page, 1, 1000);
        var rows = await persistence.ReadRoleActivityAsync((page - 1) * pageSize, pageSize + 1, ct);
        if (rows.Count == 0) { return new([], page, false); }

        var entries = rows.Take(pageSize).ToArray();
        var accountIds = entries.SelectMany(row => new[] { row.ActorAccountId, row.TargetAccountId }).Distinct();
        var emails = await persistence.ReadAccountEmailsAsync(accountIds, ct);
        return new(entries.Select(row => new RoleActivityEntry(row.OccurredUtc,
            emails.GetValueOrDefault(row.ActorAccountId) ?? row.ActorAccountId,
            emails.GetValueOrDefault(row.TargetAccountId) ?? row.TargetAccountId,
            row.PreviousRoles, row.NewRoles, row.Reason)).ToArray(), page, rows.Count > pageSize && page < 1000);
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

    public async Task<StaffRoleChangeResult> ResetStaffPinAsync(StaffPinReset command, CancellationToken ct)
    {
        if (command.ActorId == command.TargetId || string.IsNullOrWhiteSpace(command.ActorId) || string.IsNullOrWhiteSpace(command.ActorStamp))
        { return StaffRoleChangeResult.Forbidden; }
        if (!Guid.TryParseExact(command.CommandId, "N", out _) || string.IsNullOrWhiteSpace(command.ExpectedVersion)
            || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length > 500)
        { return StaffRoleChangeResult.Invalid; }
        var target = await persistence.FindByIdAsync(command.TargetId, ct);
        if (target is null) { return StaffRoleChangeResult.NotFound; }
        if (!target.EmailConfirmed || target.Roles.Contains(StaffRoles.Admin) || !target.Roles.Any(role => role is StaffRoles.Staff or StaffRoles.Manager))
        { return StaffRoleChangeResult.Invalid; }
        var audit = new StaffRoleAuditRecord(command.CommandId, "StaffPinReset", clock.GetUtcNow().UtcDateTime,
            command.ActorId, StaffRoles.Admin, target.Id, target.Roles, target.Roles, command.Reason.Trim(), command.CommandId);
        var notice = new HoleInHub.Data.Reservations.OutboxRecord
        {
            Id = audit.Id + ":pin-reset",
            Recipient = target.Email,
            Subject = "Your Hole In Hub staff PIN was reset",
            Body = "An administrator reset your staff PIN. Your old PIN and staff sessions no longer work. On an authorized staff desktop, open staff sign-in and choose Set up or reset your PIN. Enter your work email and password, verify the emailed code, then choose and confirm a new six-digit PIN. Your administrator never needs your PIN or verification code. If you did not expect this change, contact your administrator.",
            DueUtc = audit.OccurredUtc
        };
        return await persistence.ResetStaffPinAsync(command.ActorStamp, command.ExpectedVersion, audit, notice, ct) switch
        {
            StaffRoleWriteResult.Applied => StaffRoleChangeResult.Applied,
            StaffRoleWriteResult.Forbidden => StaffRoleChangeResult.Forbidden,
            StaffRoleWriteResult.Stale => StaffRoleChangeResult.Stale,
            _ => StaffRoleChangeResult.Unavailable
        };
    }

    public async Task<TeamCreationResult> CreateTeamMemberAsync(AccountDetails account, string role,
        string actorId, string actorStamp, string protectedInvitation, CancellationToken ct, string? resendId = null)
    {
        if (!StaffRoles.Workspace.Contains(role) || !ContactRules.IsName(account.FirstName) || !ContactRules.IsName(account.LastName)
            || !ContactRules.IsPhone(account.Phone) || account.Email.Length is < 3 or > 254
            || !System.Net.Mail.MailAddress.TryCreate(account.Email, out var email) || email.Address != account.Email
            || !protectedInvitation.StartsWith("protected:", StringComparison.Ordinal)
            || account.EmailConfirmed || account.PasswordHash.Length == 0 || !Guid.TryParseExact(account.Id, "N", out _))
        { return TeamCreationResult.Invalid; }
        if (resendId is not null && !Guid.TryParseExact(resendId, "N", out _)) { return TeamCreationResult.Invalid; }
        var actorRoles = role == StaffRoles.Admin ? new[] { StaffRoles.Admin } : [StaffRoles.Admin, StaffRoles.Manager];
        var result = await persistence.CreateManagedAsync(ToRecord(account with { Roles = [role] }), actorId, actorStamp,
            actorRoles, protectedInvitation, clock.GetUtcNow().UtcDateTime, ct, resendId);
        return result switch
        {
            ManagedAccountResult.Created => TeamCreationResult.Created,
            ManagedAccountResult.Duplicate => TeamCreationResult.Duplicate,
            ManagedAccountResult.Forbidden => TeamCreationResult.Forbidden,
            ManagedAccountResult.Unavailable => TeamCreationResult.Unavailable,
            _ => TeamCreationResult.Invalid
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

public sealed record RoleActivityEntry(DateTime OccurredUtc, string Actor, string Target, string[] PreviousRoles,
    string[] NewRoles, string Reason);

public sealed record RoleActivityPage(IReadOnlyList<RoleActivityEntry> Entries, int Page, bool HasMore);

