using HoleInHub.Business.Reservations;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using HoleInHub.Tests.Integration.Fixtures;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Integration.Reservations;

[Trait("Category", "Integration")]
public sealed class BookingProcessTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    private sealed class ReviewTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 3, 1, 0, 0, TimeSpan.FromHours(8));
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task OvernightRequestHoldsBayAndWaitsUntilOpeningForAlertsAndExpiry()
    {
        var (store, _, context) = await Setup();
        await new Migration_014_OperatingReviewClock().UpAsync(context.Database, default);
        var time = new ReviewTime();
        var options = Microsoft.Extensions.Options.Options.Create(new ReservationPolicy
        {
            Approved = true,
            DurationsApproved = true,
            ReviewStart = TimeSpan.FromHours(10),
            ReviewEnd = TimeSpan.FromDays(1),
            ReviewDays = [0, 1, 2, 3, 4, 5, 6],
            StaffEmails = ["staff@example.test"]
        });
        var service = new ReservationService(store, new OutboxPersistence(context), options, time);
        var input = new SubmitReservationRequest
        {
            Date = new(2026, 11, 3),
            StartTime = new(11, 0),
            DurationMinutes = 60,
            Service = "Regular",
            ResourceIds = ["bay-2"],
            FirstName = "Alex",
            LastName = "Santos",
            Phone = "09175550123",
            PrivacyAccepted = true,
            IdempotencyKey = "overnight"
        };
        var booking = await service.SubmitAsync(input, "customer", "customer@example.test", default);
        Assert.Equal(new DateTime(2026, 11, 3, 2, 30, 0, DateTimeKind.Utc), booking.ReviewDeadlineUtc);
        Assert.Equal(new DateTime(2026, 11, 3, 2, 0, 0, DateTimeKind.Utc), booking.ReviewStartsUtc);
        Assert.Contains("bay-2", await store.OccupiedAsync(booking.StartUtc, booking.EndUtc, default));
        time.Now = time.Now.AddHours(8);
        await service.ProcessPendingAsync(default);
        var messages = context.Database.GetCollection<OutboxRecord>("outbox");
        Assert.Equal(0, await messages.CountDocumentsAsync(x => x.RequiredStatus == "Pending"));
        time.Now = time.Now.AddHours(1).AddMinutes(15);
        await service.ProcessPendingAsync(default);
        await service.ProcessPendingAsync(default);
        Assert.Single(await messages.Find(x => x.RequiredStatus == "Pending").ToListAsync());
        time.Now = time.Now.AddMinutes(15);
        await service.ProcessPendingAsync(default);
        Assert.Equal("Expired", (await store.ByIdAsync(booking.Id, default))!.Status);
        Assert.DoesNotContain("bay-2", await store.OccupiedAsync(booking.StartUtc, booking.EndUtc, default));
    }

    private async Task<(ReservationPersistence Store, EmailVerificationPersistence Guests, HoleInHub.Data.Context.IMongoDbContext Context)> Setup()
    {
        Assert.True(fixture.IsServerAvailable, "Start the local MongoDB replica set.");
        var context = fixture.CreateDbContext();
        await new Migration_002_Reservations().UpAsync(context.Database, default);
        await new Migration_004_BookingProcess().UpAsync(context.Database, default);
        await new AccountsMigration().UpAsync(context.Database, default);
        await new Migration_005_GoogleAccounts().UpAsync(context.Database, default);
        await new Migration_006_SchemaHardening().UpAsync(context.Database, default);
        return (new ReservationPersistence(context), new EmailVerificationPersistence(context), context);
    }
    private static ReservationRecord Request(string service = "Regular") => new()
    {
        AccountId = Guid.NewGuid().ToString("N"),
        Email = "guest@example.test",
        Reference = Guid.NewGuid().ToString("N"),
        IdempotencyKey = Guid.NewGuid().ToString("N"),
        SubmissionHash = "payload",
        PolicyVersion = 1,
        Service = service,
        Attendees = 1,
        ResourceIds = service == "WholeShop" ? [] : service == "VIP" ? ["vip-7"] : ["bay-2"],
        StartUtc = DateTime.UtcNow.Date.AddDays(2).AddHours(4),
        EndUtc = DateTime.UtcNow.Date.AddDays(2).AddHours(5),
        CreatedUtc = DateTime.UtcNow.Date.AddHours(4),
        ReviewDeadlineUtc = DateTime.UtcNow.Date.AddDays(1),
        Participants = (service == "WholeShop" ? new[] { "bay-2", "bay-3", "bay-4", "bay-5", "bay-6", "vip-7", "vip-8" } : service == "VIP" ? new[] { "vip-7" } : new[] { "bay-2" }).Select(id => new ParticipantRecord { ResourceId = id, Players = 1 }).ToArray(),
        RentalTotal = service == "WholeShop" ? 40000 : null,
        DepositAmount = service == "WholeShop" ? 20000 : null
    };
    [Fact]
    public async Task VerificationThrottlesExpiresAndLimitsAttempts()
    {
        var (_, guests, _) = await Setup(); var now = DateTime.UtcNow;
        var record = new GuestVerificationRecord { Id = "session", Email = "guest@example.test", CodeHash = "hash", SentUtc = now, ExpiresUtc = now.AddMinutes(10) };
        Assert.True(await guests.IssueAsync(record, new OutboxRecord { Id = "code", Recipient = "guest@example.test", DueUtc = now }, default));
        Assert.False(await guests.IssueAsync(record, new OutboxRecord { Id = "resend", Recipient = "guest@example.test" }, default));
        Assert.False(await guests.VerifyAsync("other", record.Email, "hash", now, default));
        for (var i = 0; i < 5; i++)
        {
            Assert.False(await guests.VerifyAsync(record.Id, record.Email, "wrong", now, default));
        }

        Assert.False(await guests.VerifyAsync(record.Id, record.Email, "hash", now, default));
        record.SentUtc = now.AddMinutes(2); record.ExpiresUtc = now.AddMinutes(12);
        Assert.True(await guests.IssueAsync(record, new OutboxRecord { Id = "new-code", Recipient = "guest@example.test" }, default));
        Assert.False(await guests.VerifyAsync(record.Id, record.Email, "hash", now.AddMinutes(13), default));
        Assert.True(await guests.VerifyAsync(record.Id, record.Email, "hash", now.AddMinutes(3), default));
        Assert.False(await guests.VerifyAsync(record.Id, record.Email, "hash", now.AddMinutes(3), default));
    }
    [Fact]
    public async Task GuestProofIsConsumedWithBookingAndRetryReturnsOriginal()
    {
        var (store, guests, context) = await Setup(); var now = DateTime.UtcNow;
        await guests.IssueAsync(new GuestVerificationRecord { Id = "session", Email = "guest@example.test", CodeHash = "hash", SentUtc = now, ExpiresUtc = now.AddMinutes(10) }, new OutboxRecord { Id = "code", Recipient = "guest@example.test" }, default);
        Assert.True(await guests.VerifyAsync("session", "guest@example.test", "hash", now, default));
        var request = Request(); request.EmailVerificationId = "session"; request.AccessTokenHash = "access-hash";
        await store.InsertAsync(request, default, "protected:receipt");
        Assert.Equal(request.Id, (await store.InsertAsync(request, default)).Id);
        var next = Request(); next.EmailVerificationId = "session"; next.ResourceIds = ["bay-3"]; next.Participants = [new ParticipantRecord { ResourceId = "bay-3", Players = 1 }];
        await Assert.ThrowsAsync<AllocationConflictException>(() => store.InsertAsync(next, default));
        Assert.Null(await store.ByAccessAsync(request.Id, "wrong", default));
        Assert.Null(await store.OwnedAsync("someone-else", request.Id, default));
        Assert.NotNull(await store.ByAccessAsync("", "access-hash", default));
        var receipt = await context.Database.GetCollection<OutboxRecord>("outbox").Find(x => x.Id == request.Id + ":submitted").SingleAsync();
        Assert.Equal("protected:receipt", receipt.Body);
    }
    [Fact]
    public async Task AllocationConflictDoesNotConsumeGuestVerification()
    {
        var (store, guests, _) = await Setup(); var existing = Request(); await store.InsertAsync(existing, default);
        var now = DateTime.UtcNow;
        await guests.IssueAsync(new GuestVerificationRecord { Id = "proof", Email = "guest@example.test", CodeHash = "hash", SentUtc = now, ExpiresUtc = now.AddMinutes(10) }, new OutboxRecord { Id = "code", Recipient = "guest@example.test" }, default);
        await guests.VerifyAsync("proof", "guest@example.test", "hash", now, default);
        var request = Request(); request.EmailVerificationId = "proof";
        await Assert.ThrowsAsync<AllocationConflictException>(() => store.InsertAsync(request, default));
        Assert.Null((await guests.FindAsync("proof", default))!.ConsumedBySubmissionKey);
    }
    [Fact]
    public async Task ApprovalIgnoresItsOwnHoldButRejectsANewScheduleBlock()
    {
        var (store, _, context) = await Setup();
        var booking = await store.InsertAsync(Request(), default);
        Assert.False(await store.HasReviewConflictAsync(booking, default));
        await context.Database.GetCollection<ScheduleBlock>("schedule_blocks").InsertOneAsync(new()
        {
            Id = "maintenance",
            ResourceIds = booking.ResourceIds,
            StartUtc = booking.StartUtc,
            EndUtc = booking.EndUtc
        });

        Assert.True(await store.HasReviewConflictAsync(booking, default));
        await Assert.ThrowsAsync<AllocationConflictException>(() =>
            store.TransitionAsync(booking.Id, "approve", "staff", DateTime.UtcNow, [], default));
        Assert.Equal("Pending", (await store.ByIdAsync(booking.Id, default))!.Status);
    }

    [Fact]
    public async Task CoachAndVenueApprovalsAreBothRequired()
    {
        var (store, _, _) = await Setup(); var request = Request("KpgaPro"); await store.InsertAsync(request, default);
        Assert.False(await store.TransitionAsync(request.Id, "approve", "staff", DateTime.UtcNow, [], default));
        Assert.True(await store.TransitionAsync(request.Id, "coach", "staff", DateTime.UtcNow, [], default));
        Assert.True(await store.TransitionAsync(request.Id, "approve", "staff", DateTime.UtcNow, [], default));
        Assert.Equal("Confirmed", (await store.ByIdAsync(request.Id, default))!.Status);
    }
    [Fact]
    public async Task CancellationKeepsAllocationUntilStaffApproves()
    {
        var (store, _, _) = await Setup(); var request = Request(); await store.InsertAsync(request, default);
        Assert.True(await store.TransitionAsync(request.Id, "request-cancellation", "Customer", DateTime.UtcNow, [], default));
        await Assert.ThrowsAsync<AllocationConflictException>(() => store.InsertAsync(Request(), default));
        Assert.True(await store.TransitionAsync(request.Id, "cancel", "staff", DateTime.UtcNow, [], default));
        await store.InsertAsync(Request(), default);
    }
    [Fact]
    public async Task CancellationCutoffIsEnforcedByPersistence()
    {
        var (store, _, _) = await Setup(); var request = Request(); await store.InsertAsync(request, default);
        Assert.False(await store.TransitionAsync(request.Id, "request-cancellation", "Customer", request.StartUtc.AddMinutes(-59), [], default));
        Assert.True(await store.TransitionAsync(request.Id, "request-cancellation", "Customer", request.StartUtc.AddHours(-1), [], default));
    }
    [Fact]
    public async Task DemoDepositConfirmsExactlyOnceAndCannotBeStaffApproved()
    {
        var (store, _, context) = await Setup(); var request = Request("WholeShop"); await store.InsertAsync(request, default);
        Assert.False(await store.TransitionAsync(request.Id, "approve", "staff", DateTime.UtcNow, [], default));
        Assert.True(await store.ApplyDemoPaymentAsync(request.Id, "failure", "demo", DateTime.UtcNow, default));
        Assert.Equal("Pending", (await store.ByIdAsync(request.Id, default))!.Status);
        Assert.True(await store.ApplyDemoPaymentAsync(request.Id, "cancel", "demo", DateTime.UtcNow, default));
        var results = await Task.WhenAll(store.ApplyDemoPaymentAsync(request.Id, "success", "demo", DateTime.UtcNow, default), store.ApplyDemoPaymentAsync(request.Id, "success", "demo", DateTime.UtcNow, default));
        Assert.All(results, Assert.True);
        var saved = (await store.ByIdAsync(request.Id, default))!;
        Assert.Equal("Confirmed", saved.Status); Assert.Equal("DepositReceived", saved.PaymentStatus); Assert.Equal(20000, saved.DepositAmount);
        Assert.Single(saved.History, x => x.Status == "Demo payment: DepositReceived");
        Assert.Equal(1, await context.Database.GetCollection<OutboxRecord>("outbox").CountDocumentsAsync(x => x.Id == request.Id + ":DepositReceived"));
    }
    [Fact]
    public async Task LatePaymentNeverDisplacesReplacementBooking()
    {
        var (store, _, _) = await Setup(); var request = Request("WholeShop"); request.ReviewDeadlineUtc = DateTime.UtcNow.AddSeconds(-1); await store.InsertAsync(request, default);
        await store.ExpireAsync(DateTime.UtcNow, [], default); await store.InsertAsync(Request(), default);
        Assert.False(await store.ApplyDemoPaymentAsync(request.Id, "success", "late", DateTime.UtcNow, default));
        var saved = (await store.ByIdAsync(request.Id, default))!; Assert.Equal("Expired", saved.Status); Assert.Equal("LatePayment", saved.PaymentStatus);
    }
    [Fact]
    public async Task ExpiryAndApprovalHaveOneFinalOutcome()
    {
        var (store, _, _) = await Setup(); var request = Request(); await store.InsertAsync(request, default);
        await Task.WhenAll(store.TransitionAsync(request.Id, "approve", "staff", request.ReviewDeadlineUtc.AddTicks(-1), [], default), store.ExpireAsync(request.ReviewDeadlineUtc, [], default));
        var saved = (await store.ByIdAsync(request.Id, default))!;
        Assert.Contains(saved.Status, new[] { "Confirmed", "Expired" });
        Assert.Single(saved.History, x => x.Status is "Confirmed" or "Expired");
    }
    [Fact]
    public async Task ExpiryAndPaymentHaveOneFinalAllocationOutcome()
    {
        var (store, _, _) = await Setup(); var request = Request("WholeShop"); await store.InsertAsync(request, default);
        await Task.WhenAll(store.ApplyDemoPaymentAsync(request.Id, "success", "demo", request.ReviewDeadlineUtc.AddTicks(-1), default), store.ExpireAsync(request.ReviewDeadlineUtc, [], default));
        var saved = (await store.ByIdAsync(request.Id, default))!;
        Assert.True(saved.Status == "Confirmed" && saved.PaymentStatus == "DepositReceived" || saved.Status == "Expired" && saved.PaymentStatus == "LatePayment");
    }
    [Fact]
    public async Task CatalogMigrationRetainsIdsAndLegacyCounts()
    {
        var (store, _, context) = await Setup(); await new Migration_004_BookingProcess().UpAsync(context.Database, default);
        var catalog = await store.CatalogAsync(default); Assert.Equal(7, catalog.Count); Assert.DoesNotContain(catalog, x => x.Id == "bay-1");
        Assert.Contains(catalog, x => x.Id == "bay-2" && x.Name == "Bay 1"); Assert.Contains(catalog, x => x.Id == "vip-7" && x.Name == "VIP 1");
        var legacy = Request(); legacy.PolicyVersion = 0; legacy.Attendees = 4; legacy.Participants = []; await store.InsertAsync(legacy, default);
        var saved = (await store.ByIdAsync(legacy.Id, default))!; Assert.Equal(4, saved.Attendees); Assert.Empty(saved.Participants);
    }
    [Fact]
    public async Task CatalogMigrationRefusesToStrandActiveRetiredBay()
    {
        Assert.True(fixture.IsServerAvailable); var context = fixture.CreateDbContext(); await new Migration_002_Reservations().UpAsync(context.Database, default);
        var store = new ReservationPersistence(context); var request = Request(); request.ResourceIds = ["bay-1"]; request.Participants = [new ParticipantRecord { ResourceId = "bay-1", Players = 1 }]; await store.InsertAsync(request, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new Migration_004_BookingProcess().UpAsync(context.Database, default));
    }
    [Fact]
    public async Task CatalogChangeRequiresWholeShopParticipantCountsToBeReviewed()
    {
        var (store, _, _) = await Setup(); var request = Request("WholeShop");
        await store.AddResourceAsync(new ResourceRecord { Id = "bay-9", Name = "Added bay", Service = "Regular" }, default);
        await Assert.ThrowsAsync<AllocationConflictException>(() => store.InsertAsync(request, default));
    }
}
