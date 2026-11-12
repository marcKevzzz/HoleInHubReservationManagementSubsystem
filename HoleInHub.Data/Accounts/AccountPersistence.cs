using HoleInHub.Data.Context;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HoleInHub.Data.Accounts;

public sealed record AccountDirectoryRow(string Id, string Email, string FirstName, string LastName, string[] Roles, bool EmailConfirmed);

public enum ManagedAccountResult { Created, Duplicate, Forbidden, Unavailable, Invalid }

public enum StaffRoleWriteResult { Applied, Forbidden, Stale, Unavailable }

public sealed class AccountPersistence(IMongoDbContext context)
{
    internal const string RoleGuards = "staff_role_guards";
    internal const string RoleGuardId = "staff-role-administration";
    internal const string AccountCollection = "accounts";
    private IMongoCollection<AccountRecord> Accounts => context.Database.GetCollection<AccountRecord>(AccountCollection);
    private IMongoCollection<StaffRoleAuditRecord> RoleAudit =>
        context.Database.GetCollection<StaffRoleAuditRecord>(StaffRoleAuditRecord.CollectionName);
    public Task<AccountRecord?> FindByIdAsync(string id, CancellationToken ct) => Accounts.Find(x => x.Id == id).FirstOrDefaultAsync(ct)!;
    public Task<AccountRecord?> FindByEmailAsync(string email, CancellationToken ct) => Accounts.Find(x => x.NormalizedEmail == email).FirstOrDefaultAsync(ct)!;
    public Task<AccountRecord?> FindByGoogleSubjectAsync(string subject, CancellationToken ct) => Accounts.Find(x => x.GoogleSubject == subject).FirstOrDefaultAsync(ct)!;
    public Task<List<AccountDirectoryRow>> ReadDirectoryAsync(IReadOnlyList<string>? roles, int offset, int limit, CancellationToken ct)
    {
        var filter = roles is null ? Builders<AccountRecord>.Filter.Empty : Builders<AccountRecord>.Filter.AnyIn(x => x.Roles, roles);
        return Accounts.Find(filter).SortBy(x => x.NormalizedEmail).ThenBy(x => x.Id).Skip(offset).Limit(limit)
            .Project(x => new AccountDirectoryRow(x.Id, x.Email, x.FirstName, x.LastName, x.Roles, x.EmailConfirmed)).ToListAsync(ct);
    }
    public Task<List<StaffRoleAuditRecord>> ReadRoleActivityAsync(int offset, int limit, CancellationToken ct) =>
        RoleAudit.Find(FilterDefinition<StaffRoleAuditRecord>.Empty)
            .SortByDescending(entry => entry.OccurredUtc).ThenByDescending(entry => entry.Id)
            .Skip(offset).Limit(limit).ToListAsync(ct);

    public async Task<IReadOnlyDictionary<string, string>> ReadAccountEmailsAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var rows = await Accounts.Find(Builders<AccountRecord>.Filter.In(account => account.Id, ids))
            .Project(account => new { account.Id, account.Email }).ToListAsync(ct);
        return rows.ToDictionary(row => row.Id, row => row.Email, StringComparer.Ordinal);
    }

    public async Task<StaffRoleWriteResult> ApplyStaffRoleChangeAsync(string actorStamp, string expectedVersion,
        string[] roles, StaffRoleAuditRecord audit, CancellationToken ct)
    {
        var guards = context.Database.GetCollection<BsonDocument>(RoleGuards);
        var update = Builders<AccountRecord>.Update.Set(x => x.Roles, roles)
            .Set(x => x.Version, Guid.NewGuid().ToString("N"))
            .Set(x => x.SecurityStamp, Guid.NewGuid().ToString("N"));

        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (transaction, token) =>
        {
            // Serialize role changes before checking the actor, including mutual demotions.
            var guard = await guards.UpdateOneAsync(transaction, new BsonDocument("_id", RoleGuardId),
                new BsonDocument("$inc", new BsonDocument("version", 1L)), cancellationToken: token);
            if (guard.MatchedCount == 0) { return StaffRoleWriteResult.Unavailable; }

            var filters = Builders<AccountRecord>.Filter;
            var actorFilter = filters.Where(x => x.Id == audit.ActorAccountId && x.EmailConfirmed
                && x.SecurityStamp == actorStamp && (x.LockoutEndUtc == null || x.LockoutEndUtc <= audit.OccurredUtc))
                & filters.AnyEq(x => x.Roles, audit.ActorRole);
            var actor = await Accounts.Find(transaction, actorFilter).AnyAsync(token);
            if (!actor) { return StaffRoleWriteResult.Forbidden; }

            var result = await Accounts.UpdateOneAsync(transaction,
                x => x.Id == audit.TargetAccountId && x.Version == expectedVersion, update, cancellationToken: token);
            if (result.MatchedCount == 0) { return StaffRoleWriteResult.Stale; }

            // Make concurrent password or lockout changes to the acting Admin contend with this write.
            await Accounts.UpdateOneAsync(transaction, actorFilter,
                Builders<AccountRecord>.Update.Set(x => x.Version, Guid.NewGuid().ToString("N")), cancellationToken: token);
            await RoleAudit.InsertOneAsync(transaction, audit, cancellationToken: token);
            return StaffRoleWriteResult.Applied;
        }, cancellationToken: ct);
    }

    public async Task<StaffRoleWriteResult> ResetStaffPinAsync(string actorStamp, string expectedVersion,
        StaffRoleAuditRecord audit, CancellationToken ct)
    {
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(async (transaction, token) =>
        {
            var guard = await context.Database.GetCollection<BsonDocument>(RoleGuards).UpdateOneAsync(transaction,
                new BsonDocument("_id", RoleGuardId), new BsonDocument("$inc", new BsonDocument("version", 1L)), cancellationToken: token);
            if (guard.MatchedCount == 0) { return StaffRoleWriteResult.Unavailable; }
            var actorFilter = Builders<AccountRecord>.Filter.Where(x => x.Id == audit.ActorAccountId && x.EmailConfirmed
                && x.SecurityStamp == actorStamp && (x.LockoutEndUtc == null || x.LockoutEndUtc <= audit.OccurredUtc))
                & Builders<AccountRecord>.Filter.AnyEq(x => x.Roles, audit.ActorRole);
            if (!await Accounts.Find(transaction, actorFilter).AnyAsync(token)) { return StaffRoleWriteResult.Forbidden; }
            var prior = await RoleAudit.Find(transaction, x => x.Id == audit.Id).FirstOrDefaultAsync(token);
            if (prior is not null)
            {
                return prior.EventType == audit.EventType && prior.ActorAccountId == audit.ActorAccountId
                    && prior.TargetAccountId == audit.TargetAccountId && prior.Reason == audit.Reason
                    ? StaffRoleWriteResult.Applied : StaffRoleWriteResult.Stale;
            }
            var changed = await Accounts.UpdateOneAsync(transaction, x => x.Id == audit.TargetAccountId && x.Version == expectedVersion,
                Builders<AccountRecord>.Update.Set(x => x.Version, Guid.NewGuid().ToString("N"))
                    .Set(x => x.SecurityStamp, Guid.NewGuid().ToString("N")), cancellationToken: token);
            if (changed.MatchedCount == 0) { return StaffRoleWriteResult.Stale; }
            await context.Database.GetCollection<StaffPinCredential>("staff_pin_credentials").DeleteOneAsync(transaction,
                x => x.AccountId == audit.TargetAccountId, cancellationToken: token);
            await RoleAudit.InsertOneAsync(transaction, audit, cancellationToken: token);
            await Accounts.UpdateOneAsync(transaction, actorFilter,
                Builders<AccountRecord>.Update.Set(x => x.Version, Guid.NewGuid().ToString("N")), cancellationToken: token);
            return StaffRoleWriteResult.Applied;
        }, new TransactionOptions(ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
    }

    public async Task<ManagedAccountResult> CreateManagedAsync(AccountRecord account, string actorId, string actorStamp,
        string[] permittedActorRoles, string invitation, DateTime now, CancellationToken ct, string? resendId = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var session = await context.Client.StartSessionAsync(cancellationToken: timeout.Token);
        try
        {
            return await session.WithTransactionAsync(async (transaction, token) =>
            {
                var guard = await context.Database.GetCollection<BsonDocument>(RoleGuards).UpdateOneAsync(transaction,
                    new BsonDocument("_id", RoleGuardId), new BsonDocument("$inc", new BsonDocument("version", 1L)), cancellationToken: token);
                if (guard.MatchedCount == 0) { return ManagedAccountResult.Unavailable; }
                var actorFilter = Builders<AccountRecord>.Filter.Where(x => x.Id == actorId && x.EmailConfirmed
                    && x.SecurityStamp == actorStamp && (x.LockoutEndUtc == null || x.LockoutEndUtc <= now))
                    & Builders<AccountRecord>.Filter.AnyIn(x => x.Roles, permittedActorRoles);
                var actor = await Accounts.Find(transaction, actorFilter).FirstOrDefaultAsync(token);
                if (actor is null) { return ManagedAccountResult.Forbidden; }
                var prior = await Accounts.Find(transaction, x => x.Id == account.Id).FirstOrDefaultAsync(token);
                if (prior is not null && resendId is null)
                {
                    return prior.NormalizedEmail == account.NormalizedEmail && prior.Roles.SequenceEqual(account.Roles)
                        && prior.FirstName == account.FirstName && prior.LastName == account.LastName && prior.Phone == account.Phone
                        ? ManagedAccountResult.Created : ManagedAccountResult.Duplicate;
                }
                if (resendId is not null && (prior is null || prior.EmailConfirmed || prior.Version != account.Version
                    || prior.SecurityStamp != account.SecurityStamp || !prior.Roles.SequenceEqual(account.Roles)))
                { return ManagedAccountResult.Invalid; }
                var messageId = resendId is null ? "team-invite:" + account.Id : "team-reinvite:" + resendId;
                var messages = context.Database.GetCollection<HoleInHub.Data.Reservations.OutboxRecord>("outbox");
                if (await messages.Find(transaction, x => x.Id == messageId).AnyAsync(token)) { return ManagedAccountResult.Created; }
                if (prior is null) { await Accounts.InsertOneAsync(transaction, account, cancellationToken: token); }
                await messages.InsertOneAsync(transaction,
                    new() { Id = messageId, Recipient = account.Email, Subject = "Activate your Hole In Hub team account", Body = invitation, DueUtc = now }, cancellationToken: token);
                await RoleAudit.InsertOneAsync(transaction, new(resendId ?? account.Id, resendId is null ? "TeamMemberCreated" : "TeamInvitationResent", now, actorId,
                    actor.Roles.First(permittedActorRoles.Contains), account.Id, [], account.Roles, resendId is null ? "Team account created. Activation required." : "Activation email resent.", account.Id), cancellationToken: token);
                await Accounts.UpdateOneAsync(transaction, actorFilter,
                    Builders<AccountRecord>.Update.Set(x => x.Version, Guid.NewGuid().ToString("N")), cancellationToken: token);
                return ManagedAccountResult.Created;
            }, new TransactionOptions(ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), timeout.Token);
        }
        catch (MongoWriteException error) when (error.WriteError.Category == ServerErrorCategory.DuplicateKey)
        { return ManagedAccountResult.Duplicate; }
    }

    public async Task<bool> CreateAsync(AccountRecord account, CancellationToken ct)
    {
        try { await Accounts.InsertOneAsync(account, cancellationToken: ct); return true; }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
    }
    public async Task<bool> UpdateAsync(AccountRecord account, string expectedVersion, CancellationToken ct)
    {
        try
        {
            var result = await Accounts.ReplaceOneAsync(x => x.Id == account.Id && x.Version == expectedVersion, account, cancellationToken: ct);
            return result.ModifiedCount == 1;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { return false; }
    }
}
