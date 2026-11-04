using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Reservations;

[Trait("Category", "Integration")]
public sealed class GuestAccessTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    [Fact]
    public async Task DeliveryQuotaSurvivesBrowserChangesAndRedemptionIsSingleUseAndPurposeBound()
    {
        Assert.True(fixture.IsServerAvailable);
        var context = fixture.CreateDbContext();
        var codes = new EmailVerificationPersistence(context);
        var now = DateTime.UtcNow;
        var quota = new EmailDeliveryQuota("email-hash", 5, TimeSpan.FromHours(1), TimeSpan.FromMinutes(1));
        GuestVerificationRecord Code(string id, DateTime sent) => new() { Id = id, Purpose = "GuestRecovery", Email = "guest@example.test", CodeHash = "hash", SentUtc = sent, ExpiresUtc = sent.AddMinutes(10) };
        OutboxRecord Message(string id) => new() { Id = id, Recipient = "guest@example.test", DueUtc = now };
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => codes.IssueAsync(Code("browser" + i, now), Message("mail" + i), default, quota)));
        Assert.Equal(1, attempts.Count(x => x));
        Assert.False(await codes.IssueAsync(Code("another", now.AddSeconds(59)), Message("early"), default, quota));
        for (var i = 1; i < 5; i++) { Assert.True(await codes.IssueAsync(Code("browser" + (i + 10), now.AddMinutes(i)), Message("later" + i), default, quota)); }
        Assert.False(await codes.IssueAsync(Code("sixth", now.AddMinutes(5)), Message("sixth"), default, quota));
        Assert.True(await codes.IssueAsync(Code("renewed", now.AddHours(1)), Message("renewed"), default, quota));
        Assert.False(await codes.VerifyAsync("renewed", "guest@example.test", "hash", now.AddHours(1), default, "AccountEmail", true));
        var redemption = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => codes.VerifyAsync("renewed", "guest@example.test", "hash", now.AddHours(1), default, "GuestRecovery", true)));
        Assert.Equal(1, redemption.Count(x => x));
        Assert.False(await codes.VerifyAsync("renewed", "guest@example.test", "hash", now.AddHours(1), default, "GuestRecovery", true));
        // A duplicate outbox ID rolls back the quota and the new challenge together.
        Assert.False(await codes.IssueAsync(Code("rolled-back", now.AddHours(2)), Message("renewed"), default, quota));
        Assert.True(await codes.IssueAsync(Code("after-rollback", now.AddHours(2)), Message("after-rollback"), default, quota));
    }

    [Fact]
    public async Task ReplacementCodeInvalidatesPreviousCodeAndFiveWrongAttemptsLockTheChallenge()
    {
        Assert.True(fixture.IsServerAvailable);
        var context = fixture.CreateDbContext(); var codes = new EmailVerificationPersistence(context); var now = DateTime.UtcNow;
        var record = new GuestVerificationRecord { Id = "scope", Purpose = "GuestRecovery", Email = "g@example.test", CodeHash = "old", SentUtc = now, ExpiresUtc = now.AddMinutes(10) };
        Assert.True(await codes.IssueAsync(record, new() { Id = "first" }, default));
        record.SentUtc = now.AddMinutes(1); record.ExpiresUtc = now.AddMinutes(11); record.CodeHash = "new";
        Assert.True(await codes.IssueAsync(record, new() { Id = "second" }, default));
        Assert.False(await codes.VerifyAsync("scope", record.Email, "old", now.AddMinutes(1), default, "GuestRecovery", true));
        for (var i = 0; i < 4; i++) { Assert.False(await codes.VerifyAsync("scope", record.Email, "wrong", now.AddMinutes(1), default, "GuestRecovery", true)); }
        Assert.False(await codes.VerifyAsync("scope", record.Email, "new", now.AddMinutes(1), default, "GuestRecovery", true));
    }

    [Fact]
    public async Task HistoryIncludesConfirmedGuestsAfterCodeCleanupButNeverAccountOrUnverifiedRecords()
    {
        Assert.True(fixture.IsServerAvailable);
        var context = fixture.CreateDbContext();
        var now = DateTime.UtcNow;
        var records = Enumerable.Range(0, 27).Select(i => new ReservationRecord
        {
            Id = i.ToString("D3"),
            AccountId = "guest:original-session",
            EmailVerificationId = "durable-marker",
            Email = "GUEST@example.test",
            Status = "Confirmed",
            StartUtc = now.AddDays(1),
            EndUtc = now.AddDays(1).AddHours(1)
        }).ToList();
        records.Add(new() { AccountId = "account", Email = "guest@example.test", EmailVerificationId = "marker", Status = "Confirmed", EndUtc = now.AddDays(1) });
        records.Add(new() { AccountId = "guest:legacy", Email = "guest@example.test", Status = "Confirmed", EndUtc = now.AddDays(1) });
        records.Add(new() { AccountId = "guest:other", Email = "other@example.test", EmailVerificationId = "marker", Status = "Confirmed", EndUtc = now.AddDays(1) });
        records.Add(new() { Id = "past", AccountId = "guest:old", Email = "guest@example.test", EmailVerificationId = "marker", Status = "Expired", EndUtc = now.AddDays(1) });
        await context.Database.GetCollection<ReservationRecord>("reservations").InsertManyAsync(records);
        var store = new ReservationPersistence(context);
        var first = await store.GuestHistoryAsync("guest@example.test", false, now, 1, default);
        Assert.Equal(27, first.UpcomingCount); Assert.Equal(1, first.PastCount); Assert.Equal(25, first.Items.Count); Assert.True(first.HasMore);
        var second = await store.GuestHistoryAsync("guest@example.test", false, now, 2, default);
        Assert.Equal(2, second.Items.Count); Assert.False(second.HasMore);
        Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
        Assert.NotNull(await store.GuestDetailAsync("guest@example.test", "000", default));
        Assert.Null(await store.GuestDetailAsync("other@example.test", "000", default));
        Assert.Single((await store.GuestHistoryAsync("guest@example.test", true, now, 1, default)).Items);
    }

    [Fact]
    public async Task RecoveryMigrationIsRepeatableAndAllowsOnlyTheNewPurpose()
    {
        Assert.True(fixture.IsServerAvailable);
        var context = fixture.CreateDbContext();
        await new Migration_002_Reservations().UpAsync(context.Database, default);
        await new AccountsMigration().UpAsync(context.Database, default);
        await new Migration_004_BookingProcess().UpAsync(context.Database, default);
        await new Migration_005_GoogleAccounts().UpAsync(context.Database, default);
        await new Migration_006_SchemaHardening().UpAsync(context.Database, default);
        var migration = new Migration_007_GuestReservationAccess();
        await migration.UpAsync(context.Database, default); await migration.UpAsync(context.Database, default);
        var codes = context.Database.GetCollection<GuestVerificationRecord>("guest_verifications");
        var code = new GuestVerificationRecord { Id = "new", Email = "guest@example.test", CodeHash = "hash", Purpose = "GuestRecovery", SentUtc = DateTime.UtcNow, ExpiresUtc = DateTime.UtcNow.AddMinutes(10) };
        await codes.InsertOneAsync(code);
        code.Id = "bad"; code.Purpose = "Unknown";
        await Assert.ThrowsAsync<MongoWriteException>(() => codes.InsertOneAsync(code));
    }
}
