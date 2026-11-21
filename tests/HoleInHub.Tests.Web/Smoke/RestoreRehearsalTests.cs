using System.Globalization;
using HoleInHub.Business.Reservations;
using HoleInHub.Data.Context;
using HoleInHub.Data.Customers;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed partial class StaffBlockHttpTests
{
    private const string RestoreDatabaseVariable = "HOLEINHUB_RESTORE_TEST_DATABASE";

    private sealed class RestoreFactAttribute : FactAttribute
    {
        public RestoreFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RestoreDatabaseVariable)))
            {
                Skip = "Requires an isolated local restore prepared by the backup rehearsal.";
            }
        }
    }

    [RestoreFact, Trait("Category", "Integration")]
    public async Task RestoredBookingResolvesItsCustomerHistoryAndExportsThroughReports()
    {
        var database = Environment.GetEnvironmentVariable(RestoreDatabaseVariable)!;
        Assert.Matches(@"^test_restore_[A-Za-z0-9_]+$", database);
        await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        Assert.Equal(database, context.Database.DatabaseNamespace.DatabaseName);
        var memberships = context.Database.GetCollection<CustomerMembershipRecord>(CustomerProfilePersistence.MembershipsName);
        var member = await memberships.Find(Builders<CustomerMembershipRecord>.Filter.Empty).SortBy(x => x.Id).FirstOrDefaultAsync();
        Assert.NotNull(member);
        var reservations = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var booking = await reservations.GetStaffAsync(member.Id, default);
        Assert.NotNull(booking);
        Assert.False(string.IsNullOrWhiteSpace(booking.Reference));
        var byBooking = await reservations.GetCustomerProfileAsync(booking.Id, false, 1, default);
        var byProfile = await reservations.GetCustomerProfileAsync(member.ProfileId, false, 1, default);
        Assert.NotNull(byBooking);
        Assert.NotNull(byProfile);
        Assert.Matches("^CUS-[0-9A-F]{16}$", byProfile.Customer.CustomerId ?? "");
        Assert.Equal(byProfile.Customer.CustomerId, byBooking.Customer.CustomerId);
        var page = byProfile;
        while (page.HasMore && page.Page < 1000 && page.Bookings.All(row => row.Id != booking.Id))
        {
            page = await reservations.GetCustomerProfileAsync(member.ProfileId, false, page.Page + 1, default);
            Assert.NotNull(page);
        }
        Assert.Contains(page.Bookings, row => row.Id == booking.Id && row.Reference == booking.Reference);

        using var client = Client(factory, "Manager");
        var date = DashboardMetrics.Local(booking.StartUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var csv = await ReadReportCsv(client, $"?from={date}&to={date}");
        var row = Assert.Single(csv, fields => fields[0] == booking.Reference);
        Assert.Equal(booking.Status, row[6]);
    }
}
