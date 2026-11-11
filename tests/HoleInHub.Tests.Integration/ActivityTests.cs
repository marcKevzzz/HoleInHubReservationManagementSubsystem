using System.Text.Json;
using HoleInHub.Business.Activity;
using HoleInHub.Data.Accounts;
using HoleInHub.Data.Activity;
using HoleInHub.Data.Customers;
using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class ActivityTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    private static readonly DateTime At = new(2026, 11, 10, 17, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Day = new(2026, 11, 11);
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(At.AddHours(2));
    }

    [Fact]
    public async Task ProjectsPermittedSourcesAndSearchesApprovedFieldsWithoutLeakingPayloads()
    {
        Assert.True(fixture.IsServerAvailable, "Start the MongoDB replica set.");
        var context = fixture.CreateDbContext();
        await context.Database.GetCollection<AccountRecord>("accounts").InsertOneAsync(new()
        { Id = "staff", FirstName = "Jane", LastName = "Employee", PasswordHash = "secret-password", Email = "secret-email" });
        await context.Database.GetCollection<ReservationRecord>("reservations").InsertOneAsync(new()
        {
            Id = "same-id",
            Reference = "HIH-123",
            AccessTokenHash = "secret-token",
            Email = "secret-email",
            History = [new() { AtUtc = At.AddDays(-2), Actor = "staff", Status = "Pending" },
                new() { AtUtc = At, Actor = "staff", EventType = "ReservationConfirmed", Status = "Confirmed", Reason = "private-reason", CommandHash = "secret-command" }]
        });
        await context.Database.GetCollection<ScheduleBlock>("schedule_blocks").InsertManyAsync([
            new() { Id = "same-id", CreatedUtc = At, CreatedBy = "staff", ReleasedUtc = At.AddMinutes(1), ReleasedBy = "staff", StartUtc = At.AddDays(2), EndUtc = At.AddDays(2).AddHours(1), ResourceIds = ["bay-1"] },
            new() { Id = "legacy-no-audit", StartUtc = At, EndUtc = At.AddHours(1) }
        ]);
        await context.Database.GetCollection<CustomerProfileOperationRecord>("customer_profile_operations").InsertOneAsync(new()
        {
            Id = "same-id",
            TargetId = "target-profile",
            OccurredUtc = At,
            ActorId = "deleted-user",
            ActorLabel = "Historical employee",
            Reason = "merge-reason",
            SourceBefore = new() { FirstName = "Alice", Email = "secret-source-email" },
            TargetBefore = new() { FirstName = "Alicia", Phone = "secret-phone" },
            ReservationIds = ["same-id"],
            PayloadHash = "secret-payload"
        });
        await context.Database.GetCollection<StaffRoleAuditRecord>("staff_role_audit").InsertOneAsync(new("same-id", "RolesChanged", At, "staff", "Admin", "target", ["Staff"], ["Manager"], "admin-only-reason", "correlation"));
        var service = new ActivityService(new(context), new Clock());
        var staff = await service.QueryAsync(new(Day, Day), false, "staff", default);
        Assert.Equal(4, staff.Entries.Count);
        Assert.Equal(4, staff.Entries.Select(x => x.Key).Distinct().Count());
        Assert.DoesNotContain(staff.Entries, x => x.Category == "Access");
        var json = JsonSerializer.Serialize(staff);
        Assert.DoesNotContain("secret-", json);
        Assert.DoesNotContain("admin-only-reason", json);
        var booking = Assert.Single(staff.Entries, x => x.Category == "Reservations");
        Assert.Equal("Jane Employee", booking.Actor);
        Assert.Equal(JsonSerializer.Serialize(booking), JsonSerializer.Serialize(await service.GetAsync(booking.Key, false, default)));
        var customer = Assert.Single(staff.Entries, x => x.Category == "Customers");
        Assert.Equal("Historical employee", customer.Actor);
        Assert.Equal("target-profile", customer.TargetId);
        Assert.Equal("Alice / Alicia", customer.TargetLabel);
        Assert.Single((await service.QueryAsync(new(Day, Day, Search: "Historical employee"), false, "staff", default)).Entries);
        Assert.Equal(3, (await service.QueryAsync(new(Day, Day, Search: "Jane Employee"), false, "staff", default)).Entries.Count);
        Assert.Empty((await service.QueryAsync(new(Day, Day, Search: "private-reason"), false, "staff", default)).Entries);
        Assert.Empty((await service.QueryAsync(new(Day, Day, Search: ".*"), false, "staff", default)).Entries);
        Assert.Equal(2, (await service.QueryAsync(new(Day, Day, Search: "bay-1"), false, "staff", default)).Entries.Count);
        var legacyKey = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("b|legacy-no-audit|event")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Null(await service.GetAsync(legacyKey, false, default));
        Assert.Equal(3, (await service.QueryAsync(new(Day, Day, Actor: "mine"), false, "staff", default)).Entries.Count);
        var admin = await service.QueryAsync(new(Day, Day), true, "admin", default);
        var access = Assert.Single(admin.Entries, x => x.Category == "Access");
        Assert.Null(await service.GetAsync(access.Key, false, default));
        Assert.NotNull(await service.GetAsync(access.Key, true, default));
        Assert.Null(await service.GetAsync("invalid!", true, default));
    }

    [Fact]
    public async Task SameTimeCursorIsStableAcrossNewAppendsAndPagesAreBounded()
    {
        Assert.True(fixture.IsServerAvailable, "Start the MongoDB replica set.");
        var context = fixture.CreateDbContext();
        var collection = context.Database.GetCollection<ReservationRecord>("reservations");
        await collection.InsertOneAsync(new()
        { Id = "booking", Reference = "HIH-many", History = Enumerable.Range(0, 115).Select(_ => new HistoryRecord { AtUtc = At, Actor = "System", Status = "Confirmed" }).ToList() });
        var service = new ActivityService(new(context), new Clock());
        var first = await service.QueryAsync(new(Day, Day), false, "staff", default);
        Assert.Equal(50, first.Entries.Count);
        Assert.NotNull(first.NextCursor);
        await collection.UpdateOneAsync(x => x.Id == "booking", Builders<ReservationRecord>.Update.Push(x => x.History,
            new HistoryRecord { AtUtc = At.AddHours(3), Actor = "System", Status = "Expired" }));
        var seen = first.Entries.Select(x => x.Key).ToList();
        var next = first.NextCursor;
        while (next is not null)
        {
            var page = await service.QueryAsync(new(Day, Day, Cursor: next, AsOfUtc: first.AsOfUtc), false, "staff", default);
            Assert.InRange(page.Entries.Count, 1, 50);
            seen.AddRange(page.Entries.Select(x => x.Key));
            next = page.NextCursor;
        }
        Assert.Equal(115, seen.Count);
        Assert.Equal(115, seen.Distinct().Count());
    }
}
