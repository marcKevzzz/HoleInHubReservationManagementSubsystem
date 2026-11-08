using HoleInHub.Business.Reservations;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Reservations;

[Trait("Category", "Integration")]
public sealed class CustomerQueryTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    private static ReservationRecord Contact(string owner, string first = "Alex") => new()
    {
        AccountId = owner,
        Email = "shared@example.test",
        FirstName = first,
        LastName = "Santos",
        Phone = "09175550123",
        Reference = "HIH-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
        Service = "Regular",
        Status = "Confirmed",
        StartUtc = DateTime.UtcNow.AddDays(1),
        EndUtc = DateTime.UtcNow.AddDays(1).AddHours(1),
        CreatedUtc = DateTime.UtcNow,
        ReviewDeadlineUtc = DateTime.UtcNow,
        Attendees = 2,
        ResourceIds = ["bay-2"],
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        SubmissionHash = "test"
    };
    private async Task<(ReservationService Service, HoleInHub.Data.Context.IMongoDbContext Context)> Setup()
    {
        Assert.True(fixture.IsServerAvailable, "Start the local MongoDB replica set.");
        var context = fixture.CreateDbContext();
        await new Migration_002_Reservations().UpAsync(context.Database, default);
        return (new(new ReservationPersistence(context), new OutboxPersistence(context), Options.Create(new ReservationPolicy())), context);
    }
    [Fact]
    public async Task ProfilesSeparateAccountGuestAndWalkInIdentitiesAndIncludeLegacyAccountRecords()
    {
        var (service, context) = await Setup();
        var account = Contact("account-one"); account.CustomerAccountId = "account-one";
        var legacy = Contact("account-one", "Earlier");
        legacy.CreatedUtc = DateTime.UtcNow.AddDays(-10);
        legacy.Status = "Completed"; legacy.StartUtc = DateTime.UtcNow.AddDays(-9); legacy.EndUtc = legacy.StartUtc.AddHours(1);
        var guest = Contact("guest:first"); guest.EmailVerificationId = "verified-first";
        var secondGuest = Contact("guest:second"); secondGuest.EmailVerificationId = "verified-second";
        secondGuest.Email = "SHARED@example.test"; secondGuest.Status = "Cancelled";
        var walkIn = Contact("staff:employee");
        var anotherWalkIn = Contact("staff:employee");
        var unverified = Contact("guest:unverified");
        await context.Database.GetCollection<ReservationRecord>("reservations").InsertManyAsync([account, legacy, guest, secondGuest, walkIn, anotherWalkIn, unverified]);

        var directory = await service.SearchCustomersAsync(null, 1, default);
        Assert.Equal(5, directory.Items.Count);
        Assert.Single(directory.Items, row => row.Kind == "Account");
        Assert.Single(directory.Items, row => row.Kind == "Guest");
        Assert.Equal(3, directory.Items.Count(row => row.Kind == "BookingContact"));

        var accountProfile = (await service.GetCustomerProfileAsync(legacy.Id, false, 1, default))!;
        Assert.Equal(2, accountProfile.Total);
        Assert.Equal(1, accountProfile.Completed);
        Assert.Equal(1, accountProfile.Upcoming);
        Assert.Equal(account.FirstName, accountProfile.Customer.FirstName);
        var guestProfile = (await service.GetCustomerProfileAsync(guest.Id, false, 1, default))!;
        Assert.Equal(2, guestProfile.Total);
        Assert.Equal(1, guestProfile.Cancelled);
        Assert.Equal(1, (await service.GetCustomerProfileAsync(walkIn.Id, false, 1, default))!.Total);
        Assert.Single((await service.GetCustomerProfileAsync(account.Id, true, 1, default))!.Bookings);
        Assert.Null(await service.GetCustomerProfileAsync("missing", false, 1, default));
    }
    [Fact]
    public async Task ProfileHistoryPagesDoNotDropOrDuplicateReservations()
    {
        var (service, context) = await Setup();
        var rows = Enumerable.Range(0, 27).Select(_ => Contact("one-customer")).ToArray();
        await context.Database.GetCollection<ReservationRecord>("reservations").InsertManyAsync(rows);
        var first = (await service.GetCustomerProfileAsync(rows[0].Id, false, 1, default))!;
        var second = (await service.GetCustomerProfileAsync(rows[0].Id, false, 2, default))!;
        Assert.Equal(27, first.Total);
        Assert.Equal(25, first.Bookings.Count);
        Assert.True(first.HasMore);
        Assert.Equal(2, second.Bookings.Count);
        Assert.False(second.HasMore);
        Assert.Empty(first.Bookings.Select(x => x.Id).Intersect(second.Bookings.Select(x => x.Id)));
    }

    [Fact]
    public async Task SearchEscapesPatternsNormalizesPhonesAndPaginatesDistinctContacts()
    {
        var (service, context) = await Setup();
        var rows = Enumerable.Range(0, 27).Select(index => Contact("account-" + index, "María")).ToArray();
        await context.Database.GetCollection<ReservationRecord>("reservations").InsertManyAsync(rows);
        var first = await service.SearchCustomersAsync("María", 1, default);
        Assert.Equal(25, first.Items.Count);
        Assert.True(first.HasMore);
        var second = await service.SearchCustomersAsync("María", 2, default);
        Assert.Equal(2, second.Items.Count);
        Assert.False(second.HasMore);
        Assert.Empty(first.Items.Select(x => x.BookingId).Intersect(second.Items.Select(x => x.BookingId)));
        Assert.Empty((await service.SearchCustomersAsync(".*", 1, default)).Items);
        Assert.Equal(25, (await service.SearchCustomersAsync("+63 (917) 555-0123", 1, default)).Items.Count);
        Assert.Single((await service.SearchCustomersAsync(rows[0].Reference.ToLowerInvariant(), 1, default)).Items);
        Assert.Equal(25, (await service.SearchCustomersAsync("SHARED@example.test", 1, default)).Items.Count);
        await Assert.ThrowsAsync<ReservationValidationException>(() => service.SearchCustomersAsync("x", 1, default));
    }
}
