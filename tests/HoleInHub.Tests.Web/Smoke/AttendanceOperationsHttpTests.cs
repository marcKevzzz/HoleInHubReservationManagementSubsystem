using System.Net;
using System.Text.RegularExpressions;
using HoleInHub.Data.Context;
using HoleInHub.Data.Migrations.Definitions;
using HoleInHub.Data.Reservations;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit;

namespace HoleInHub.Tests.Web.Smoke;

public sealed partial class StaffBlockHttpTests
{
    [Fact, Trait("Category", "Integration")]
    public async Task NoShowCorrectionAndExtensionRequireConfirmationAndWorkThroughStaffForms()
    {
        var database = "test_att_ops_" + Guid.NewGuid().ToString("N");
        await using var factory = Factory(database);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IMongoDbContext>();
        try
        {
            await new Migration_002_Reservations().UpAsync(context.Database, default);
            await new AccountsMigration().UpAsync(context.Database, default);
            await new Migration_004_BookingProcess().UpAsync(context.Database, default);
            await new Migration_005_GoogleAccounts().UpAsync(context.Database, default);
            await new Migration_006_SchemaHardening().UpAsync(context.Database, default);
            await new Migration_013_ReservationAttendance().UpAsync(context.Database, default);
            await new Migration_020_AttendanceOperations().UpAsync(context.Database, default);
            var start = new DateTime(2026, 11, 10, 3, 0, 0, DateTimeKind.Utc);
            var booking = new ReservationRecord
            {
                AccountId = "customer",
                Email = "customer@example.test",
                Reference = "HIH-ATTENDANCE-OPS",
                FirstName = "Alex",
                LastName = "Santos",
                Service = "Regular",
                Status = "Confirmed",
                ResourceIds = ["bay-2"],
                StartUtc = start,
                EndUtc = start.AddHours(2),
                CreatedUtc = start.AddDays(-1),
                ReviewDeadlineUtc = start,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                SubmissionHash = "test",
                Attendees = 1
            };
            var records = context.Database.GetCollection<ReservationRecord>("reservations");
            await records.InsertOneAsync(booking);
            using var staff = Client(factory, "Staff");
            var html = await staff.GetStringAsync("/BookingReview/Details/" + booking.Id);
            Assert.Contains("Mark as no-show", html);
            Assert.Contains("Correct attendance", html);
            Assert.Contains("Extend session", html);
            var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            Assert.NotEmpty(token);
            var path = "/staff/reservations/" + booking.Id + "/attendance";
            var form = new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Action"] = "no-show",
                ["CommandId"] = Guid.NewGuid().ToString("N"),
                ["ExpectedRevision"] = "0",
                ["Reason"] = "Called guest",
                ["ConfirmAttendance"] = "false"
            };
            Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync(path, new FormUrlEncodedContent(form))).StatusCode);
            Assert.Equal("Confirmed", (await records.Find(x => x.Id == booking.Id).SingleAsync()).Status);
            form["ConfirmAttendance"] = "true";
            Assert.Equal(HttpStatusCode.Redirect, (await staff.PostAsync(path, new FormUrlEncodedContent(form))).StatusCode);
            Assert.Equal("No-show", (await records.Find(x => x.Id == booking.Id).SingleAsync()).Status);
            form["Action"] = "correct"; form["ExpectedRevision"] = "1"; form["CommandId"] = Guid.NewGuid().ToString("N");
            form["Reason"] = "Arrival recorded incorrectly"; form["CorrectedStatus"] = "Checked-In";
            form["ArrivalLocal"] = "2026-11-10T11:30";
            Assert.Equal(HttpStatusCode.Redirect, (await staff.PostAsync(path, new FormUrlEncodedContent(form))).StatusCode);
            var corrected = await records.Find(x => x.Id == booking.Id).SingleAsync();
            Assert.Equal("Checked-In", corrected.Status);
            Assert.Equal(start.AddMinutes(30), corrected.CheckedInUtc);
            form.Remove("CorrectedStatus"); form.Remove("ArrivalLocal");
            form["Action"] = "extend"; form["ExpectedRevision"] = "2"; form["CommandId"] = Guid.NewGuid().ToString("N");
            form["Reason"] = "Customer agreed"; form["ExtensionMinutes"] = "30";
            Assert.Equal(HttpStatusCode.Redirect, (await staff.PostAsync(path, new FormUrlEncodedContent(form))).StatusCode);
            var extended = await records.Find(x => x.Id == booking.Id).SingleAsync();
            Assert.Equal(booking.EndUtc.AddMinutes(30), extended.EndUtc);
            Assert.Equal(3, extended.Revision);
            Assert.All(extended.History, entry => Assert.Equal("test-admin", entry.Actor));
        }
        finally { await context.Client.DropDatabaseAsync(database); }
    }
}
